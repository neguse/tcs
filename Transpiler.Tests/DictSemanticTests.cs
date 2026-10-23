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
                    return dict.Keys.Count;
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
                    return dict.Values.Count;
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

    // `foreach (var (k, v) in dict)`: KeyValuePair を作らず (k, v) を束縛する。float key は NaN、
    // 変更可能な struct の値は copy、break / continue は通常の foreach と同じ
    [Fact]
    public void Dict_ForeachDeconstruct()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            public struct S { public int N; public void Bump() { N += 1; } }
            public class T
            {
                public static string Test()
                {
                    var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    var sum = 0; var keys = 0;
                    foreach (var (k, v) in d) { if (k == "b") continue; sum += v; keys += k.Length; }
                    var f = new Dictionary<float, int> { [float.NaN] = 5, [1.5f] = 6 };
                    var nan = 0;
                    foreach (var (k, v) in f) { if (k != k) nan += v; }
                    var ss = new Dictionary<int, S> { [1] = new S { N = 10 } };
                    foreach (var (k, v) in ss) { v.Bump(); }
                    var seen = 0;
                    foreach (var (k, v) in d) { seen++; if (seen == 1) break; }
                    return sum + ":" + keys + ":" + nan + ":" + ss[1].N + ":" + seen;
                }
            }
            """, "T.Test()");
        Assert.Equal("4:2:5:10:1", result);
    }

    // 分解 foreach は Dictionary の (key, value) の 2 要素だけ。List の tuple や discard は TCS1001
    [Fact]
    public void Dict_ForeachDeconstruct_OtherShapesAreUnsupported()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            using System.Collections.Generic;
            public class T
            {
                public static int Test()
                {
                    var pairs = new List<(int, int)> { (1, 2) };
                    var n = 0;
                    foreach (var (a, b) in pairs) n += a + b;
                    var d = new Dictionary<string, int> { ["a"] = 1 };
                    foreach (var (k, _) in d) n += k.Length;
                    return n;
                }
            }
            """]);

        Assert.Equal(2, result.Errors.Count(e => e.Contains("TCS1001") && e.Contains("ForEachVariableStatement")));
    }

    // Keys / Values は List の LINQ への橋。順序が要るときは OrderBy を通す
    [Fact]
    public void Dict_KeysValues_AreLinqReceivers()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var d = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1, ["c"] = 3 };
                    var keys = d.Keys.OrderBy(k => k);
                    var big = d.Values.Where(v => v > 1).OrderByDescending(v => v);
                    var names = d.Keys.Select(k => k + k).OrderBy(s => s);
                    return string.Join(",", keys) + ":" + d.Values.Sum() + ":" + d.Values.Min() + ":" + d.Keys.Max()
                        + ":" + big.Count + ":" + big[0] + ":" + (d.Keys.Any(k => k == "c") ? 1 : 0) + ":" + (d.Values.All(v => v > 0) ? 1 : 0)
                        + ":" + d.Values.Count(v => v >= 2) + ":" + d.Values.MinBy(v => -v) + ":" + names[0]
                        + ":" + d.Values.Sum(v => v * 10) + ":" + (d.Keys.FirstOrDefault(k => k == "zz") == null ? 1 : 0);
                }
            }
            """, "T.Test()");
        Assert.Equal("a,b,c:6:1:c:2:3:1:1:2:3:aa:60:1", result);
    }

    // Keys / Values を持ち越したいときは copy constructor で写す。写した List は
    // Dictionary を変えても変わらない (3 経路とも同じ時点の copy)
    [Fact]
    public void Dict_KeysValues_SnapshotViaCopyConstructor()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static string Test()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var snap = new List<int>(d.Values);
                    d.Clear();
                    return snap.Count + ":" + (snap.Any() ? 1 : 0) + ":" + d.Values.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("1:1:0", result);
    }

    // Keys / Values は変数・field・property・引数・戻り値に持てない。Lua / C は取得時の
    // copy、dotnet は live view なので、保存して Dictionary を変えた後に使うと結果が分かれる
    [Fact]
    public void Dict_KeysValues_CannotBeStored()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                static Dictionary<int, int>.ValueCollection Field;
                static Dictionary<int, int>.KeyCollection Prop { get; set; }
                static Dictionary<int, int>.KeyCollection Ret(Dictionary<int, int> d) => d.Keys;
                static int Take(Dictionary<int, int>.ValueCollection vs) => vs.Count;
                public static int Test()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var values = d.Values;
                    d.Clear();
                    return values.Any() ? 1 : 0;
                }
            }
            """]);

        Assert.Equal(5, result.Errors.Count(e => e.Contains("TCS1001") && e.Contains("KeysValuesStored(")));
        Assert.Contains(result.Errors, e => e.Contains("KeysValuesStored(Dictionary<int, int>.ValueCollection)"));
    }
    // Keys / Values を受け手にした LINQ の copy は .NET facade と同じく呼び出し時
    // (引数を評価した後)。引数の評価が Dictionary を変えても結果が一致し、
    // dict の式は引数より先に評価する (C# の評価順)
    [Fact]
    public void Dict_KeysValues_CopyAfterArguments()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                static Dictionary<int, int> Pick(Dictionary<int, int> d, List<string> log) { log.Add("r"); return d; }
                static Func<int, bool> Clearing(Dictionary<int, int> d, List<string> log) { log.Add("a"); d.Clear(); return x => true; }
                public static string Test()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var log = new List<string>();
                    var any = Pick(d, log).Values.Any(Clearing(d, log));
                    var d2 = new Dictionary<int, int> { [1] = 1, [2] = 2 };
                    var keep = d2.Keys.Where(k => k > 1);
                    return (any ? 1 : 0) + ":" + string.Join(",", log) + ":" + keep.Count;
                }
            }
            """, "T.Test()");
        Assert.Equal("0:r,a:1", result);
    }

    // lambda だけの引数は評価に副作用が無いので、copy を遅らせる形にしない
    [Fact]
    public void Dict_KeysValues_PureArguments_CopyDirectly()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                public static int Test(Dictionary<int, int> d) => d.Values.Count(v => v > 1) + d.Keys.Where(k => k > 0).Count;
            }
            """]);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.DoesNotContain("__tcs_dict", result.Lua);
    }
    // 副作用のある getter の引数、括弧付き / 三項の receiver でも copy は呼び出し時
    [Fact]
    public void Dict_KeysValues_CopyAfterArguments_GetterAndWrappedReceiver()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                static Dictionary<int, int> s = new Dictionary<int, int> { [1] = 10 };
                static Func<int, bool> ClearingPredicate { get { s.Clear(); return x => true; } }
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                public static string Test()
                {
                    var a = s.Values.Any(ClearingPredicate) ? 1 : 0;
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var b = (d.Values).Any(Clear(d)) ? 1 : 0;
                    var e = new Dictionary<int, int> { [2] = 20 };
                    var f = new Dictionary<int, int> { [3] = 30 };
                    var flag = e.Count == 1;
                    var c = (flag ? e.Keys : f.Keys).Count(Clear(e));
                    return a + ":" + b + ":" + c;
                }
            }
            """, "T.Test()");
        Assert.Equal("0:0:0", result);
    }
    // user 定義の単項演算子を含む引数と、`??` の receiver でも copy は呼び出し時
    [Fact]
    public void Dict_KeysValues_CopyAfterArguments_UnaryOperatorAndCoalesceReceiver()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class Number
            {
                public static int operator -(Number x) { T.s.Clear(); return 0; }
            }
            public class T
            {
                public static Dictionary<int, int> s = new Dictionary<int, int> { [1] = 10 };
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                public static string Test()
                {
                    var n = new Number();
                    Func<int, bool> yes = x => true;
                    Func<int, bool> no = x => false;
                    var a = s.Values.Any(-n == 0 ? yes : no) ? 1 : 0;
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var e = new Dictionary<int, int> { [2] = 20 };
                    var b = (d.Values ?? e.Values).Any(Clear(d)) ? 1 : 0;
                    return a + ":" + b;
                }
            }
            """, "T.Test()");
        Assert.Equal("0:0", result);
    }
    // 三項 / `??` の receiver の片側が null でも、他方の view の copy は呼び出し時
    [Fact]
    public void Dict_KeysValues_CopyAfterArguments_NullArmReceiver()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                public static string Test()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    bool flag = d.Count == 1;
                    var a = (flag ? d.Values : null).Any(Clear(d)) ? 1 : 0;
                    var e = new Dictionary<int, int> { [1] = 10 };
                    var b = (!flag ? null : e.Keys).Any(Clear(e)) ? 1 : 0;
                    var f = new Dictionary<int, int> { [1] = 10 };
                    var c = (f.Values ?? null).Any(Clear(f)) ? 1 : 0;
                    return a + ":" + b + ":" + c;
                }
            }
            """, "T.Test()");
        Assert.Equal("0:0:0", result);
    }
    // null の側に括弧 / cast が付いても、`?? (null)`、switch 式、`?.` (IIFE) の receiver でも、
    // view の copy は呼び出し時
    [Fact]
    public void Dict_KeysValues_CopyAfterArguments_WrappedNullAndIifeReceiver()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class T
            {
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                static Dictionary<int, int>? Get(Dictionary<int, int> d) => d;
                static Dictionary<int, int> New() => new Dictionary<int, int> { [1] = 10 };
                public static string Test()
                {
                    var d = New();
                    bool flag = d.Count == 1;
                    var parts = new List<int>();
                    parts.Add((flag ? d.Values : (null)).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    parts.Add((flag ? d.Values : (Dictionary<int, int>.ValueCollection?)null).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    parts.Add((d.Values ?? (null)).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    parts.Add((Get(d)?.Values).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    parts.Add(Get(d)?.Values.Any(Clear(d)) == true ? 1 : 0);
                    d = New();
                    parts.Add((flag switch { true => d.Values, _ => null }).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    parts.Add((flag ? (flag ? d.Values : null) : null).Any(Clear(d)) ? 1 : 0);
                    return string.Join(",", parts);
                }
            }
            """, "T.Test()");
        Assert.Equal("0,0,0,0,0,0,0", result);
    }
}
