namespace TinyCs.Tests;

// 値型・string の型パターンは Lua 側に型 table が無く、
// `getmetatable(x) == int` (未定義 global = nil 比較) だと nil がマッチする。
// type() 判定 (number/boolean/string) を emit する。
public class TypePatternTests
{
    [Fact]
    public void IsInt_OnNullable_MatchesOnlyValues()
    {
        var result = TestHelper.TranspileAndRun("""
            public class T
            {
                public static string Test()
                {
                    int? none = null;
                    int? some = 7;
                    var a = none is int;
                    int b;
                    if (some is int x) b = x; else b = -1;
                    return $"{a}|{b}";
                }
            }
            """, "T.Test()");
        Assert.Equal("false|7", result);
    }

    [Fact]
    public void IsBoolAndString_MatchByLuaType()
    {
        var result = TestHelper.TranspileAndRun("""
            public class T
            {
                public static string Test()
                {
                    bool? flag = false;
                    string? name = null;
                    string? given = "ok";
                    string a;
                    if (flag is bool b) a = b ? "t" : "f"; else a = "none";
                    var c = name is string ? "str" : "nil";
                    string d;
                    if (given is string s) d = s; else d = "nil";
                    return $"{a}|{c}|{d}";
                }
            }
            """, "T.Test()");
        Assert.Equal("f|nil|ok", result);
    }

    [Fact]
    public void SwitchExpression_ValueTypeArm_DoesNotMatchNull()
    {
        var result = TestHelper.TranspileAndRun("""
            public class T
            {
                public static int Classify(int? n) => n switch
                {
                    int v => v,
                    _ => -1,
                };

                public static string Test() =>
                    $"{Classify(5)}|{Classify(null)}";
            }
            """, "T.Test()");
        Assert.Equal("5|-1", result);
    }

    // 式文脈 (ternary / 複合条件 / lambda) の is-pattern designation 束縛
    [Fact]
    public void IsPattern_InTernary_BindsDesignation()
    {
        var result = TestHelper.TranspileAndRun("""
            public class T
            {
                public static string Test()
                {
                    int? some = 7;
                    int? none = null;
                    var a = some is int x ? x : -1;
                    var b = none is int y ? y : -1;
                    return $"{a}|{b}";
                }
            }
            """, "T.Test()");
        Assert.Equal("7|-1", result);
    }

    [Fact]
    public void IsPattern_InCompoundCondition_BindsDesignation()
    {
        var result = TestHelper.TranspileAndRun("""
            public class Shape { }
            public class Circle
            {
                public int R;
            }
            public class T
            {
                public static string Classify(object s)
                {
                    if (s is Circle c && c.R > 5) return $"big:{c.R}";
                    return "other";
                }

                public static string Test() =>
                    Classify(new Circle { R = 9 }) + "|" + Classify(new Shape());
            }
            """, "T.Test()");
        Assert.Equal("big:9|other", result);
    }

    [Fact]
    public void IsPattern_InLambda_BindsDesignation()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using TinySystem.Linq;

            public class Shape { }
            public class Circle
            {
                public int R;
            }
            public class T
            {
                public static int Test()
                {
                    var shapes = new List<object>
                    {
                        new Circle { R = 1 },
                        new Shape(),
                        new Circle { R = 9 },
                    };
                    return shapes.Count(o => o is Circle c && c.R > 2);
                }
            }
            """, "T.Test()");
        Assert.Equal("1", result);
    }

    [Fact]
    public void ClassPattern_StillUsesMetatable()
    {
        var result = TestHelper.TranspileAndRun("""
            public class Shape { }
            public class Circle { }
            public class T
            {
                public static string Test()
                {
                    object s = new Circle();
                    object? n = null;
                    var a = s is Circle ? "circle" : "other";
                    var b = n is Circle ? "circle" : "none";
                    return $"{a}|{b}";
                }
            }
            """, "T.Test()");
        Assert.Equal("circle|none", result);
    }

    [Fact]
    public void IsUnrelatedOrNull_False()
    {
        var result = TestHelper.TranspileAndRun("""
            public class Animal { }
            public class Rock { }
            public class T
            {
                public static string Test()
                {
                    object r = new Rock();
                    object? n = null;
                    return $"{r is Animal}|{n is Animal}";
                }
            }
            """, "T.Test()");
        Assert.Equal("false|false", result);
    }

    // 派生の無い class の `is` は metatable の直接比較 (user の class は継承できない。q.md Q15)。
    // 派生があるのは `--ref` stub の class だけで、その基底への `is` は chain を辿る __tcs_is のまま
    [Fact]
    public void IsClass_WithoutDerived_ComparesMetatableDirectly()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            public interface IShape { }
            public class Sq : IShape { public int S; }
            public class Ci : IShape { public int R; }
            public class T
            {
                public static int Test(IShape v) => v is Sq s ? s.S : v switch { Ci c => c.R, _ => -1 };
            }
            """]);

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Contains("getmetatable(", result.Lua);
        // prelude の定義 1 回だけで、呼び出しは無い
        Assert.Equal(1, result.Lua.Split("__tcs_is(").Length - 1);
    }

    [Fact]
    public void IsClass_WithDerivedStub_WalksMetatableChain()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            public class T
            {
                public static bool Test(object v) => v is BaseOptions;
            }
            """], null, ["""
            public class BaseOptions { public int Width; }
            public class Options : BaseOptions { public int Height; }
            """]);

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.True(result.Lua.Split("__tcs_is(").Length - 1 > 1, "派生のある基底への is は chain を辿る");
    }

    [Fact]
    public void IsClass_DirectComparison_RejectsNullAndOtherValues()
    {
        var result = TestHelper.TranspileAndRun("""
            public class A { public int X; }
            public class B { public int Y; }
            public class T
            {
                public static string Test()
                {
                    object a = new A { X = 1 };
                    object b = new B { Y = 2 };
                    object n = null;
                    object s = "str";
                    object i = 5;
                    return (a is A) + ":" + (b is A) + ":" + (n is A) + ":" + (s is A) + ":" + (i is A);
                }
            }
            """, "T.Test()");
        Assert.Equal("true:false:false:false:false", result);
    }
}
