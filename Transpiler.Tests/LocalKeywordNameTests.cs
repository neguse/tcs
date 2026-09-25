namespace TinyCs.Tests;

// issue #7: C# では合法な local 束縛名が Lua 予約語 (local / end / then ...)
// と衝突しても、tcs が安全な出力名を割り当てる (ゲーム側に Lua の命名制約を
// 持ち込まない)。宣言・参照・closure 内参照で同じ写像を使い、元のソースの
// `local_` のような名前とも衝突しない。
public class LocalKeywordNameTests
{
    private static void AssertNoKeywordWarning(string source)
    {
        var result = Transpiler.TranspileWithDiagnostics([source],
            checkNaming: false);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("LuaKeywordIdentifier"));
    }

    [Fact]
    public void LocalNamedLocal_IssueRepro()
    {
        const string source = """
            public static class KeywordRepro
            {
                public static int Value()
                {
                    var local = 1;
                    return local;
                }
            }
            """;
        AssertNoKeywordWarning(source);
        Assert.Equal("1", TestHelper.TranspileAndRun(source, "KeywordRepro.Value()"));
    }

    [Fact]
    public void KeywordLocal_DoesNotCollideWithSuffixedName()
    {
        var output = TestHelper.TranspileAndRun("""
            public static class T
            {
                public static int Test()
                {
                    var local = 1;
                    var local_ = 2;
                    var local__ = 3;
                    return local * 100 + local_ * 10 + local__;
                }
            }
            """, "T.Test()");

        Assert.Equal("123", output);
    }

    [Fact]
    public void KeywordLocal_CapturedByClosure()
    {
        var output = TestHelper.TranspileAndRun("""
            using System;

            public static class T
            {
                public static int Test()
                {
                    var end = 5;
                    Func<int> read = () => end + 1;
                    Action bump = () => { end = end * 2; };
                    bump();
                    return read() * 100 + end;
                }
            }
            """, "T.Test()");

        Assert.Equal("1110", output);
    }

    [Fact]
    public void KeywordParameters_MethodCtorOperatorAndDefaults()
    {
        var output = TestHelper.TranspileAndRun("""
            public class Box
            {
                public int Value;

                public Box(int end)
                {
                    Value = end;
                }

                public static Box operator +(Box and, Box or) => new Box(and.Value + or.Value);

                public int Scale(int then, int until = 3) => Value * then + until;
            }

            public static class T
            {
                public static int Test()
                {
                    var sum = new Box(2) + new Box(5);
                    return sum.Scale(10) * 100 + sum.Scale(1, until: 0);
                }
            }
            """, "T.Test()");

        Assert.Equal("7307", output);
    }

    [Fact]
    public void KeywordLoopAndLambdaVariables()
    {
        var output = TestHelper.TranspileAndRunWithRuntime("""
            using System.Collections.Generic;
            using System.Linq;

            public static class T
            {
                public static int Test()
                {
                    var values = new List<int> { 1, 2, 3, 4 };
                    var total = 0;
                    foreach (var elseif in values) total += elseif;
                    for (var repeat = 0; repeat < 3; repeat++) total += repeat;
                    var big = values.Where(nil => nil > 2).Count();
                    return total * 10 + big;
                }
            }
            """, "T.Test()");

        Assert.Equal("132", output);
    }

    [Fact]
    public void KeywordForVariable_CapturedUsesWhileLowering()
    {
        var output = TestHelper.TranspileAndRun("""
            using System;
            using System.Collections.Generic;

            public static class T
            {
                public static int Test()
                {
                    var readers = new List<Func<int>>();
                    for (var until = 0; until < 3; until++)
                        readers.Add(() => until);
                    return readers[0]() + readers[2]();
                }
            }
            """, "T.Test()");

        Assert.Equal("6", output);
    }

    [Fact]
    public void KeywordPatternDesignations()
    {
        var output = TestHelper.TranspileAndRun("""
            public static class T
            {
                public static int Read(object value)
                {
                    if (value is int function) return function;
                    return -1;
                }

                public static int Arm(object value) => value switch
                {
                    int nil => nil + 1,
                    _ => 0,
                };

                public static int Case(object value)
                {
                    switch (value)
                    {
                        case int @goto:
                            return @goto * 2;
                        default:
                            return 0;
                    }
                }

                public static int Test() => Read(4) * 100 + Arm(4) * 10 + Case(1);
            }
            """, "T.Test()");

        Assert.Equal("452", output);
    }

    [Fact]
    public void KeywordOutVariable()
    {
        var output = TestHelper.TranspileAndRun("""
            using System.Collections.Generic;

            public static class T
            {
                public static int Test()
                {
                    var map = new Dictionary<string, int> { ["a"] = 7 };
                    if (map.TryGetValue("a", out var then)) return then;
                    return 0;
                }
            }
            """, "T.Test()");

        Assert.Equal("7", output);
    }

    [Fact]
    public void KeywordDeconstructionTargets()
    {
        var output = TestHelper.TranspileAndRun("""
            public record Pair(int Left, int Right);

            public static class T
            {
                public static int Test()
                {
                    var (local, until) = new Pair(3, 4);
                    return local * 10 + until;
                }
            }
            """, "T.Test()");

        Assert.Equal("34", output);
    }

    [Fact]
    public void GlobalLocal_IsMappedForNonCompatLua()
    {
        // `global` は Lua 5.5 の予約語 (compat ビルドでのみ名前として通る)
        var result = Transpiler.TranspileWithDiagnostics(["""
            public static class T
            {
                public static int Test()
                {
                    var global = 2;
                    return global + 1;
                }
            }
            """]);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.DoesNotContain("local global", result.Lua);
        Assert.Equal("3", TestHelper.RunLua(result.Lua + "\nprint(T.test())").Trim());
    }

    [Fact]
    public void KeywordLocal_InTopLevelStatements()
    {
        var output = TestHelper.TranspileAndRun("""
            var local = 3;
            var @end = local + 1;
            var check = local * 10 + @end;
            """, "check");

        Assert.Equal("34", output);
    }

    [Fact]
    public void KeywordLocal_InLegacyFallbackBody()
    {
        // 早期 break を含む switch 文は IL 未対応で legacy visitor 経路になる
        const string source = """
            public static class T
            {
                public static int Test(int n)
                {
                    var then = 0;
                    switch (n)
                    {
                        case 1:
                            if (then == 0) break;
                            then = 5;
                            break;
                        default:
                            then = n * 2;
                            break;
                    }
                    return then;
                }
            }
            """;
        var result = Transpiler.TranspileWithDiagnostics([source]);
        Assert.True(result.LegacyBodies > 0, "expected a legacy-emitted body");
        Assert.Equal("8", TestHelper.TranspileAndRun(source, "T.Test(4)"));
    }

    [Fact]
    public void KeywordLocals_ProduceNoKeywordWarnings()
    {
        AssertNoKeywordWarning("""
            using System.Collections.Generic;

            public class Walker
            {
                public int Wait(int @end, int then) => @end + then;

                public int Sum(List<int> values)
                {
                    var repeat = 0;
                    foreach (var elseif in values) repeat += elseif;
                    return repeat;
                }

                public int Read(object value) => value is int function ? function : 0;
            }
            """);
    }

    [Fact]
    public void KeywordMembers_StillReportUnsupportedSyntax()
    {
        // member 名 (field / property / positional record parameter) は
        // 対象外。local 束縛だけを写す
        var result = Transpiler.TranspileWithDiagnostics(["""
            public record Span(int until);

            public class Holder
            {
                public int repeat;
            }
            """], checkNaming: false);

        Assert.Contains(result.Warnings, w => w.Contains("LuaKeywordIdentifier(until)"));
        Assert.Contains(result.Warnings, w => w.Contains("LuaKeywordIdentifier(repeat)"));
    }
}
