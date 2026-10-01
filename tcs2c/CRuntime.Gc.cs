namespace TinyCs.Tcs2c;

// C runtime の GC 節: mark-sweep (非移動)。heap は精密 (全 heap object が
// kind + TcsLayout を持ち、生成側が class / struct の pointer map を出す)、
// root は static field (生成側の tcs_gc_mark_statics) と C stack の保守的
// 走査 (setjmp で callee-saved register も stack 上に落とす)。stack 由来の
// 候補 word は interior pointer を許す (生成コードは tcs_array_at 等の
// 要素 pointer を temp に持つため)。
internal sealed partial class CEmitter
{
    private const string RuntimeGc = """
        /* ---- GC ---- */
        #ifndef TCS_GC_MIN_THRESHOLD
        #define TCS_GC_MIN_THRESHOLD ((size_t)1 << 20)
        #endif

        static const uint32_t tcs_layout_ptr_offsets[1] = { 0 };
        static const TcsLayout tcs_layout_ptr =
            { sizeof(void *), 1, tcs_layout_ptr_offsets };

        static TcsGcHeader *tcs_gc_objects;     /* 全 heap object の連結 list */
        static size_t tcs_gc_object_count;
        static size_t tcs_gc_live_bytes;        /* 直近 GC 後の生存 bytes */
        static size_t tcs_gc_allocated_since;   /* 直近 GC 以降の確保 bytes */
        static size_t tcs_gc_threshold = TCS_GC_MIN_THRESHOLD;
        static size_t tcs_gc_collections;
        static void *tcs_gc_stack_base;         /* entry で記録 (NULL = GC 不可) */
        static int tcs_gc_collecting;
        #ifdef TCS_GC_STRESS
        static size_t tcs_gc_stress_counter;
        #endif

        static TcsGcHeader **tcs_gc_mark_stack;
        static size_t tcs_gc_mark_count;
        static size_t tcs_gc_mark_capacity;
        static TcsGcHeader **tcs_gc_sorted;     /* 保守的走査用の address 順 index */
        static size_t tcs_gc_sorted_count;

        static void
        tcs_gc_push(TcsGcHeader *hdr)
        {
            if (hdr->flags & (TCS_GC_MARK | TCS_GC_STATIC)) return;
            hdr->flags |= TCS_GC_MARK;
            if (tcs_gc_mark_count == tcs_gc_mark_capacity) {
                size_t capacity = tcs_gc_mark_capacity == 0
                    ? 1024 : tcs_gc_mark_capacity * 2;
                TcsGcHeader **grown = realloc(tcs_gc_mark_stack,
                    capacity * sizeof(*grown));
                if (grown == NULL) tcs_fault("allocation");
                tcs_gc_mark_stack = grown;
                tcs_gc_mark_capacity = capacity;
            }
            tcs_gc_mark_stack[tcs_gc_mark_count++] = hdr;
        }

        /* 精密 pointer (object 先頭を指す) の mark */
        static void
        tcs_gc_mark_ptr(const void *pointer)
        {
            if (pointer != NULL) tcs_gc_push(TCS_GC_HEADER(pointer));
        }

        static void
        tcs_gc_mark_value(const void *value, const TcsLayout *layout)
        {
            uint32_t i;
            if (layout == NULL) return;
            for (i = 0; i < layout->count; i++) {
                void *pointer;
                memcpy(&pointer, (const unsigned char *)value + layout->offsets[i],
                    sizeof(pointer));
                tcs_gc_mark_ptr(pointer);
            }
        }

        static void
        tcs_gc_trace(TcsGcHeader *hdr)
        {
            void *payload = TCS_GC_PAYLOAD(hdr);
            size_t i;
            switch (hdr->kind) {
            case TCS_KIND_OBJECT:
                tcs_gc_mark_value(payload, hdr->layout);
                break;
            case TCS_KIND_ARRAY: {
                TcsArray *array = payload;
                if (hdr->layout == NULL) break;
                for (i = 0; i < array->length; i++)
                    tcs_gc_mark_value(array->data + i * array->element_size,
                        hdr->layout);
                break;
            }
            case TCS_KIND_LIST: {
                TcsList *list = payload;
                tcs_gc_mark_ptr(list->data);
                if (hdr->layout == NULL || list->data == NULL) break;
                for (i = 0; i < list->length; i++)
                    tcs_gc_mark_value((unsigned char *)list->data
                        + i * list->element_size, hdr->layout);
                break;
            }
            case TCS_KIND_DICT: {
                TcsDict *dict = payload;
                tcs_gc_mark_ptr(dict->buckets);
                for (i = 0; i < dict->bucket_count; i++)
                    tcs_gc_mark_ptr(dict->buckets[i]);
                break;
            }
            case TCS_KIND_DICT_NODE: {
                TcsDictNode *node = payload;
                tcs_gc_mark_ptr(node->next);
                tcs_gc_mark_ptr(node->key_s);
                tcs_gc_mark_value(node->value, hdr->layout);
                break;
            }
            case TCS_KIND_CLOSURE: {
                TcsClosure *closure = payload;
                size_t cells = (hdr->size - sizeof(TcsClosure)) / sizeof(void *);
                for (i = 0; i < cells; i++) tcs_gc_mark_ptr(closure->cells[i]);
                break;
            }
            default:
                break;
            }
        }

        static int
        tcs_gc_compare(const void *left, const void *right)
        {
            uintptr_t a = (uintptr_t)*(TcsGcHeader *const *)left;
            uintptr_t b = (uintptr_t)*(TcsGcHeader *const *)right;
            return a < b ? -1 : a > b ? 1 : 0;
        }

        /* 保守的候補: payload 内 (末尾 1 過ぎを含む) を指す word なら mark */
        static void
        tcs_gc_mark_conservative(uintptr_t word)
        {
            size_t lo = 0;
            size_t hi = tcs_gc_sorted_count;
            TcsGcHeader *hdr;
            uintptr_t start;
            while (lo < hi) {
                size_t mid = lo + (hi - lo) / 2;
                if ((uintptr_t)TCS_GC_PAYLOAD(tcs_gc_sorted[mid]) <= word)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            if (lo == 0) return;
            hdr = tcs_gc_sorted[lo - 1];
            start = (uintptr_t)TCS_GC_PAYLOAD(hdr);
            if (word >= start && word <= start + hdr->size) tcs_gc_push(hdr);
        }

        static void TCS_NO_ASAN
        tcs_gc_scan_range(const void *from, const void *to)
        {
            uintptr_t lo = (uintptr_t)from;
            uintptr_t hi = (uintptr_t)to;
            uintptr_t p;
            if (lo > hi) { uintptr_t t = lo; lo = hi; hi = t; }
            lo &= ~(uintptr_t)(sizeof(void *) - 1);
            for (p = lo; p + sizeof(void *) <= hi; p += sizeof(void *)) {
                uintptr_t word;
                memcpy(&word, (const void *)p, sizeof(word));
                if (word != 0) tcs_gc_mark_conservative(word);
            }
        }

        static void __attribute__((noinline)) TCS_NO_ASAN
        tcs_gc_scan_stack(void)
        {
            jmp_buf registers;
            setjmp(registers);
            tcs_gc_scan_range(&registers, (unsigned char *)&registers
                + sizeof(registers));
            /* この frame から entry の frame まで (sanitizer の fake stack に
               依らず実 stack を取るため __builtin_frame_address) */
            tcs_gc_scan_range(__builtin_frame_address(0), tcs_gc_stack_base);
        }

        static void __attribute__((noinline))
        tcs_gc_collect(void)
        {
            TcsGcHeader *hdr;
            TcsGcHeader **link;
            size_t i = 0;
            size_t live = 0;
            size_t survivors = 0;
            if (tcs_gc_collecting || tcs_gc_stack_base == NULL) return;
            tcs_gc_collecting = 1;

            tcs_gc_sorted = malloc((tcs_gc_object_count + 1) * sizeof(*tcs_gc_sorted));
            if (tcs_gc_sorted == NULL) tcs_fault("allocation");
            for (hdr = tcs_gc_objects; hdr != NULL; hdr = hdr->next)
                tcs_gc_sorted[i++] = hdr;
            tcs_gc_sorted_count = i;
            qsort(tcs_gc_sorted, i, sizeof(*tcs_gc_sorted), tcs_gc_compare);

            tcs_gc_mark_count = 0;
            tcs_gc_mark_statics();
            tcs_gc_scan_stack();
            while (tcs_gc_mark_count > 0)
                tcs_gc_trace(tcs_gc_mark_stack[--tcs_gc_mark_count]);

            link = &tcs_gc_objects;
            while (*link != NULL) {
                hdr = *link;
                if (hdr->flags & TCS_GC_MARK) {
                    hdr->flags &= ~(uint32_t)TCS_GC_MARK;
                    live += sizeof(*hdr) + hdr->size;
                    survivors++;
                    link = &hdr->next;
                } else {
                    *link = hdr->next;
                    free(hdr);
                }
            }
            free(tcs_gc_sorted);
            tcs_gc_sorted = NULL;
            tcs_gc_sorted_count = 0;
            tcs_gc_object_count = survivors;
            tcs_gc_live_bytes = live;
            tcs_gc_allocated_since = 0;
            tcs_gc_threshold = live > TCS_GC_MIN_THRESHOLD
                ? live : TCS_GC_MIN_THRESHOLD;
            tcs_gc_collections++;
            tcs_gc_collecting = 0;
        }

        static void *
        tcs_gc_alloc(uint32_t kind, const TcsLayout *layout, size_t size)
        {
            TcsGcHeader *hdr;
        #ifdef TCS_GC_STRESS
            if (tcs_gc_stress_counter++ % (TCS_GC_STRESS) == 0) tcs_gc_collect();
        #else
            if (tcs_gc_allocated_since >= tcs_gc_threshold) tcs_gc_collect();
        #endif
            if (size > SIZE_MAX - sizeof(*hdr)) tcs_fault("allocation-overflow");
            hdr = calloc(1, sizeof(*hdr) + size);
            if (hdr == NULL) {
                tcs_gc_collect();
                hdr = calloc(1, sizeof(*hdr) + size);
                if (hdr == NULL) tcs_fault("allocation");
            }
            hdr->next = tcs_gc_objects;
            hdr->layout = layout;
            hdr->size = size;
            hdr->kind = kind;
            tcs_gc_objects = hdr;
            tcs_gc_object_count++;
            tcs_gc_allocated_since += sizeof(*hdr) + size;
            return TCS_GC_PAYLOAD(hdr);
        }

        static void *
        tcs_alloc(size_t size)
        {
            return tcs_gc_alloc(TCS_KIND_RAW, NULL, size);
        }

        static void *
        tcs_new_object(const TcsLayout *layout)
        {
            return tcs_gc_alloc(TCS_KIND_OBJECT, layout, layout->size);
        }

        static void *
        tcs_new_cell(size_t size, const TcsLayout *layout)
        {
            return tcs_gc_alloc(TCS_KIND_OBJECT, layout, size);
        }

        static TcsClosure *
        tcs_new_closure(void *fn, size_t cells)
        {
            TcsClosure *closure = tcs_gc_alloc(TCS_KIND_CLOSURE, NULL,
                sizeof(*closure) + cells * sizeof(void *));
            closure->fn = fn;
            return closure;
        }

        /* entry 境界: C stack の底を記録する (再入は外側の底を保つ) */
        static void *
        tcs_gc_enter(void *frame)
        {
            void *saved = tcs_gc_stack_base;
            if (saved == NULL) tcs_gc_stack_base = frame;
            return saved;
        }

        static void
        tcs_gc_leave(void *saved)
        {
            tcs_gc_stack_base = saved;
        }

        """;
}
