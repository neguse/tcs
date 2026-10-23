namespace TinyCs.Tests;

public class DictSemanticTests
{
    [Fact]
    public void Dict_Count()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static int Test()
                {
                    var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
                    return dict.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("2", result);
    }

    [Fact]
    public void Dict_Remove()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static int Test()
                {
                    var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
                    dict.Remove("a");
                    return dict.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("1", result);
    }

    [Fact]
    public void Dict_Keys()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var dict = new Dictionary<string, int> { { "x", 1 }, { "y", 2 } };
                    var keys = dict.Keys;
                    return keys.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("2", result);
    }

    [Fact]
    public void Dict_Values()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test()
                {
                    var dict = new Dictionary<string, int> { { "x", 10 }, { "y", 20 } };
                    var vals = dict.Values;
                    return vals.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("2", result);
    }

    [Fact]
    public void Dict_TryGetValue_Found_AssignsOutVar()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var dict = new Dictionary<string, int> { { "x", 42 } };
                    var found = dict.TryGetValue("x", out var value);
                    return found.ToString() + ":" + value.ToString();
                }
            }
            """, "T.Test()");

        Assert.Equal("true:42", result);
    }

    [Fact]
    public void Dict_TryGetValue_Missing_AssignsDefaultToOutVar()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var dict = new Dictionary<string, int> { { "x", 42 } };
                    var found = dict.TryGetValue("missing", out var value);
                    return found.ToString() + ":" + value.ToString();
                }
            }
            """, "T.Test()");

        Assert.Equal("false:0", result);
    }

    [Fact]
    public void Dict_TryGetValue_IfCondition_OutVarVisibleInThen()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static int Test()
                {
                    var dict = new Dictionary<string, int> { { "x", 7 } };
                    if (dict.TryGetValue("x", out var value))
                    {
                        return value;
                    }
                    return 0;
                }
            }
            """, "T.Test()");

        Assert.Equal("7", result);
    }

    [Fact]
    public void Dict_TryGetValue_ExistingOutVariable()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var dict = new Dictionary<string, string> { { "x", "ok" } };
                    string value;
                    var found = dict.TryGetValue("missing", out value);
                    return found.ToString() + ":" + (value == null ? "nil" : value);
                }
            }
            """, "T.Test()");

        Assert.Equal("false:nil", result);
    }

    // Dictionary の (k, v) 述語の演算 (TinySystem.Linq.Dict)。結果は key と value の集合だけで決まる
    [Fact]
    public void Dict_WhereAnyAllCountRemoveAll()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
                    var even = d.Where((k, v) => v % 2 == 0);
                    var any = d.Any((k, v) => k == "c" && v == 3);
                    var none = d.Any((k, v) => v > 10);
                    var all = d.All((k, v) => v > 0);
                    var notAll = d.All((k, v) => k != "a");
                    var count = d.Count((k, v) => v >= 2);
                    var removed = d.RemoveAll((k, v) => v <= 2);
                    return even.Count + ":" + (even.ContainsKey("b") ? 1 : 0) + (even.ContainsKey("a") ? 1 : 0) + ":" + (any ? 1 : 0) + (none ? 1 : 0)
                        + ":" + (all ? 1 : 0) + (notAll ? 1 : 0) + ":" + count + ":" + removed + ":" + d.Count
                        + ":" + (d.ContainsKey("c") ? 1 : 0) + (d.ContainsKey("a") ? 1 : 0);
                }
            }
            """, "T.Test()");
        Assert.Equal("2:10:10:10:3:2:2:10", result);
    }

    [Fact]
    public void Dict_Clear_KeepsTableIdentity()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public class T
            {
                public static string Test()
                {
                    var d = new Dictionary<int, string> { [1] = "x", [2] = "y" };
                    var alias = d;
                    d.Clear();
                    d[3] = "z";
                    return alias.Count + ":" + alias[3];
                }
            }
            """, "T.Test()");
        Assert.Equal("1:z", result);
    }

    // float key の dict は述語に NaN をそのまま渡す (sentinel は見えない)
    [Fact]
    public void Dict_FloatKey_PredicateSeesNaN()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var d = new Dictionary<float, int> { [float.NaN] = 1, [1.5f] = 2 };
                    var nan = d.Count((k, v) => k != k);
                    var kept = d.Where((k, v) => k != k);
                    return nan + ":" + kept.Count + ":" + (kept.ContainsKey(float.NaN) ? 1 : 0);
                }
            }
            """, "T.Test()");
        Assert.Equal("1:1:1", result);
    }

    // 述語は呼び出し時点の entry の写しに対して呼ぶ: 述語が Dictionary を変えても (追加 / 上書き /
    // 削除 / Clear)、呼ばれる entry と渡る値は変わらず、結果は呼び出し時点の内容で決まる。
    // RemoveAll は述語が真でまだ在る entry を消し、その数を返す
    [Fact]
    public void Dict_PredicateOps_UseCallTimeEntries()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    var count = d.Count((k, v) => { d["x" + k] = v; return true; });
                    var e = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
                    var even = e.Where((k, v) => { e[k] = 0; return v % 2 == 0; });
                    var f = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    var allCalls = 0;
                    var all = f.All((k, v) => { allCalls++; f.Clear(); return v > 0; });
                    var g = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    var removed = g.RemoveAll((k, v) => { g.Remove("c"); return v >= 2; });
                    var h = new Dictionary<int, int> { [1] = 10, [2] = 20 };
                    var any = h.Any((k, v) => { h[k + 10] = v; h.Remove(k); return v > 100; });
                    return count + ":" + d.Count + ":" + even.Count + even["b"] + even["d"] + ":" + e["a"] + e["d"]
                        + ":" + (all ? 1 : 0) + allCalls + f.Count + ":" + removed + ":" + g.Count + (g.ContainsKey("a") ? 1 : 0)
                        + ":" + (any ? 1 : 0) + h.Count + (h.ContainsKey(11) ? 1 : 0) + (h.ContainsKey(12) ? 1 : 0);
                }
            }
            """, "T.Test()");
        Assert.Equal("3:6:224:00:130:1:11:0211", result);
    }
}
