namespace TinyCs.Tests;

// issue #12: ホットパスで遅くなる生成パターンの置き換え。意味は変えず、
// 出力の形 (呼び出し・closure の除去) を確認する。
public class HotPathCodegenTests
{
    [Fact]
    public void ListAdd_UsesLengthIndexStore()
    {
        const string source = """
            using System.Collections.Generic;

            public class Bag
            {
                public List<int> Items = new List<int>();

                public void Put(int v)
                {
                    Items.Add(v);
                    Items.Add(v * 2);
                }
            }

            public static class T
            {
                public static string Test()
                {
                    var xs = new List<int>();
                    for (var i = 0; i < 3; i++) xs.Add(i);
                    var b = new Bag();
                    b.Put(5);
                    return $"{xs.Count}:{xs[2]}:{b.Items.Count}:{b.Items[1]}";
                }
            }
            """;
        var lua = Transpiler.Transpile(source);

        Assert.DoesNotContain("table.insert", lua);
        Assert.Contains("xs[#xs + 1] = i", lua);
        Assert.Equal("3:2:2:10", TestHelper.TranspileAndRun(source, "T.Test()"));
    }

    [Fact]
    public void ListAdd_WithCallArgument_KeepsInsertOrder()
    {
        // 引数評価が同じ List へ Add する場合、`#t + 1` を先に評価すると
        // 上書きになる。呼び出しを含む引数は table.insert のまま
        const string source = """
            using System.Collections.Generic;

            public static class T
            {
                private static List<int> Xs = new List<int>();

                private static int Push(int v)
                {
                    Xs.Add(v);
                    return v * 10;
                }

                public static string Test()
                {
                    Xs.Add(Push(1));
                    return $"{Xs.Count}:{Xs[0]}:{Xs[1]}";
                }
            }
            """;
        Assert.Contains("table.insert", Transpiler.Transpile(source));
        Assert.Equal("2:1:10", TestHelper.TranspileAndRun(source, "T.Test()"));
    }

    [Fact]
    public void TernaryInExpression_NumericBranches_NoClosure()
    {
        const string source = """
            public static class T
            {
                public static int Add(int a, int b) => a + b;

                public static int Test(int n)
                {
                    var total = 0;
                    for (var i = 0; i < n; i++)
                        total = Add(total, i % 2 == 0 ? i : -i);
                    return total;
                }
            }
            """;
        var lua = Transpiler.Transpile(source);

        Assert.DoesNotContain("(function()", lua);
        Assert.Equal("-3", TestHelper.TranspileAndRun(source, "T.Test(6)"));
    }

    [Fact]
    public void TernaryInExpression_FalsyCapableBranches_KeepCSharpValue()
    {
        // bool / nullable 参照の分岐は `c and t or f` が使えない
        // (t が false / nil だと f に落ちる)
        var output = TestHelper.TranspileAndRun("""
            public class Node
            {
                public string? Name;
            }

            public static class T
            {
                public static string Show(bool b) => b ? "T" : "F";

                public static string Test()
                {
                    var flag = true;
                    var none = new Node();
                    var named = new Node { Name = "n" };
                    var a = Show(flag ? false : true);
                    var b = Show(flag ? flag : false);
                    var c = Show(!flag ? true : flag);
                    var d = Show((flag ? none.Name : "x") == null);
                    var e = Show((flag ? "x" : none.Name) == null);
                    var f = Show((!flag ? named.Name : none.Name) == null);
                    return a + b + c + d + e + f;
                }
            }
            """, "T.Test()");

        Assert.Equal("FTTTFT", output);
    }

    [Fact]
    public void NestedTernary_ReturnAndLocal_AreFullyStatementized()
    {
        const string source = """
            public static class T
            {
                public static string Grade(int s)
                {
                    return (s >= 90 ? "A" : s >= 80 ? "B" : s >= 70 ? "C" : "D");
                }

                public static int Pick(bool a, bool b)
                {
                    var r = a ? (b ? 1 : 2) : (b ? 3 : 4);
                    return r;
                }

                public static string Test() =>
                    Grade(95) + Grade(85) + Grade(75) + Grade(10) +
                    Pick(true, true) + Pick(true, false) + Pick(false, true) + Pick(false, false);
            }
            """;
        var lua = Transpiler.Transpile(source);

        Assert.DoesNotContain("(function()", lua);
        Assert.Equal("ABCD1234", TestHelper.TranspileAndRun(source, "T.Test()"));
    }

    [Fact]
    public void TernaryWithSideEffects_EvaluatesOnlyChosenBranch()
    {
        var output = TestHelper.TranspileAndRun("""
            public static class T
            {
                private static string Log = "";

                private static int Mark(string m, int v)
                {
                    Log = Log + m;
                    return v;
                }

                private static int Sum(int a, int b) => a + b;

                public static string Test()
                {
                    var x = Sum(Mark("a", 1), true ? Mark("t", 2) : Mark("f", 3));
                    var y = Sum(false ? Mark("t", 2) : Mark("f", 3), Mark("b", 4));
                    return $"{Log}:{x}:{y}";
                }
            }
            """, "T.Test()");

        Assert.Equal("atfb:3:7", output);
    }

    [Fact]
    public void NewArray_IsDefaultInitializedWithLength()
    {
        var output = TestHelper.TranspileAndRun("""
            public enum Kind { A, B }

            public struct Cell
            {
                public int V;
            }

            public static class T
            {
                public static string Test(int n)
                {
                    var ints = new int[n];
                    var floats = new float[n];
                    var flags = new bool[n];
                    var kinds = new Kind[n];
                    var cells = new Cell[n];
                    var empty = new int[0];
                    cells[0].V = 7;
                    ints[1] += 5;
                    var sum = 0;
                    foreach (var x in ints) sum += x;
                    return $"{ints.Length},{floats.Length},{flags.Length},{kinds.Length},{cells.Length},{empty.Length}|" +
                        $"{sum},{floats[2]},{(flags[2] ? 1 : 0)},{(kinds[2] == Kind.A ? 1 : 0)},{cells[0].V},{cells[1].V}";
                }
            }
            """, "T.Test(3)");

        Assert.Equal("3,3,3,3,3,0|5,0,0,1,7,0", output);
    }

    [Fact]
    public void EnumAndSmallIntegralFields_DefaultToZero()
    {
        var output = TestHelper.TranspileAndRun("""
            public enum State { Idle, Run }

            public class A
            {
                public State S;
                public static State G;
                public byte B;
                public short H;
            }

            public static class T
            {
                public static string Test()
                {
                    var a = new A();
                    var idle = a.S == State.Idle && A.G == State.Idle;
                    return $"{(idle ? 1 : 0)},{a.B + a.H}";
                }
            }
            """, "T.Test()");

        Assert.Equal("1,0", output);
    }

    [Fact]
    public void MathPassThrough_CallsLuaMathDirectly()
    {
        const string source = """
            using System;

            public static class T
            {
                public static string Test(float x)
                {
                    var a = Math.Sqrt(x) + Math.Abs(-x) + Math.Max(x, 2f) + Math.Min(x, 2f);
                    var b = Math.Pow(x, 2) + Math.Floor(x + 0.5f) + Math.Ceiling(x - 0.5f);
                    var c = Math.Round(2.5) + Math.Sign(-x) + Math.Clamp(x, 0f, 1f);
                    return $"{a}|{b}|{c}";
                }
            }
            """;
        var lua = Transpiler.Transpile(source);

        Assert.Contains("math.sqrt(x)", lua);
        Assert.Contains("(x ^ 2)", lua);
        Assert.DoesNotContain("Math.Sqrt", lua);
        Assert.DoesNotContain("Math.Pow", lua);
        // C# 意味論を実装した facade は残す
        Assert.Contains("Math.Round(", lua);
        Assert.Equal("12|24|2",
            TestHelper.TranspileAndRunWithRuntime(source, "T.Test(4)"));
    }

    private const string TypeLocalSource = """
        namespace Game.Core
        {
            public class Counter
            {
                public static int Total;
                public int N;

                public Counter(int n)
                {
                    N = n;
                    Total += n;
                }
            }
        }

        public static class T
        {
            public static int Test()
            {
                var a = new Game.Core.Counter(2);
                var b = new Game.Core.Counter(3);
                return Game.Core.Counter.Total * 10 + a.N + b.N;
            }
        }
        """;

    [Fact]
    public void TypeTables_AreChunkLocalsAndStillPublishedAsGlobals()
    {
        var lua = Transpiler.Transpile(TypeLocalSource);

        Assert.Contains("local Counter, T = Counter, T", lua);
        // 別 chunk (host) からは従来どおり global で引ける
        var script = $"assert(load({EscapeLong(lua)}))()\n" +
            "print(_G.T.test(), rawget(_G, 'Counter') ~= nil)";
        Assert.Equal("55\ttrue", TestHelper.RunLua(script).Trim());
    }

    [Fact]
    public void TypeTables_StayGlobalForHotReloadBuilds()
    {
        var lua = Transpiler.Transpile([TypeLocalSource], instanceRegistry: true);

        Assert.DoesNotContain("local Counter", lua);
        Assert.DoesNotContain("_ENV.Counter", lua);
        Assert.Equal("55", TestHelper.RunLua(lua + "\nprint(T.test())").Trim());
    }

    [Fact]
    public void TypeTables_TooManyTypes_FallBackToGlobals()
    {
        // Lua の local 上限 (200/関数) に近づく program では local 化しない
        var types = string.Concat(Enumerable.Range(0, 130)
            .Select(i => $"public class C{i} {{ public static int V = {i}; }}\n"));
        var lua = Transpiler.Transpile(types +
            "public static class T { public static int Test() => C129.V + C0.V; }");

        Assert.DoesNotContain("local C0", lua);
        Assert.Equal("129", TestHelper.RunLua(lua + "\nprint(T.test())").Trim());
    }

    [Fact]
    public void ModuleRegistryEnv_ResolvesTypesThenHostWithoutFunctionIndex()
    {
        var registry = TestHelper.FindProjectFile(LuaRuntime.RegistryRelativePath);
        var output = TestHelper.RunLua($$"""
            local Reg = dofile("{{registry}}")
            local reg = Reg.new(_G)
            local mt = getmetatable(reg.env)
            print(type(mt.__index))
            reg:applyBatch({ revision = 1, modules = { {
              id = "m", hash = "h1",
              types = { { id = "m#Box", name = "Box", kind = "class",
                statics = {}, keys = { "__index", "get" } } },
              define = function(_ENV)
                Box.__index = Box
                function Box.get() return math.max(1, 2) end
              end,
              inits = {}, initfns = {} } } })
            print(reg.env.Box.get(), reg.env.print == print, reg.env.Missing)
            """);

        Assert.Equal("table\n2\ttrue\tnil", output.Trim().Replace("\r", ""));
    }

    private static string EscapeLong(string lua) => $"[==[{lua}]==]";
}
