namespace TinyCs.Tcs2c.Tests;

// Dictionary の (k, v) 述語の演算 (Where / Any / All / Count / RemoveAll) と Clear の
// 2 backend 一致。列挙順は未規定なので、key と value の集合だけで決まる値を出す
public partial class DifferentialTests
{
    [CFact]
    public void Dict_PredicateOpsAndClear()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public struct S { public int N; }
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
                    var even = d.Where((k, v) => v % 2 == 0);
                    Console.WriteLine(even.Count + ":" + even.ContainsKey("b") + ":" + even.ContainsKey("a") + ":" + even["d"]);
                    Console.WriteLine(d.Any((k, v) => k == "c" && v == 3) + ":" + d.Any((k, v) => v > 10) + ":" + d.All((k, v) => v > 0) + ":" + d.All((k, v) => k != "a"));
                    Console.WriteLine(d.Count((k, v) => v >= 2) + ":" + d.RemoveAll((k, v) => v <= 2) + ":" + d.Count + ":" + d.ContainsKey("c") + ":" + d.ContainsKey("a"));
                    var ints = new Dictionary<int, S> { [1] = new S { N = 10 }, [2] = new S { N = 20 }, [3] = new S { N = 30 } };
                    var big = ints.Where((k, v) => { v.N += 100; return v.N > 115; });
                    Console.WriteLine(big.Count + ":" + big[2].N + ":" + ints[2].N + ":" + ints.RemoveAll((k, v) => k == 1) + ":" + ints.Count);
                    var empty = new Dictionary<string, int>();
                    Console.WriteLine(empty.Any((k, v) => true) + ":" + empty.All((k, v) => false) + ":" + empty.Count((k, v) => true) + ":" + empty.RemoveAll((k, v) => true) + ":" + empty.Where((k, v) => true).Count);
                    var alias = d;
                    d.Clear();
                    d["z"] = 26;
                    Console.WriteLine(alias.Count + ":" + alias["z"]);
                }
            }
            """, "P");
        Assert.Equal("2:true:false:4\ntrue:false:true:false\n3:2:2:true:false\n2:20:20:1:2\nfalse:true:0:0:0\n1:26", output);
    }

    // 述語は呼び出し時点の entry の写しに対して呼ぶ (述語が Dictionary を変えても呼ばれる entry と
    // 値は変わらない)。RemoveAll は述語が真でまだ在る entry を消して数を返す
    [CFact]
    public void Dict_PredicateOps_UseCallTimeEntries()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    Console.WriteLine(d.Count((k, v) => { d["x" + k] = v; return true; }) + ":" + d.Count);
                    var e = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
                    var even = e.Where((k, v) => { e[k] = 0; return v % 2 == 0; });
                    Console.WriteLine(even.Count + ":" + even["b"] + ":" + even["d"] + ":" + e["a"] + ":" + e["d"]);
                    var f = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    var allCalls = 0;
                    Console.WriteLine(f.All((k, v) => { allCalls++; f.Clear(); return v > 0; }) + ":" + allCalls + ":" + f.Count);
                    var g = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    Console.WriteLine(g.RemoveAll((k, v) => { g.Remove("c"); return v >= 2; }) + ":" + g.Count + ":" + g.ContainsKey("a"));
                    var h = new Dictionary<int, int> { [1] = 10, [2] = 20 };
                    Console.WriteLine(h.Any((k, v) => { h[k + 10] = v; h.Remove(k); return v > 100; }) + ":" + h.Count + ":" + h.ContainsKey(11) + ":" + h.ContainsKey(12));
                }
            }
            """, "P");
        Assert.Equal("3:6\n2:2:4:0:0\ntrue:3:0\n1:1:true\nfalse:2:true:true", output);
    }
}
