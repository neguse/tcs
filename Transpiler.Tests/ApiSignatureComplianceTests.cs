namespace TinyCs.Tests;

// supported API allowlist は完全シグネチャ単位。メソッド名が同じでも
// Lua runtime が実装していない overload は TCS1002 になる。
public class ApiSignatureComplianceTests
{
    private static string Wrap(string statement) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using TinySystem.Linq;

        public class Api
        {
            public static void Run()
            {
                var list = new List<int> { 1, 2, 3 };
                var names = new List<string> { "a", "b" };
                var dict = new Dictionary<string, int> { { "a", 1 } };
                var s = "a,b";
                {{statement}}
            }
        }
        """;

    [Theory]
    [InlineData("list.Select((x, i) => x + i);", "System.Linq.Enumerable.Select")]
    [InlineData("list.OrderBy(x => x, Comparer<int>.Default);", "System.Linq.Enumerable.OrderBy")]
    [InlineData("list.FirstOrDefault(-1);", "System.Linq.Enumerable.FirstOrDefault")]
    [InlineData("names.ToDictionary(x => x, StringComparer.Ordinal);", "System.Linq.Enumerable.ToDictionary")]
    [InlineData("s.Contains(\"a\", StringComparison.Ordinal);", "string.Contains")]
    [InlineData("s.StartsWith(\"a\", StringComparison.Ordinal);", "string.StartsWith")]
    [InlineData("s.Split(\",\", StringSplitOptions.None);", "string.Split")]
    [InlineData("s.Split(',', ';');", "string.Split")]
    [InlineData("var c = new List<int>(4);", "List<T>")]
    [InlineData("var c = new Dictionary<string, int>(4);", "Dictionary<TKey, TValue>")]
    [InlineData("dict.Remove(\"a\", out var removed);", "Dictionary<TKey, TValue>.Remove")]
    [InlineData("list.Sort(Comparer<int>.Default);", "List<T>.Sort")]
    [InlineData("list.Find(x => x > 1);", "List<T>.Find")]
    [InlineData("Math.Round(1.55f, MidpointRounding.AwayFromZero);", "System.Math.Round")]
    [InlineData("string.Join('-', \"a\", \"b\");", "string.Join")]
    public void UnimplementedOverload_ReportsTcs1002(string statement,
        string expectedApiName)
    {
        var result = Transpiler.TranspileWithDiagnostics([Wrap(statement)]);

        var error = Assert.Single(result.Errors,
            e => e.Contains(TinyCsDiagnosticIds.UnsupportedApi));
        Assert.Contains(expectedApiName, error);
    }

    [Theory]
    [InlineData("list.Sort(); list.Sort((a, b) => b - a);")]
    [InlineData("dict.TryGetValue(\"a\", out var value);")]
    [InlineData("var r = Math.Round(1.234f, 2) + Math.Log(8.0f, 2.0f);")]
    [InlineData("var parts = s.Split(\",\"); var all = s.Split();")]
    [InlineData("var p = s.Split(','); var i = s.IndexOf('a'); var b = s.Contains('a') && char.IsDigit(s[0]);")]
    [InlineData("var i = s.IndexOf(\"a\", 1); var sub = s.Substring(1, 2);")]
    [InlineData("var j = string.Join(\",\", names) + string.Join(\",\", \"x\", \"y\");")]
    [InlineData("var f = list.FirstOrDefault() + list.Count(x => x > 1) + list.Min() + list.Sum();")]
    [InlineData("var any = list.Any() || list.Any(x => x > 2);")]
    [InlineData("var shortest = names.Min(x => x.Length);")]
    [InlineData("var i = list.FindIndex(x => x > 1) + list.RemoveAll(x => x > 2); var m = list.MinBy(x => -x) + list.MaxBy(x => x);")]
    [InlineData("list[0] = list[1]; dict[\"a\"] = dict[\"a\"] + 1;")]
    [InlineData("dict.Clear(); var w = dict.Where((k, v) => v > 0); var n = dict.Count((k, v) => true) + dict.RemoveAll((k, v) => false); var b = dict.Any((k, v) => true) && dict.All((k, v) => true);")]
    public void ImplementedOverload_PassesWithoutTcs1002(string statement)
    {
        var result = Transpiler.TranspileWithDiagnostics([Wrap(statement)]);

        Assert.True(result.Success, string.Join("\n", result.Errors));
    }

    // LINQ は TinySystem.Linq.List の拡張メソッド (即時評価)。System.Linq は
    // namespace ごと TCS1002 で、TinySystem.Linq の using が無い呼び出しは
    // Enumerable に束縛されて止まる
    [Fact]
    public void SystemLinq_WithoutTinySystem_ReportsTcs1002()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            using System.Collections.Generic;
            using System.Linq;

            public class Api
            {
                public static int Run()
                {
                    var list = new List<int> { 1, 2, 3 };
                    var big = list.Where(x => x > 1);
                    return 0;
                }
            }
            """]);

        var error = Assert.Single(result.Errors,
            e => e.Contains(TinyCsDiagnosticIds.UnsupportedApi));
        Assert.Contains("System.Linq.Enumerable.Where", error);
    }

    [Theory]
    [InlineData("list.First();")]
    [InlineData("list.Last();")]
    [InlineData("list.LastOrDefault(x => x > 1);")]
    [InlineData("list.Take(1);")]
    [InlineData("list.Skip(1);")]
    [InlineData("list.ToList();")]
    [InlineData("list.Count();")]
    [InlineData("names.ToDictionary(x => x);")]
    public void DroppedOperation_IsNotInTinySystemLinq(string statement)
    {
        var source = Wrap(statement).Replace("using System.Linq;\n", "");
        var result = Transpiler.TranspileWithDiagnostics([source]);

        Assert.Contains(result.Errors, e => e.Contains("CS1061") || e.Contains("CS1955") || e.Contains("CS7036") || e.Contains("CS1501"));
        Assert.DoesNotContain(result.Errors, e => e.Contains(TinyCsDiagnosticIds.UnsupportedApi));
    }

    [Fact]
    public void TinySystemLinq_ChainBindsWithoutSystemLinq()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            using System.Collections.Generic;
            using TinySystem.Linq;

            public class Api
            {
                public static int Run()
                {
                    var list = new List<int> { 1, 2, 3 };
                    List<int> doubled = list.Where(x => x > 1).Select(x => x * 2);
                    return doubled.Count + list.Count(x => x > 2) + list.Sum();
                }
            }
            """]);

        Assert.True(result.Success, string.Join("\n", result.Errors));
    }
}
