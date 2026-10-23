namespace TinyCs.Tests;

public class ListSemanticTests
{
    [Fact]
    public void List_Remove()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2, 3 };
                    list.Remove(2);
                    return list.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("2", result);
    }

    [Fact]
    public void List_RemoveAt()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 10, 20, 30 };
                    list.RemoveAt(0);
                    return list[0];
                }
            }
            """, "T.Test()");
        Assert.Equal("20", result);
    }

    [Fact]
    public void List_Clear()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2, 3 };
                    list.Clear();
                    return list.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("0", result);
    }

    [Fact]
    public void List_Contains()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static bool Test()
                {
                    var list = new List<int> { 1, 2, 3 };
                    return list.Contains(2);
                }
            }
            """, "tostring(T.Test())");
        Assert.Equal("true", result);
    }

    [Fact]
    public void List_IndexOf()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 10, 20, 30 };
                    return list.IndexOf(20);
                }
            }
            """, "T.Test()");
        Assert.Equal("1", result);
    }

    [Fact]
    public void List_Sort()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var list = new List<int> { 3, 1, 2 };
                    list.Sort();
                    return list[0].ToString() + "," + list[1].ToString() + "," + list[2].ToString();
                }
            }
            """, "T.Test()");

        Assert.Equal("1,2,3", result);
    }

    // string の順序は ordinal (大文字が先)。
    // dotnet differential の .NET 側も InvariantGlobalization (test project) で同じ順序になる
    [Fact]
    public void List_Sort_StringOrdinal()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var list = new List<string> { "b", "A", "a", "B" };
                    list.Sort();
                    return list[0] + "," + list[1] + "," + list[2] + "," + list[3];
                }
            }
            """, "T.Test()");

        Assert.Equal("A,B,a,b", result);
    }

    [Fact]
    public void List_Sort_Comparison()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var list = new List<int> { 3, 1, 2 };
                    list.Sort((a, b) => b - a);
                    return list[0].ToString() + "," + list[1].ToString() + "," + list[2].ToString();
                }
            }
            """, "T.Test()");

        Assert.Equal("3,2,1", result);
    }

    // FindIndex は 0-based の位置か -1。述語は要素ごとに 1 回、要素の順で、見つかった時点で止まる
    [Fact]
    public void List_FindIndex()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var list = new List<int> { 3, 5, 8 };
                    var log = "";
                    var found = list.FindIndex(x => { log += x + ";"; return x > 4; });
                    var missing = list.FindIndex(x => x > 100);
                    var empty = new List<int>().FindIndex(x => true);
                    return found + ":" + missing + ":" + empty + ":" + log;
                }
            }
            """, "T.Test()");
        Assert.Equal("1:-1:-1:3;5;", result);
    }

    // RemoveAll は述語が真の要素をその場で消して数を返す。残る順は保ち、述語は要素ごとに 1 回
    [Fact]
    public void List_RemoveAll()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public struct S { public int N; }
            public class T
            {
                public static string Test()
                {
                    var list = new List<int> { 1, 2, 3, 4, 5, 6 };
                    var calls = 0;
                    var removed = list.RemoveAll(x => { calls++; return x % 2 == 0; });
                    var none = list.RemoveAll(x => x > 100);
                    var ss = new List<S> { new S { N = 1 }, new S { N = 2 }, new S { N = 3 } };
                    var sr = ss.RemoveAll(s => { s.N = 99; return s.N == 99 && ss.Count == 3 && false; });
                    var all = new List<int> { 7, 8 };
                    var allRemoved = all.RemoveAll(x => true);
                    return removed + ":" + none + ":" + calls + ":" + string.Join(",", list)
                        + ":" + sr + ":" + ss[0].N + ss[1].N + ss[2].N + ":" + allRemoved + ":" + all.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("3:0:6:1,3,5:0:123:2:0", result);
    }

    // FindIndex / RemoveAll は BCL の List<T> の実装に揃える: FindIndex の走査は呼び出し時点の
    // Count まで (述語が足した要素は見ない)。RemoveAll は生きた Count まで走り (述語が足した
    // 要素も述語に渡す)、残す要素は述語の後に読み直す (述語が書き換えた値が残る)
    [Fact]
    public void List_FindIndexRemoveAll_CallbacksMutateReceiver()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var xs = new List<int> { 1, 2, 3 };
                    var calls = 0;
                    var found = xs.FindIndex(x => { calls++; xs.Add(9); return x == 9; });
                    var ys = new List<int> { 1, 2 };
                    var removedAdded = ys.RemoveAll(x => { if (x == 1) ys.Add(3); return x == 3; });
                    var ws = new List<int> { 1, 2, 3 };
                    var removedRewritten = ws.RemoveAll(x => { if (x == 3) ws[2] = 8; return x == 1; });
                    var vs = new List<int> { 1, 2, 3, 4 };
                    var removedShrunk = vs.RemoveAll(x => { if (x == 2) vs.RemoveAt(vs.Count - 1); return x % 2 == 1; });
                    return found + ":" + calls + ":" + removedAdded + ":" + string.Join(",", ys)
                        + ":" + removedRewritten + ":" + string.Join(",", ws) + ":" + removedShrunk + ":" + string.Join(",", vs);
                }
            }
            """, "T.Test()");
        Assert.Equal("-1:3:1:1,2:1:2,8:2:2", result);
    }

    // FindIndex の述語が受け手を縮めて添字が範囲外になれば Lua / C は fault (List の他の演算と
    // 同じ)。.NET の List<T>.FindIndex は配列の残骸を読んで続けるので、ここだけ dotnet とは揃わない
    // (support-matrix §17)
    [Fact]
    public void List_FindIndex_ShrinkingReceiver_Fault()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestHelper.TranspileAndRunWithRuntime("""
                using System.Collections.Generic;
                public class T
                {
                    public static int Test()
                    {
                        var xs = new List<int> { 1, 2, 3 };
                        return xs.FindIndex(x => { xs.RemoveAt(xs.Count - 1); return false; });
                    }
                }
                """, "T.Test()"));
        Assert.Contains("Lua exited", ex.Message);
        Assert.Contains("Index was out of range", ex.Message);
    }
}
