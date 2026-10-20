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
}
