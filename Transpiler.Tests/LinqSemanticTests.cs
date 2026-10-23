namespace TinyCs.Tests;

public class LinqSemanticTests
{
    [Fact]
    public void Linq_All()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static bool Test()
                {
                    var list = new List<int> { 2, 4, 6 };
                    return list.All(x => x % 2 == 0);
                }
            }
            """, "tostring(T.Test())");
        Assert.Equal("true", result);
    }

    // IEnumerable receiver (チェーン途中) でも default(T) が要る —
    // List receiver 専用分岐だけ default を注入していた退行の再現
    [Fact]
    public void Linq_FirstOrDefault_EmptyChain_ReturnsIntDefault()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2 };
                    return list.OrderBy(x => x).Skip(5).FirstOrDefault();
                }
            }
            """, "T.Test()");
        Assert.Equal("0", result);
    }

    [Fact]
    public void Linq_LastOrDefault_EmptyWhereChain_ReturnsIntDefault()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2 };
                    return list.Where(x => x > 100).LastOrDefault();
                }
            }
            """, "T.Test()");
        Assert.Equal("0", result);
    }

    [Fact]
    public void Linq_FirstOrDefault_Found()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2, 3 };
                    return list.FirstOrDefault(x => x > 1);
                }
            }
            """, "T.Test()");
        Assert.Equal("2", result);
    }

    [Fact]
    public void Linq_Min()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 5, 3, 8, 1 };
                    return list.Min();
                }
            }
            """, "T.Test()");
        Assert.Equal("1", result);
    }

    // float の selector が NaN を返したら以降の selector は呼ばない (support-matrix §19
    // の短絡)。Lua / C は元から短絡し、TinySystem facade は dotnet differential で
    // 呼出し回数まで比べる (#261)
    [Fact]
    public void Linq_Min_Selector_StopsAtNaN()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2, 3 };
                    int calls = 0;
                    float m = list.Min(x =>
                    {
                        calls++;
                        float z = x - x;
                        return x == 2 ? z / z : (float)x;
                    });
                    return (m != m ? 10 : 0) + calls;
                }
            }
            """, "T.Test()");
        Assert.Equal("12", result);
    }

    // 結果は呼び出し時点の内容で確定する (q.md Q18)。selector が受け手に足した要素は
    // 走査しない
    [Fact]
    public void Linq_Min_Selector_IgnoresElementsAddedBySelector()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var xs = new List<int> { 2, 1 };
                    int m = xs.Min(x =>
                    {
                        if (x == 1) xs.Add(0);
                        return x;
                    });
                    return m * 10 + xs.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("13", result);
    }

    // callback (述語 / selector / key selector) が受け手に要素を足しても、走査は
    // 呼び出し時点の Count まで (q.md Q18)。facade が Enumerable に委譲していると
    // dotnet だけ「Collection was modified」で止まる
    [Fact]
    public void Linq_Callbacks_IgnoreElementsAddedByCallback()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var parts = new List<string>();
                    var xs = new List<int> { 1, 2 };
                    parts.Add(xs.Max(x => { if (x == 1) xs.Add(3); return x; }).ToString());
                    xs = new List<int> { 1, 2 };
                    parts.Add(xs.Sum(x => { if (x == 1) xs.Add(3); return x; }).ToString());
                    xs = new List<int> { 1, 2 };
                    parts.Add(xs.Where(x => { if (x == 1) xs.Add(3); return x > 0; }).Count.ToString());
                    xs = new List<int> { 1, 2 };
                    parts.Add(xs.Select(x => { if (x == 1) xs.Add(3); return x * 10; }).Count.ToString());
                    xs = new List<int> { 1, 2 };
                    parts.Add(xs.Any(x => { if (x == 1) xs.Add(3); return x == 3; }) ? "t" : "f");
                    xs = new List<int> { 1, 2 };
                    parts.Add(xs.All(x => { if (x == 1) xs.Add(3); return x < 3; }) ? "t" : "f");
                    xs = new List<int> { 1, 2 };
                    parts.Add(xs.Count(x => { if (x == 1) xs.Add(3); return true; }).ToString());
                    xs = new List<int> { 1, 2 };
                    parts.Add(xs.FirstOrDefault(x => { if (x == 1) xs.Add(3); return x == 3; }).ToString());
                    xs = new List<int> { 2, 1 };
                    parts.Add(xs.OrderBy(x => { if (x == 2) xs.Add(0); return x; }).Count.ToString());
                    return string.Join(",", parts);
                }
            }
            """, "T.Test()");
        Assert.Equal("2,3,2,2,f,t,2,0,2", result);
    }

    // 要素を返す演算は callback の前に読んだ要素を返す (callback が要素を書き換えても
    // 結果は呼び出し時点の要素)
    [Fact]
    public void Linq_Callbacks_ReadElementsBeforeCallback()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var xs = new List<int> { 1 };
                    var a = xs.FirstOrDefault(x => { xs[0] = 9; return true; });
                    xs = new List<int> { 1 };
                    var b = xs.Where(x => { xs[0] = 9; return true; })[0];
                    xs = new List<int> { 2, 1 };
                    var c = xs.OrderBy(x => { xs[0] = 9; xs[1] = 8; return x; });
                    return a + ":" + b + ":" + c[0] + "," + c[1];
                }
            }
            """, "T.Test()");
        Assert.Equal("1:1:1,2", result);
    }

    // string の順は ordinal (Lua / C runtime と同じ。culture の比較ではない)
    [Fact]
    public void Linq_StringKeys_CompareOrdinal()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var xs = new List<string> { "b", "B", "a" };
                    return xs.Min() + xs.Max() + xs.OrderBy(s => s)[0] + xs.OrderByDescending(s => s)[0]
                        + xs.Min(s => s) + xs.Max(s => s);
                }
            }
            """, "T.Test()");
        Assert.Equal("BbBbBb", result);
    }

    [Fact]
    public void Linq_Max()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 5, 3, 8, 1 };
                    return list.Max();
                }
            }
            """, "T.Test()");
        Assert.Equal("8", result);
    }

    [Fact]
    public void Linq_Count()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2, 3 };
                    return list.Count();
                }
            }
            """, "T.Test()");

        Assert.Equal("3", result);
    }

    [Fact]
    public void Linq_Count_WithPredicate()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2, 3, 4 };
                    return list.Count(x => x % 2 == 0);
                }
            }
            """, "T.Test()");

        Assert.Equal("2", result);
    }

    [Fact]
    public void Linq_ToDictionary_KeySelector()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;

            public class Item
            {
                public string Name;
                public int Count;

                public Item(string name, int count)
                {
                    Name = name;
                    Count = count;
                }
            }

            public class T
            {
                public static int Test()
                {
                    var items = new List<Item>
                    {
                        new Item("Potion", 5),
                        new Item("Arrow", 20)
                    };
                    var byName = items.ToDictionary(item => item.Name);
                    return byName["Arrow"].Count;
                }
            }
            """, "T.Test()");

        Assert.Equal("20", result);
    }

    [Fact]
    public void Linq_ToDictionary_ValueSelector()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;

            public class Item
            {
                public string Name;
                public int Count;

                public Item(string name, int count)
                {
                    Name = name;
                    Count = count;
                }
            }

            public class T
            {
                public static int Test()
                {
                    var items = new List<Item>
                    {
                        new Item("Potion", 5),
                        new Item("Arrow", 20)
                    };
                    var counts = items.ToDictionary(item => item.Name, item => item.Count);
                    return counts["Potion"];
                }
            }
            """, "T.Test()");

        Assert.Equal("5", result);
    }

    [Fact]
    public void Linq_OrderByDescending()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var list = new List<int> { 2, 4, 1, 3 };
                    var sorted = list.OrderByDescending(x => x).ToList();
                    return sorted[0].ToString() + "," + sorted[1].ToString();
                }
            }
            """, "T.Test()");

        Assert.Equal("4,3", result);
    }

    [Fact]
    public void Linq_Take()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var list = new List<int> { 1, 2, 3, 4 };
                    var taken = list.Take(2).ToList();
                    return taken.Count.ToString() + ":" + taken[0].ToString() + "," + taken[1].ToString();
                }
            }
            """, "T.Test()");

        Assert.Equal("2:1,2", result);
    }

    [Fact]
    public void Linq_Skip()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var list = new List<int> { 1, 2, 3, 4 };
                    var skipped = list.Skip(2).ToList();
                    return skipped.Count.ToString() + ":" + skipped[0].ToString() + "," + skipped[1].ToString();
                }
            }
            """, "T.Test()");

        Assert.Equal("2:3,4", result);
    }

    [Fact]
    public void Linq_Last()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var list = new List<int> { 1, 2, 3, 4 };
                    return list.Last(x => x % 2 == 0);
                }
            }
            """, "T.Test()");

        Assert.Equal("4", result);
    }

    [Fact]
    public void Linq_LastOrDefault_NotFound()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var list = new List<string> { "a", "b" };
                    var found = list.LastOrDefault(x => x == "z");
                    return found == null ? "nil" : found;
                }
            }
            """, "T.Test()");

        Assert.Equal("nil", result);
    }

    // empty sequence の default は要素型別 (int=0 / bool=false / ref=nil)。
    // First/Last/Min/Max の empty・predicate miss は nil ではなく明示 error。
    [Fact]
    public void Linq_FirstOrDefault_ValueTypeDefaults()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var ints = new List<int>();
                    var bools = new List<bool>();
                    var strs = new List<string>();
                    var i = ints.FirstOrDefault();
                    var m = ints.FirstOrDefault(x => x > 10);
                    var b = bools.FirstOrDefault();
                    var s = strs.FirstOrDefault();
                    var l = ints.LastOrDefault();
                    return $"{i}|{m}|{b}|{s ?? "nil"}|{l}";
                }
            }
            """, "T.Test()");

        Assert.Equal("0|0|false|nil|0", result);
    }

    [Fact]
    public void Linq_FirstOnEmpty_RaisesExplicitError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestHelper.TranspileAndRunWithRuntime("""
                using System.Collections.Generic;
                using TinySystem.Linq;
                public class T
                {
                    public static int Test()
                    {
                        var ints = new List<int>();
                        return ints.First();
                    }
                }
                """, "T.Test()"));

        Assert.Contains("Sequence contains no elements", ex.Message);
    }

    [Fact]
    public void Linq_MinMaxOnEmpty_RaiseExplicitError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestHelper.TranspileAndRunWithRuntime("""
                using System.Collections.Generic;
                using TinySystem.Linq;
                public class T
                {
                    public static int Test()
                    {
                        var ints = new List<int>();
                        return ints.Min();
                    }
                }
                """, "T.Test()"));

        Assert.Contains("Sequence contains no elements", ex.Message);
    }

    // ToDictionary の key/value selector は各要素 1 回だけ、key → value
    // の順で評価される (C# と同じ評価回数・順序)。
    [Fact]
    public void Linq_ToDictionary_SelectorsEvaluatedOncePerElementInOrder()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Log = "";

                public static string Test()
                {
                    var names = new List<string> { "a", "bb" };
                    var d = names.ToDictionary(
                        x => { Log = Log + "k"; return x; },
                        x => { Log = Log + "v"; return x.Length; });
                    return $"{Log}|{d["a"]}|{d["bb"]}";
                }
            }
            """, "T.Test()");

        Assert.Equal("kvkv|1|2", result);
    }

    [Fact]
    public void Linq_SumOnEmpty_ReturnsZero()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var ints = new List<int>();
                    return ints.Sum();
                }
            }
            """, "T.Test()");

        Assert.Equal("0", result);
    }

    // callback が受け手を縮めて添字が範囲外になれば fault (.NET の list[i] は
    // ArgumentOutOfRangeException、C は bounds、Lua は nil の要素を範囲外とみなす error。
    // 要素は null を持てない (TCS1003) ので nil は範囲外にしか現れない)
    [Theory]
    [InlineData("xs.Where(x => { xs.RemoveAt(xs.Count - 1); return true; }).Count")]
    [InlineData("xs.Select(x => { xs.RemoveAt(xs.Count - 1); return x; }).Count")]
    [InlineData("xs.Any(x => { xs.RemoveAt(xs.Count - 1); return false; }) ? 1 : 0")]
    [InlineData("xs.All(x => { xs.RemoveAt(xs.Count - 1); return true; }) ? 1 : 0")]
    [InlineData("xs.Count(x => { xs.RemoveAt(xs.Count - 1); return true; })")]
    [InlineData("xs.FirstOrDefault(x => { xs.RemoveAt(xs.Count - 1); return false; })")]
    [InlineData("xs.Min(x => { xs.RemoveAt(xs.Count - 1); return x; })")]
    [InlineData("xs.Max(x => { xs.RemoveAt(xs.Count - 1); return x; })")]
    [InlineData("xs.Sum(x => { xs.RemoveAt(xs.Count - 1); return x; })")]
    [InlineData("xs.Sum(x => { xs.RemoveAt(xs.Count - 1); return x * 0.5f; }) > 0 ? 1 : 0")]
    public void Linq_Callbacks_ShrinkingReceiver_Fault(string op)
    {
        var source = $$"""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var xs = new List<int> { 1, 2, 3 };
                    return {{op}};
                }
            }
            """;
        var lua = Assert.Throws<InvalidOperationException>(() =>
            TestHelper.TranspileAndRunWithRuntime(source, "T.Test()"));
        Assert.Contains("Lua exited", lua.Message);
        Assert.Contains("Index was out of range", lua.Message);
        if (!SpecConformance.CorpusDifferential.Enabled) return;
        var dotnet = Assert.Throws<InvalidOperationException>(() =>
            SpecConformance.CorpusDifferential.Check(source, "T.Test()", ""));
        Assert.Contains("Index was out of range", dotnet.Message);
    }
}
