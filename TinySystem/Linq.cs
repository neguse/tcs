namespace TinySystem.Linq;

// List<T> の LINQ (即時評価)。System.Linq の代わりに `using TinySystem.Linq;`
// で使う。TinySystem 直下に置かないのは、`using System;` と併用したとき
// Math / String / Random が System 側の型と曖昧 (CS0104) になるため。
// 列を返す演算は List<T> を返し、runtime (runtime/tinysystem.lua の List) と
// tcs2c (CEmitter.Linq) の即時評価と同じ結果になる。Enumerable には委譲せず
// 添字で走る: 走査は呼び出し時点の Count まで、要素は callback の前に読む
// (callback が受け手を変えても結果は呼び出し時点の内容で決まる。q.md Q18)。
// 順は string が ordinal、float の NaN は最小 (Comparer<float>.Default)
public static class List
{
    public static global::System.Collections.Generic.List<T> Where<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate)
    {
        var count = list.Count;
        var result = new global::System.Collections.Generic.List<T>();
        for (var i = 0; i < count; i++)
        {
            var item = list[i];
            if (predicate(item)) result.Add(item);
        }
        return result;
    }
    public static global::System.Collections.Generic.List<TResult>
        Select<T, TResult>(
            this global::System.Collections.Generic.List<T> list,
            global::System.Func<T, TResult> selector)
    {
        var count = list.Count;
        var result = new global::System.Collections.Generic.List<TResult>(count);
        for (var i = 0; i < count; i++) result.Add(selector(list[i]));
        return result;
    }
    public static bool Any<T>(
        this global::System.Collections.Generic.List<T> list) => list.Count > 0;
    public static bool Any<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate)
    {
        var count = list.Count;
        for (var i = 0; i < count; i++)
            if (predicate(list[i])) return true;
        return false;
    }
    public static bool All<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate)
    {
        var count = list.Count;
        for (var i = 0; i < count; i++)
            if (!predicate(list[i])) return false;
        return true;
    }
    public static int Count<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate)
    {
        var count = list.Count;
        var n = 0;
        for (var i = 0; i < count; i++)
            if (predicate(list[i])) n++;
        return n;
    }
    public static T? FirstOrDefault<T>(
        this global::System.Collections.Generic.List<T> list) =>
        list.Count == 0 ? default : list[0];
    public static T? FirstOrDefault<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate)
    {
        var count = list.Count;
        for (var i = 0; i < count; i++)
        {
            var item = list[i];
            if (predicate(item)) return item;
        }
        return default;
    }
    public static global::System.Collections.Generic.List<T> OrderBy<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector) =>
        Order(list, keySelector, descending: false);
    public static global::System.Collections.Generic.List<T>
        OrderByDescending<T, TKey>(
            this global::System.Collections.Generic.List<T> list,
            global::System.Func<T, TKey> keySelector) =>
        Order(list, keySelector, descending: true);
    // 安定 sort: 要素を全て写してから key を要素ごとに 1 回・要素の順に計算し
    // (Lua / C runtime と同じ)、同じ key は元の順
    private static global::System.Collections.Generic.List<T> Order<T, TKey>(
        global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector, bool descending)
    {
        var count = list.Count;
        var items = new T[count];
        list.CopyTo(items);
        var keys = new TKey[count];
        for (var i = 0; i < count; i++) keys[i] = keySelector(items[i]);
        var index = new int[count];
        for (var i = 0; i < count; i++) index[i] = i;
        global::System.Array.Sort(index, (a, b) =>
        {
            var c = Compare(keys[a], keys[b]);
            return c != 0 ? (descending ? -c : c) : a.CompareTo(b);
        });
        var result = new global::System.Collections.Generic.List<T>(count);
        for (var i = 0; i < count; i++) result.Add(items[index[i]]);
        return result;
    }
    public static T Min<T>(
        this global::System.Collections.Generic.List<T> list) =>
        Min(list, static x => x);
    public static T Max<T>(
        this global::System.Collections.Generic.List<T> list) =>
        Max(list, static x => x);
    public static int Sum(
        this global::System.Collections.Generic.List<int> list) =>
        Sum(list, static x => x);
    public static float Sum(
        this global::System.Collections.Generic.List<float> list) =>
        Sum(list, static x => x);
    // Min は NaN を見た時点で NaN (以降の selector は呼ばない)、Max は NaN 以外が
    // 1 つでもあればその最大 (Lua / C runtime と同じ。NaN は最小の値なので
    // Compare だけで決まる)。等しい値同士 (±0) は先の方を残す
    public static TKey Min<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> selector)
    {
        var count = list.Count;
        if (count == 0)
            throw new global::System.InvalidOperationException(
                "Sequence contains no elements");
        var best = selector(list[0]);
        if (IsNaN(best)) return best;
        for (var i = 1; i < count; i++)
        {
            var key = selector(list[i]);
            if (IsNaN(key)) return key;
            if (Compare(key, best) < 0) best = key;
        }
        return best;
    }
    public static TKey Max<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> selector)
    {
        var count = list.Count;
        if (count == 0)
            throw new global::System.InvalidOperationException(
                "Sequence contains no elements");
        var best = selector(list[0]);
        for (var i = 1; i < count; i++)
        {
            var key = selector(list[i]);
            if (Compare(best, key) < 0) best = key;
        }
        return best;
    }
    private static bool IsNaN<TKey>(TKey key) =>
        key is float f && float.IsNaN(f);
    // Lua / C runtime の順: string は ordinal、それ以外は Comparer<TKey>.Default
    // (float の NaN はどの値より小さい)
    private static int Compare<TKey>(TKey a, TKey b) =>
        a is string sa && b is string sb
            ? string.CompareOrdinal(sa, sb)
            : global::System.Collections.Generic.Comparer<TKey>.Default.Compare(a, b);
    // int の和は checked (途中の overflow で例外)、float の和は double で足して float に丸める
    // (Enumerable.Sum と同じ)
    public static int Sum<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, int> selector)
    {
        var count = list.Count;
        var total = 0;
        for (var i = 0; i < count; i++) total = checked(total + selector(list[i]));
        return total;
    }
    public static float Sum<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, float> selector)
    {
        var count = list.Count;
        double total = 0;
        for (var i = 0; i < count; i++) total += selector(list[i]);
        return (float)total;
    }
    // 空列は Min / Max と同じく例外 (.NET の MinBy は参照型で default を返すが、
    // Lua / C の fault と揃える)。同じ key は先の要素、NaN の key は最小
    public static T MinBy<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector) =>
        By(list, keySelector, max: false);
    public static T MaxBy<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector) =>
        By(list, keySelector, max: true);
    private static T By<T, TKey>(
        global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector, bool max)
    {
        var count = list.Count;
        if (count == 0)
            throw new global::System.InvalidOperationException(
                "Sequence contains no elements");
        var best = list[0];
        var bestKey = keySelector(best);
        for (var i = 1; i < count; i++)
        {
            var item = list[i];
            var key = keySelector(item);
            var c = Compare(key, bestKey);
            if (max ? c > 0 : c < 0)
            {
                best = item;
                bestKey = key;
            }
        }
        return best;
    }

    // Dictionary の Keys / Values (KeyCollection / ValueCollection) は List の LINQ への橋
    // (q.md Q18)。列挙順は未規定なので、並べるなら OrderBy を通す。Lua / C は
    // Dict.Keys / Values が最初から copy の List なので同じ List の演算に写る
    public static global::System.Collections.Generic.List<TKey> Where<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, bool> predicate)
        where TKey : notnull =>
        Where(new global::System.Collections.Generic.List<TKey>(xs), predicate);
    public static global::System.Collections.Generic.List<TResult> Select<TKey, TValue, TResult>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, TResult> selector)
        where TKey : notnull =>
        Select(new global::System.Collections.Generic.List<TKey>(xs), selector);
    public static bool Any<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs)
        where TKey : notnull =>
        xs.Count > 0;
    public static bool Any<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, bool> predicate)
        where TKey : notnull =>
        Any(new global::System.Collections.Generic.List<TKey>(xs), predicate);
    public static bool All<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, bool> predicate)
        where TKey : notnull =>
        All(new global::System.Collections.Generic.List<TKey>(xs), predicate);
    public static int Count<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, bool> predicate)
        where TKey : notnull =>
        Count(new global::System.Collections.Generic.List<TKey>(xs), predicate);
    public static TKey? FirstOrDefault<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs)
        where TKey : notnull =>
        FirstOrDefault(new global::System.Collections.Generic.List<TKey>(xs));
    public static TKey? FirstOrDefault<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, bool> predicate)
        where TKey : notnull =>
        FirstOrDefault(new global::System.Collections.Generic.List<TKey>(xs), predicate);
    public static global::System.Collections.Generic.List<TKey> OrderBy<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, TK> keySelector)
        where TKey : notnull =>
        OrderBy(new global::System.Collections.Generic.List<TKey>(xs), keySelector);
    public static global::System.Collections.Generic.List<TKey> OrderByDescending<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, TK> keySelector)
        where TKey : notnull =>
        OrderByDescending(new global::System.Collections.Generic.List<TKey>(xs), keySelector);
    public static TKey Min<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs)
        where TKey : notnull =>
        Min(new global::System.Collections.Generic.List<TKey>(xs));
    public static TKey Max<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs)
        where TKey : notnull =>
        Max(new global::System.Collections.Generic.List<TKey>(xs));
    public static TK Min<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, TK> selector)
        where TKey : notnull =>
        Min(new global::System.Collections.Generic.List<TKey>(xs), selector);
    public static TK Max<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, TK> selector)
        where TKey : notnull =>
        Max(new global::System.Collections.Generic.List<TKey>(xs), selector);
    public static TKey MinBy<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, TK> keySelector)
        where TKey : notnull =>
        MinBy(new global::System.Collections.Generic.List<TKey>(xs), keySelector);
    public static TKey MaxBy<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, TK> keySelector)
        where TKey : notnull =>
        MaxBy(new global::System.Collections.Generic.List<TKey>(xs), keySelector);
    public static int Sum<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, int> selector)
        where TKey : notnull =>
        Sum(new global::System.Collections.Generic.List<TKey>(xs), selector);
    public static float Sum<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection xs, global::System.Func<TKey, float> selector)
        where TKey : notnull =>
        Sum(new global::System.Collections.Generic.List<TKey>(xs), selector);
    public static global::System.Collections.Generic.List<TValue> Where<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, bool> predicate)
        where TKey : notnull =>
        Where(new global::System.Collections.Generic.List<TValue>(xs), predicate);
    public static global::System.Collections.Generic.List<TResult> Select<TKey, TValue, TResult>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, TResult> selector)
        where TKey : notnull =>
        Select(new global::System.Collections.Generic.List<TValue>(xs), selector);
    public static bool Any<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs)
        where TKey : notnull =>
        xs.Count > 0;
    public static bool Any<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, bool> predicate)
        where TKey : notnull =>
        Any(new global::System.Collections.Generic.List<TValue>(xs), predicate);
    public static bool All<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, bool> predicate)
        where TKey : notnull =>
        All(new global::System.Collections.Generic.List<TValue>(xs), predicate);
    public static int Count<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, bool> predicate)
        where TKey : notnull =>
        Count(new global::System.Collections.Generic.List<TValue>(xs), predicate);
    public static TValue? FirstOrDefault<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs)
        where TKey : notnull =>
        FirstOrDefault(new global::System.Collections.Generic.List<TValue>(xs));
    public static TValue? FirstOrDefault<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, bool> predicate)
        where TKey : notnull =>
        FirstOrDefault(new global::System.Collections.Generic.List<TValue>(xs), predicate);
    public static global::System.Collections.Generic.List<TValue> OrderBy<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, TK> keySelector)
        where TKey : notnull =>
        OrderBy(new global::System.Collections.Generic.List<TValue>(xs), keySelector);
    public static global::System.Collections.Generic.List<TValue> OrderByDescending<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, TK> keySelector)
        where TKey : notnull =>
        OrderByDescending(new global::System.Collections.Generic.List<TValue>(xs), keySelector);
    public static TValue Min<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs)
        where TKey : notnull =>
        Min(new global::System.Collections.Generic.List<TValue>(xs));
    public static TValue Max<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs)
        where TKey : notnull =>
        Max(new global::System.Collections.Generic.List<TValue>(xs));
    public static TK Min<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, TK> selector)
        where TKey : notnull =>
        Min(new global::System.Collections.Generic.List<TValue>(xs), selector);
    public static TK Max<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, TK> selector)
        where TKey : notnull =>
        Max(new global::System.Collections.Generic.List<TValue>(xs), selector);
    public static TValue MinBy<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, TK> keySelector)
        where TKey : notnull =>
        MinBy(new global::System.Collections.Generic.List<TValue>(xs), keySelector);
    public static TValue MaxBy<TKey, TValue, TK>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, TK> keySelector)
        where TKey : notnull =>
        MaxBy(new global::System.Collections.Generic.List<TValue>(xs), keySelector);
    public static int Sum<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, int> selector)
        where TKey : notnull =>
        Sum(new global::System.Collections.Generic.List<TValue>(xs), selector);
    public static float Sum<TKey, TValue>(this global::System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection xs, global::System.Func<TValue, float> selector)
        where TKey : notnull =>
        Sum(new global::System.Collections.Generic.List<TValue>(xs), selector);
    public static int Sum<TValue>(this global::System.Collections.Generic.Dictionary<int, TValue>.KeyCollection xs) =>
        Sum(new global::System.Collections.Generic.List<int>(xs));
    public static float Sum<TValue>(this global::System.Collections.Generic.Dictionary<float, TValue>.KeyCollection xs) =>
        Sum(new global::System.Collections.Generic.List<float>(xs));
    public static int Sum<TKey>(this global::System.Collections.Generic.Dictionary<TKey, int>.ValueCollection xs)
        where TKey : notnull =>
        Sum(new global::System.Collections.Generic.List<int>(xs));
    public static float Sum<TKey>(this global::System.Collections.Generic.Dictionary<TKey, float>.ValueCollection xs)
        where TKey : notnull =>
        Sum(new global::System.Collections.Generic.List<float>(xs));
}

// Dictionary<K,V> の演算。結果が key と value の集合だけで決まるものに限る
// (列挙順は backend ごとに異なり未規定。q.md Q18)。述語は `(k, v) =>` の 2 引数
// (KeyValuePair を値として持たない)。Any / All は真 / 偽が決まった時点で止まる。
// 述語は呼び出し時点の entry の写しに対して呼ぶ: 述語が Dictionary を変えても
// 呼ばれる entry と渡る値は変わらない (Lua / C runtime と同じ。live の foreach だと
// 追加で「Collection was modified」になり、backend ごとに結果が分かれる)
public static class Dict
{
    private static global::System.Collections.Generic.KeyValuePair<TKey, TValue>[] Entries<TKey, TValue>(
        global::System.Collections.Generic.Dictionary<TKey, TValue> dict)
        where TKey : notnull
    {
        var entries = new global::System.Collections.Generic.KeyValuePair<TKey, TValue>[dict.Count];
        ((global::System.Collections.Generic.ICollection<
            global::System.Collections.Generic.KeyValuePair<TKey, TValue>>)dict).CopyTo(entries, 0);
        return entries;
    }
    public static global::System.Collections.Generic.Dictionary<TKey, TValue> Where<TKey, TValue>(
        this global::System.Collections.Generic.Dictionary<TKey, TValue> dict,
        global::System.Func<TKey, TValue, bool> predicate)
        where TKey : notnull
    {
        var result = new global::System.Collections.Generic.Dictionary<TKey, TValue>();
        foreach (var kv in Entries(dict))
            if (predicate(kv.Key, kv.Value)) result[kv.Key] = kv.Value;
        return result;
    }
    public static bool Any<TKey, TValue>(
        this global::System.Collections.Generic.Dictionary<TKey, TValue> dict,
        global::System.Func<TKey, TValue, bool> predicate)
        where TKey : notnull
    {
        foreach (var kv in Entries(dict))
            if (predicate(kv.Key, kv.Value)) return true;
        return false;
    }
    public static bool All<TKey, TValue>(
        this global::System.Collections.Generic.Dictionary<TKey, TValue> dict,
        global::System.Func<TKey, TValue, bool> predicate)
        where TKey : notnull
    {
        foreach (var kv in Entries(dict))
            if (!predicate(kv.Key, kv.Value)) return false;
        return true;
    }
    public static int Count<TKey, TValue>(
        this global::System.Collections.Generic.Dictionary<TKey, TValue> dict,
        global::System.Func<TKey, TValue, bool> predicate)
        where TKey : notnull
    {
        var n = 0;
        foreach (var kv in Entries(dict))
            if (predicate(kv.Key, kv.Value)) n++;
        return n;
    }
    // 述語が真でまだ在る entry をその場で消し、消した数を返す (述語が先に消した
    // entry は数えない)
    public static int RemoveAll<TKey, TValue>(
        this global::System.Collections.Generic.Dictionary<TKey, TValue> dict,
        global::System.Func<TKey, TValue, bool> predicate)
        where TKey : notnull
    {
        var removed = 0;
        foreach (var kv in Entries(dict))
            if (predicate(kv.Key, kv.Value) && dict.Remove(kv.Key)) removed++;
        return removed;
    }
}
