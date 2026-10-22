namespace TinySystem.Linq;

// List<T> の LINQ (即時評価)。System.Linq の代わりに `using TinySystem.Linq;`
// で使う。TinySystem 直下に置かないのは、`using System;` と併用したとき
// Math / String / Random が System 側の型と曖昧 (CS0104) になるため。
// 列を返す演算は List<T> を返し、runtime (runtime/tinysystem.lua の List) と
// tcs2c (CEmitter.Linq) の即時評価と同じ結果になる。
public static class List
{
    public static global::System.Collections.Generic.List<T> Where<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        [.. global::System.Linq.Enumerable.Where(list, predicate)];
    public static global::System.Collections.Generic.List<TResult>
        Select<T, TResult>(
            this global::System.Collections.Generic.List<T> list,
            global::System.Func<T, TResult> selector) =>
        [.. global::System.Linq.Enumerable.Select(list, selector)];
    public static bool Any<T>(
        this global::System.Collections.Generic.List<T> list) => list.Count > 0;
    public static bool Any<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        global::System.Linq.Enumerable.Any(list, predicate);
    public static bool All<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        global::System.Linq.Enumerable.All(list, predicate);
    public static int Count<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        global::System.Linq.Enumerable.Count(list, predicate);
    public static T First<T>(
        this global::System.Collections.Generic.List<T> list) => list[0];
    public static T First<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        global::System.Linq.Enumerable.First(list, predicate);
    public static T? FirstOrDefault<T>(
        this global::System.Collections.Generic.List<T> list) =>
        global::System.Linq.Enumerable.FirstOrDefault(list);
    public static T? FirstOrDefault<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        global::System.Linq.Enumerable.FirstOrDefault(list, predicate);
    public static global::System.Collections.Generic.List<T> OrderBy<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> keySelector) =>
        [.. global::System.Linq.Enumerable.OrderBy(list, keySelector)];
    public static global::System.Collections.Generic.List<T>
        OrderByDescending<T, TKey>(
            this global::System.Collections.Generic.List<T> list,
            global::System.Func<T, TKey> keySelector) =>
        [.. global::System.Linq.Enumerable.OrderByDescending(list, keySelector)];
    public static global::System.Collections.Generic.List<T> Take<T>(
        this global::System.Collections.Generic.List<T> list, int count) =>
        [.. global::System.Linq.Enumerable.Take(list, count)];
    public static global::System.Collections.Generic.List<T> Skip<T>(
        this global::System.Collections.Generic.List<T> list, int count) =>
        [.. global::System.Linq.Enumerable.Skip(list, count)];
    public static T Last<T>(
        this global::System.Collections.Generic.List<T> list) => list[^1];
    public static T Last<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        global::System.Linq.Enumerable.Last(list, predicate);
    public static T? LastOrDefault<T>(
        this global::System.Collections.Generic.List<T> list) =>
        global::System.Linq.Enumerable.LastOrDefault(list);
    public static T? LastOrDefault<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, bool> predicate) =>
        global::System.Linq.Enumerable.LastOrDefault(list, predicate);
    public static T Min<T>(
        this global::System.Collections.Generic.List<T> list) =>
        global::System.Linq.Enumerable.Min(list)!;
    public static T Max<T>(
        this global::System.Collections.Generic.List<T> list) =>
        global::System.Linq.Enumerable.Max(list)!;
    public static int Sum(
        this global::System.Collections.Generic.List<int> list) =>
        global::System.Linq.Enumerable.Sum(list);
    public static float Sum(
        this global::System.Collections.Generic.List<float> list) =>
        global::System.Linq.Enumerable.Sum(list);
    public static global::System.Collections.Generic.List<T> ToList<T>(
        this global::System.Collections.Generic.List<T> list) => [.. list];
    public static global::System.Collections.Generic.Dictionary<TKey, T>
        ToDictionary<T, TKey>(
            this global::System.Collections.Generic.List<T> list,
            global::System.Func<T, TKey> keySelector)
        where TKey : notnull =>
        global::System.Linq.Enumerable.ToDictionary(list, keySelector,
            item => item);
    public static global::System.Collections.Generic.Dictionary<TKey, TValue>
        ToDictionary<T, TKey, TValue>(
            this global::System.Collections.Generic.List<T> list,
            global::System.Func<T, TKey> keySelector,
            global::System.Func<T, TValue> valueSelector)
        where TKey : notnull =>
        global::System.Linq.Enumerable.ToDictionary(list, keySelector,
            valueSelector);
    public static int Count<T>(
        this global::System.Collections.Generic.List<T> list) => list.Count;
    // Enumerable.Min(selector) は NaN を見ても残りの要素に selector を当てる。
    // Lua / C runtime は NaN で打ち切るので、selector の副作用まで合わせる
    public static TKey Min<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> selector)
    {
        if (list.Count == 0)
            throw new global::System.InvalidOperationException(
                "Sequence contains no elements");
        var best = selector(list[0]);
        if (IsNaN(best)) return best;
        var comparer = global::System.Collections.Generic.Comparer<TKey>.Default;
        for (var i = 1; i < list.Count; i++)
        {
            var key = selector(list[i]);
            if (IsNaN(key)) return key;
            if (comparer.Compare(key, best) < 0) best = key;
        }
        return best;
    }
    private static bool IsNaN<TKey>(TKey key) =>
        key is float f && float.IsNaN(f);
    public static TKey Max<T, TKey>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, TKey> selector) =>
        global::System.Linq.Enumerable.Max(list, selector)!;
    public static int Sum<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, int> selector) =>
        global::System.Linq.Enumerable.Sum(list, selector);
    public static float Sum<T>(
        this global::System.Collections.Generic.List<T> list,
        global::System.Func<T, float> selector) =>
        global::System.Linq.Enumerable.Sum(list, selector);
}
