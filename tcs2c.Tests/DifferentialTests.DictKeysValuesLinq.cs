namespace TinyCs.Tcs2c.Tests;

// Dictionary の Keys / Values を受け手にした List の LINQ の 2 backend 一致。
// 列挙順は未規定なので、OrderBy を通すか順序に依らない値を出す
public partial class DifferentialTests
{
    [CFact]
    public void Dict_KeysValues_ListLinq()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1, ["c"] = 3 };
                    Console.WriteLine(string.Join(",", d.Keys.OrderBy(k => k)) + ":" + string.Join(",", d.Values.OrderByDescending(v => v)));
                    Console.WriteLine(d.Values.Sum() + ":" + d.Values.Min() + ":" + d.Values.Max() + ":" + d.Keys.Min() + ":" + d.Keys.Max());
                    Console.WriteLine(d.Values.Where(v => v > 1).Count + ":" + d.Keys.Any(k => k == "c") + ":" + d.Values.All(v => v > 1) + ":" + d.Values.Count(v => v >= 2));
                    Console.WriteLine(d.Values.MinBy(v => -v) + ":" + d.Keys.MaxBy(k => k.Length) .Length + ":" + d.Values.Sum(v => v * 10) + ":" + string.Join(",", d.Keys.Select(k => k + "!").OrderBy(s => s)));
                    var ints = new Dictionary<int, float> { [3] = 0.5f, [1] = 1.5f };
                    Console.WriteLine(ints.Keys.Sum() + ":" + ints.Values.Sum() + ":" + string.Join(",", ints.Keys.OrderBy(k => k)));
                }
            }
            """, "P");
        Assert.Equal("a,b,c:3,2,1\n6:1:3:a:c\n2:true:false:2\n3:1:60:a!,b!,c!\n4:2:1,3", output);
    }
    // Keys / Values を持ち越すときは copy constructor で写す (同じ時点の copy)
    [CFact]
    public void Dict_ValuesSnapshot_ViaCopyConstructor()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var snap = new List<int>(d.Values);
                    d.Clear();
                    Console.WriteLine(snap.Count + ":" + (snap.Any() ? 1 : 0) + ":" + d.Values.Count);
                }
            }
            """, "P");
        Assert.Equal("1:1:0", output);
    }

    // Keys / Values を変数に持つと、Lua / C の copy と dotnet の live view で結果が
    // 分かれるので、両 backend が shared facts の診断で IL を作る前に止まる
    [Fact]
    public void Dict_StoredValues_BothBackendsStopWithDiagnostic()
    {
        const string program = """
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var values = d.Values;
                    d.Clear();
                    Console.WriteLine(values.Any() ? 1 : 0);
                }
            }
            """;
        foreach (var run in new Func<string>[]
                 {
                     () => Backends.RunLua([program], "P"),
                     () => Backends.EmitC([program], "P"),
                 })
        {
            var ex = Assert.Throws<InvalidOperationException>(run);
            Assert.Contains("(9,9): error TCS1001: unsupported syntax: KeysValuesStored(Dictionary<int, int>.ValueCollection)",
                ex.Message);
        }
    }
    // Keys / Values の copy は呼び出し時 (引数を評価した後)。引数の評価で Dictionary を
    // 変えても 3 経路で一致する (期待値は CoreCLR の stdout)
    [CFact]
    public void Dict_KeysValues_CopyAfterArguments()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                static Dictionary<int, int> Pick(Dictionary<int, int> d, List<string> log) { log.Add("r"); return d; }
                static Func<int, bool> Clearing(Dictionary<int, int> d, List<string> log) { log.Add("a"); d.Clear(); return x => true; }
                public static void Main()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var log = new List<string>();
                    Console.WriteLine(Pick(d, log).Values.Any(Clearing(d, log)) ? 1 : 0);
                    Console.WriteLine(string.Join(",", log) + ":" + d.Values.Count);
                }
            }
            """, "P");
        Assert.Equal("0\nr,a:0", output);
    }
    // 副作用のある getter の引数、括弧付き / 三項の receiver でも copy は呼び出し時
    [CFact]
    public void Dict_KeysValues_CopyAfterArguments_GetterAndWrappedReceiver()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                static Dictionary<int, int> s = new Dictionary<int, int> { [1] = 10 };
                static Func<int, bool> ClearingPredicate { get { s.Clear(); return x => true; } }
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                public static void Main()
                {
                    Console.WriteLine(s.Values.Any(ClearingPredicate) ? 1 : 0);
                    var d = new Dictionary<int, int> { [1] = 10 };
                    Console.WriteLine((d.Values).Any(Clear(d)) ? 1 : 0);
                    var e = new Dictionary<int, int> { [2] = 20 };
                    var f = new Dictionary<int, int> { [3] = 30 };
                    var flag = e.Count == 1;
                    Console.WriteLine((flag ? e.Keys : f.Keys).Count(Clear(e)));
                }
            }
            """, "P");
        Assert.Equal("0\n0\n0", output);
    }
    // user 定義の単項演算子を含む引数と、`??` の receiver でも copy は呼び出し時
    [CFact]
    public void Dict_KeysValues_CopyAfterArguments_UnaryOperatorAndCoalesceReceiver()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class Number
            {
                public static int operator -(Number x) { P.s.Clear(); return 0; }
            }
            public class P
            {
                public static Dictionary<int, int> s = new Dictionary<int, int> { [1] = 10 };
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                public static void Main()
                {
                    var n = new Number();
                    Func<int, bool> yes = x => true;
                    Func<int, bool> no = x => false;
                    Console.WriteLine(s.Values.Any(-n == 0 ? yes : no) ? 1 : 0);
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var e = new Dictionary<int, int> { [2] = 20 };
                    Console.WriteLine((d.Values ?? e.Values).Any(Clear(d)) ? 1 : 0);
                }
            }
            """, "P");
        Assert.Equal("0\n0", output);
    }
    // 三項 / `??` の receiver の片側が null でも、他方の view の copy は呼び出し時
    [CFact]
    public void Dict_KeysValues_CopyAfterArguments_NullArmReceiver()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                public static void Main()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    bool flag = d.Count == 1;
                    Console.WriteLine((flag ? d.Values : null).Any(Clear(d)) ? 1 : 0);
                    var e = new Dictionary<int, int> { [1] = 10 };
                    Console.WriteLine((!flag ? null : e.Keys).Any(Clear(e)) ? 1 : 0);
                    var f = new Dictionary<int, int> { [1] = 10 };
                    Console.WriteLine((f.Values ?? null).Any(Clear(f)) ? 1 : 0);
                }
            }
            """, "P");
        Assert.Equal("0\n0\n0", output);
    }
    // null の側に括弧 / cast が付いても、`?? (null)`、switch 式、`?.` (IIFE) の receiver でも、
    // view の copy は呼び出し時
    [CFact]
    public void Dict_KeysValues_CopyAfterArguments_WrappedNullAndIifeReceiver()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                static Func<int, bool> Clear(Dictionary<int, int> d) { d.Clear(); return x => true; }
                static Dictionary<int, int>? Get(Dictionary<int, int> d) => d;
                static Dictionary<int, int> New() => new Dictionary<int, int> { [1] = 10 };
                public static void Main()
                {
                    var d = New();
                    bool flag = d.Count == 1;
                    Console.WriteLine((flag ? d.Values : (null)).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    Console.WriteLine((flag ? d.Values : (Dictionary<int, int>.ValueCollection?)null).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    Console.WriteLine((d.Values ?? (null)).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    Console.WriteLine((Get(d)?.Values).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    Console.WriteLine(Get(d)?.Values.Any(Clear(d)) == true ? 1 : 0);
                    d = New();
                    Console.WriteLine((flag switch { true => d.Values, _ => null }).Any(Clear(d)) ? 1 : 0);
                    d = New();
                    Console.WriteLine((flag ? (flag ? d.Values : null) : null).Any(Clear(d)) ? 1 : 0);
                }
            }
            """, "P");
        Assert.Equal("0\n0\n0\n0\n0\n0\n0", output);
    }
}
