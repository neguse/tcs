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
    // Lua / C の fault と揃える)。同じ key は先の要素
    public static T MinBy<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector) =>
        list.Count == 0
            ? throw new global::System.InvalidOperationException("Sequence contains no elements")
            : global::System.Linq.Enumerable.MinBy(list, keySelector)!;
    public static T MaxBy<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector) =>
        list.Count == 0
            ? throw new global::System.InvalidOperationException("Sequence contains no elements")
            : global::System.Linq.Enumerable.MaxBy(list, keySelector)!;
}
