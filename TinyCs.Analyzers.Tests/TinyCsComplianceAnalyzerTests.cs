using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace TinyCs.Analyzers.Tests;

public partial class TinyCsComplianceAnalyzerTests
{
    [Fact]
    public async Task SupportedSubset_HasNoDiagnostics()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Player
            {
                public int X { get; set; }

                public static int Move(int x)
                {
                    if (x > 0) return x + 1;
                    return 0;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LuaKeywordMember_ReportsKeywordMemberOnly()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Repro
            {
                public int end_;
                public void end() { end_ = 1; }
                public int flash;
                public void Flash() { flash = 1; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedSyntax, diagnostic.Id);
        Assert.Contains("LuaKeywordIdentifier(end)", diagnostic.GetMessage());
        Assert.Equal(3, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task StructDeclaration_InstanceMembersClean_StaticReports()
    {
        // instance member (method/property/ctor) は許可、
        // static member は引き続き拒否
        var clean = await AnalyzeAsync("""
            public struct Vec2
            {
                public int X;
                public Vec2(int x) { X = x; }
                public int Twice() { return X * 2; }
                public int P { get; set; }
            }
            """);
        Assert.Empty(clean);

        var withStatic = await AnalyzeAsync("""
            public struct Vec2
            {
                public int X;
                public static int Make() { return 1; }
            }
            """);
        var diagnostic = Assert.Single(withStatic);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedSyntax, diagnostic.Id);
        Assert.Contains("StructMember", diagnostic.GetMessage());
    }

    [Fact]
    public async Task RecordStruct_IsClean_StaticMemberReports()
    {
        // record struct は対応済み。static member は struct と同じ規則
        var clean = await AnalyzeAsync("""
            public readonly record struct Vec2(int X, int Y);
            """);
        Assert.Empty(clean);

        var withStatic = await AnalyzeAsync("""
            public record struct Vec2(int X, int Y)
            {
                public static int Make() { return 1; }
            }
            """);
        var diagnostic = Assert.Single(withStatic);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedSyntax, diagnostic.Id);
        Assert.Contains("StructMember", diagnostic.GetMessage());
    }

    [Fact]
    public async Task NamedArgument_ReportsUnsupportedSyntax()
    {
        // transpiler は build を止める error、analyzer は warning のまま
        // (.editorconfig で severity を上げられる)
        var diagnostics = await AnalyzeAsync("""
            public class T
            {
                public static int F(int x, int y = -1) => x;
                public static int Test() => F(y: 2, x: 1);
            }
            """);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal(TinyCsDiagnosticIds.UnsupportedSyntax, d.Id);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("NamedArgument", d.GetMessage());
        });
    }

    [Fact]
    public async Task TypeNamedLikeRuntimeGlobal_ReportsUnsupportedSyntax()
    {
        // namespace の中でも simple name で emit され runtime の table を
        // 上書きする (#21)。transpiler では build を止める error
        var math = await AnalyzeAsync("""
            public class Math
            {
                public static int Twice(int x) => x * 2;
            }
            """);
        var diagnostic = Assert.Single(math);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedSyntax, diagnostic.Id);
        Assert.Contains("RuntimeGlobalIdentifier(Math)", diagnostic.GetMessage());

        var dict = await AnalyzeAsync("""
            namespace Game
            {
                public class Dict { }
            }
            """);
        Assert.Contains("RuntimeGlobalIdentifier(Dict)",
            Assert.Single(dict).GetMessage());

        var clean = await AnalyzeAsync("""
            namespace Game
            {
                public class MathUtil
                {
                    public static int Twice(int x) => System.Math.Abs(x) * 2;
                }
            }
            """);
        Assert.Empty(clean);
    }

    [Theory]
    [InlineData("CallerArgumentExpression(\"value\")", "CallerArgumentExpression")]
    [InlineData("CallerMemberName", "CallerMemberName")]
    [InlineData("CallerLineNumber", "CallerLineNumber")]
    [InlineData("System.Runtime.CompilerServices.CallerFilePathAttribute",
        "CallerFilePath")]
    public async Task CallerInfoAttribute_ReportsUnsupportedSyntax(
        string attribute, string kind)
    {
        // caller info は呼び出し側で埋まる値を tcs が再現しない (#17)
        var type = kind == "CallerLineNumber" ? "int" : "string";
        var dflt = kind == "CallerLineNumber" ? "0" : "\"\"";
        var diagnostics = await AnalyzeAsync($$"""
            using System.Runtime.CompilerServices;

            public static class CallerInfoRepro
            {
                public static {{type}} Where(int value,
                    [{{attribute}}] {{type}} text = {{dflt}}) => text;

                public static {{type}} Test() => Where(1 + 2);
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedSyntax, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains($"CallerInfoAttribute({kind})", diagnostic.GetMessage());
    }

    [Fact]
    public async Task DoubleAndLongTypesAndDoubleLiterals_ReportUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            public class NumericTypes
            {
                public double Field;
                public long Signed;
                public ulong Unsigned;

                public static double Run(long value, ulong other)
                {
                    double result = 1.5;
                    float supported = 2.5f;
                    return result + value + other + supported;
                }
            }
            """);

        var syntaxDiagnostics = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax)
            .ToArray();

        Assert.Equal(8, syntaxDiagnostics.Length);
        Assert.Equal(3, syntaxDiagnostics.Count(
            d => d.GetMessage().Contains("DoubleType")));
        Assert.Equal(4, syntaxDiagnostics.Count(
            d => d.GetMessage().Contains("LongType")));
        Assert.Single(syntaxDiagnostics,
            d => d.GetMessage().Contains("DoubleLiteral"));
        Assert.DoesNotContain(syntaxDiagnostics,
            d => d.Location.SourceTree?.GetText()
                .ToString(d.Location.SourceSpan) == "2.5f");
    }

    [Fact]
    public async Task PartialTypesAndLock_ReportUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            public partial class PartialClass
            {
                public static int First() => 1;
            }
            public partial class PartialClass
            {
                public static int Second() => 2;
            }
            public partial record PartialRecord;
            public partial interface IPartial { }

            public class RegularClass
            {
                public static void Run()
                {
                    lock (new object())
                    {
                    }
                }
            }

            public record RegularRecord;
            public interface IRegular { }
            """);

        var syntaxDiagnostics = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax)
            .ToArray();

        Assert.Equal(5, syntaxDiagnostics.Length);
        Assert.Equal(4, syntaxDiagnostics.Count(
            d => d.GetMessage().Contains("PartialTypeDeclaration")));
        Assert.Single(syntaxDiagnostics,
            d => d.GetMessage().Contains("LockStatement"));
    }

    [Fact]
    public async Task PartialStructs_KeepExistingSingleDiagnostic()
    {
        var diagnostics = await AnalyzeAsync("""
            public partial struct PartialStruct { }
            public readonly partial record struct PartialRecordStruct(int Value);
            """);

        var syntaxDiagnostics = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax)
            .ToArray();

        // record struct 解禁後は partial record struct も
        // PartialTypeDeclaration の 1 診断のみ
        Assert.Equal(2, syntaxDiagnostics.Length);
        Assert.All(syntaxDiagnostics,
            d => Assert.Contains("PartialTypeDeclaration", d.GetMessage()));
        Assert.DoesNotContain(syntaxDiagnostics,
            d => d.GetMessage().Contains("StructDeclaration')")); // 二重報告なし
    }

    // nameof は定数式として対応 (T250): 診断なし
    [Fact]
    public async Task NameOfExpressions_AreSupported()
    {
        const string source = """
            public class NameDemo
            {
                public static string Simple(int value) => nameof(value);
                public static string MemberName() => nameof(System.Math.E);
                public static string TypeName() => nameof(System.DateTime);
            }
            """;
        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }
    [Fact]
    public async Task UserMethodNamedNameof_IsNotNameOfExpression()
    {
        var diagnostics = await AnalyzeAsync("""
            public class NameDemo
            {
                public static string nameof(string value) => value;
                public static string Run() => nameof("ok");
            }
            """);

        Assert.DoesNotContain(diagnostics,
            d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax);
    }

    [Fact]
    public async Task UnsupportedStatements_ReportUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Demo
            {
                public static int Run()
                {
                    int Local() => 1;
                    try
                    {
                        return Local();
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("LocalFunctionStatement"));
        Assert.Contains(diagnostics, d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("TryStatement"));
        Assert.Contains(diagnostics, d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("ThrowStatement"));
    }

    [Fact]
    public async Task ListPattern_ReportsUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Demo
            {
                public static bool Run(int[] values)
                {
                    return values is [1, 2];
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedSyntax, diagnostic.Id);
        Assert.Contains("ListPattern", diagnostic.GetMessage());
    }

    [Fact]
    public async Task UnsupportedBclApi_ReportsUnsupportedApi()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.IO;

            public class Demo
            {
                public static string Load(string path)
                {
                    return File.ReadAllText(path);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedApi, diagnostic.Id);
        Assert.Contains("System.IO.File.ReadAllText", diagnostic.GetMessage());
    }

    [Fact]
    public async Task FullyQualifiedApis_ReportOnlyUnsupportedMember()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Demo
            {
                public static int Supported() => System.Math.Min(3, 7);

                public static string Unsupported() =>
                    System.IO.File.ReadAllText("save.dat");
            }
            """);

        var diagnostic = Assert.Single(diagnostics,
            value => value.Id == TinyCsDiagnosticIds.UnsupportedApi);
        Assert.Contains("System.IO.File.ReadAllText", diagnostic.GetMessage());
    }

    [Fact]
    public async Task DiagnosticSeverity_CanBeOverridden()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.IO;

            public class Demo
            {
                public static string Load(string path)
                {
                    return File.ReadAllText(path);
                }
            }
            """,
            new Dictionary<string, ReportDiagnostic>
            {
                [TinyCsDiagnosticIds.UnsupportedApi] = ReportDiagnostic.Error,
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(TinyCsDiagnosticIds.UnsupportedApi, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task UnsupportedCoreLibraryMembers_ReportUnsupportedApi()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Demo
            {
                public static float Run()
                {
                    var values = new List<int> { 1 };
                    values.Reverse();
                    var one = values.Single();
                    var capacity = values.Capacity;
                    var empty = string.Empty;
                    return (float)(Math.Cbrt(one) + Math.E + capacity + empty.Length);
                }
            }
            """);

        var apiDiagnostics = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedApi)
            .ToArray();

        Assert.Equal(6, apiDiagnostics.Length);
        Assert.Contains(apiDiagnostics,
            d => d.GetMessage().Contains("List<T>.Reverse"));
        Assert.Contains(apiDiagnostics,
            d => d.GetMessage().Contains("System.Linq.Enumerable.Single"));
        Assert.Contains(apiDiagnostics,
            d => d.GetMessage().Contains("System.Math.Cbrt"));
        Assert.Contains(apiDiagnostics,
            d => d.GetMessage().Contains("List<T>.Capacity"));
        Assert.Contains(apiDiagnostics,
            d => d.GetMessage().Contains("string.Empty"));
        Assert.Contains(apiDiagnostics,
            d => d.GetMessage().Contains("System.Math.E"));
    }

    [Fact]
    public async Task CollectionNulls_ReportUnsupportedCollectionNull()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.Collections.Generic;
            using TinySystem.Linq;

            public class Demo
            {
                public static void Run()
                {
                    var list = new List<string> { null };
                    list.Add(default);
                    list[0] = null;

                    var dict = new Dictionary<string, string>
                    {
                        { "a", null }
                    };
                    dict["b"] = default;

                }
            }
            """);

        var collectionNulls = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedCollectionNull)
            .ToArray();

        Assert.Equal(5, collectionNulls.Length);
        Assert.All(collectionNulls,
            d => Assert.Contains("null storage", d.GetMessage()));
        Assert.Contains(collectionNulls,
            d => d.GetMessage().Contains("List<T>"));
        Assert.Contains(collectionNulls,
            d => d.GetMessage().Contains("Dictionary<K,V>"));
    }

    [Fact]
    public async Task NullOutsideCollections_HasNoCollectionNullDiagnostic()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Demo
            {
                public static bool Run()
                {
                    string value = null;
                    return value == null;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics,
            d => d.Id == TinyCsDiagnosticIds.UnsupportedCollectionNull);
    }

    // #20: 要素の default が nil になる `new T[n]`、初期化子の null 要素、
    // `a[i] = null` は TCS1003。値型要素の配列と、int? / object へ変換される
    // `default(int)` は対象外
    // (transpiler 側の DiagnosticTests.ArrayNullSource と同じ source)
    [Fact]
    public async Task ArrayNulls_ReportUnsupportedCollectionNull()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.Collections.Generic;
            public class Foo { public int V; }
            public struct S { public int X; }
            public enum E { A = 3 }
            public class Pool<T> { public T[] Items = new T[2]; }
            public class Box<T> where T : struct { public T[] Make(int n) => new T[n]; }
            public class Bag<T>
            {
                public T[] Make(T x) => new T[] { x, default, x };
                public void Clear(T[] a) { a[0] = default; }
                public void Push(List<T> l) { l.Add(default); }
            }
            public class Demo
            {
                public static void Run()
                {
                    var strings = new string[3];
                    var foos = new Foo[2];
                    var maybe = new int?[2];
                    var jagged = new int[2][];
                    var withNull = new string[] { "a", null };
                    var implicitNull = new[] { "a", (string)null };
                    strings[0] = null;

                    var ints = new int[3];
                    var structs = new S[2];
                    var enums = new E[2];
                    var chars = new char[2];
                    var flags = new bool[2];
                    var fine = new string[] { "a", "b" };
                    fine[0] = "c";
                    var inner = new int[][] { new int[2], new int[3] };
                    var liftedZero = new int?[] { default(int) };
                    var boxedZero = new object[] { default(int) };
                    var list = new List<string>();
                }
            }
            """);

        var collectionNulls = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedCollectionNull)
            .ToArray();

        Assert.Equal(12, collectionNulls.Length);
        Assert.DoesNotContain(diagnostics,
            d => d.Id != TinyCsDiagnosticIds.UnsupportedCollectionNull);
        Assert.All(collectionNulls,
            d => Assert.Contains("Lua sequence tables", d.GetMessage()));
        Assert.Contains(collectionNulls,
            d => d.GetMessage().Contains("int[][] created by size"));
        Assert.Equal(3, collectionNulls.Count(
            d => d.GetMessage().Contains("string[] cannot store null elements")));
        Assert.Equal(2, collectionNulls.Count(
            d => d.GetMessage().Contains("T[] created by size")));
        Assert.Equal(2, collectionNulls.Count(
            d => d.GetMessage().Contains("T[] cannot store null elements")));
        Assert.Single(collectionNulls,
            d => d.GetMessage().Contains("List<T> cannot store"));
    }

    [Fact]
    public async Task SupportedOperatorOverloads_HaveNoDiagnostics()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Vec2
            {
                public float X;
                public float Y;

                public Vec2(float x, float y)
                {
                    X = x;
                    Y = y;
                }

                public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
                public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
                public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
                public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
                public static Vec2 operator *(float s, Vec2 a) => new Vec2(s * a.X, s * a.Y);
                public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
                public static Vec2 operator %(Vec2 a, Vec2 b) => new Vec2(a.X % b.X, a.Y % b.Y);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UnsupportedOperatorOverloads_ReportUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Id
            {
                public int Value;

                public static bool operator ==(Id a, Id b) => a.Value == b.Value;
                public static bool operator !=(Id a, Id b) => a.Value != b.Value;
                public static implicit operator int(Id a) => a.Value;
                public override bool Equals(object o) => false;
                public override int GetHashCode() => 0;
            }
            """);

        var syntaxDiagnostics = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax)
            .ToArray();

        // Equals / GetHashCode の override も TCS1001 (継承は対象外、q.md Q15)
        Assert.Equal(5, syntaxDiagnostics.Length);
        Assert.Equal(2, syntaxDiagnostics.Count(
            d => d.GetMessage().Contains("'OverrideMember'")));
        Assert.Contains(syntaxDiagnostics,
            d => d.GetMessage().Contains("OperatorDeclaration(==)"));
        Assert.Contains(syntaxDiagnostics,
            d => d.GetMessage().Contains("OperatorDeclaration(!=)"));
        Assert.Contains(syntaxDiagnostics,
            d => d.GetMessage().Contains("ConversionOperatorDeclaration"));
    }

    [Fact]
    public async Task OutRefParameters_ReportUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Parser
            {
                public static bool TryParse(string text, out int value)
                {
                    value = 0;
                    return false;
                }

                public static void Bump(ref int value)
                {
                    value = value + 1;
                }
            }
            """);

        var syntaxDiagnostics = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax)
            .ToArray();

        Assert.Equal(2, syntaxDiagnostics.Length);
        Assert.Contains(syntaxDiagnostics,
            d => d.GetMessage().Contains("OutParameter"));
        Assert.Contains(syntaxDiagnostics,
            d => d.GetMessage().Contains("RefParameter"));
    }

    // transpiler check 側 ApiSignatureComplianceTests と同じケースで
    // Analyzer が同じ TCS1002 判定になることを固定する。
    private static string WrapApiStatement(string statement) => $$"""
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
    public async Task UnimplementedOverload_ReportsTcs1002(string statement,
        string expectedApiName)
    {
        var diagnostics = await AnalyzeAsync(WrapApiStatement(statement));

        var diagnostic = Assert.Single(diagnostics,
            d => d.Id == TinyCsDiagnosticIds.UnsupportedApi);
        Assert.Contains(expectedApiName, diagnostic.GetMessage());
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
    [InlineData("var ks = dict.Keys.OrderBy(k => k); var vs = dict.Values.Sum() + dict.Values.Where(v => v > 0).Count + dict.Keys.Count(k => k.Length > 0);")]
    [InlineData("dict.Clear(); var w = dict.Where((k, v) => v > 0); var n = dict.Count((k, v) => true) + dict.RemoveAll((k, v) => false); var b = dict.Any((k, v) => true) && dict.All((k, v) => true);")]
    public async Task ImplementedOverload_PassesWithoutTcs1002(string statement)
    {
        var diagnostics = await AnalyzeAsync(WrapApiStatement(statement));

        Assert.DoesNotContain(diagnostics,
            d => d.Id == TinyCsDiagnosticIds.UnsupportedApi);
    }

    private static async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(
        string source,
        IDictionary<string, ReportDiagnostic>? specificDiagnosticOptions = null,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview));
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary);
        if (specificDiagnosticOptions != null)
            options = options.WithSpecificDiagnosticOptions(
                specificDiagnosticOptions);

        var compilation = CSharpCompilation.Create(
            "TinyCsAnalyzerTests",
            [tree],
            GetReferences().Concat(extraReferences ?? []),
            options);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new TinyCsComplianceAnalyzer());
        var diagnostics = await compilation.WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics
            .OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Character)
            .ToArray();
    }

    private static IReadOnlyList<MetadataReference> GetReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyNames = new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Console.dll",
            "System.IO.FileSystem.dll",
        };

        return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(
                    typeof(global::TinySystem.Random).Assembly.Location),
            }
            .Concat(assemblyNames
                .Select(name => Path.Combine(runtimeDir, name))
                .Where(File.Exists)
                .Select(path => MetadataReference.CreateFromFile(path)))
            .ToArray();
    }

    [Fact]
    public async Task SystemLinq_WithoutTinySystem_ReportsTcs1002()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.Collections.Generic;
            using System.Linq;

            public class Demo
            {
                public static int Run()
                {
                    var list = new List<int> { 1, 2, 3 };
                    var big = list.Where(x => x > 1);
                    return 0;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics,
            d => d.Id == TinyCsDiagnosticIds.UnsupportedApi);
        Assert.Contains("System.Linq.Enumerable.Where", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ForeachDeconstruct_OnlyDictionaryPairIsSupported()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.Collections.Generic;
            public class Demo
            {
                public static int Run()
                {
                    var pairs = new List<(int, int)> { (1, 2) };
                    var d = new Dictionary<string, int> { ["a"] = 1 };
                    var n = 0;
                    foreach (var (a, b) in pairs) n += a + b;
                    foreach (var (k, v) in d) n += v;
                    foreach (var (k, _) in d) n += k.Length;
                    return n;
                }
            }
            """);

        var unsupported = diagnostics.Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("ForEachVariableStatement")).ToArray();
        Assert.Equal(2, unsupported.Length);
    }

    [Fact]
    public async Task KeysValues_CannotBeStored()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class Demo
            {
                static Dictionary<int, int>.ValueCollection Field;
                static Dictionary<int, int>.KeyCollection Prop { get; set; }
                static Dictionary<int, int>.KeyCollection Ret(Dictionary<int, int> d) => d.Keys;
                static int Take(Dictionary<int, int>.ValueCollection vs) => vs.Count;
                public static int Run()
                {
                    var d = new Dictionary<int, int> { [1] = 10 };
                    var values = d.Values;
                    d.Clear();
                    foreach (var k in d.Keys) values = d.Values;
                    return values.Any() ? d.Keys.Count : new List<int>(d.Values).Count;
                }
            }
            """);

        var unsupported = diagnostics.Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("KeysValuesStored(")).ToArray();
        Assert.Equal(5, unsupported.Length);
    }

    [Fact]
    public async Task StructToReference_ReportsUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            public interface IShape { }
            public struct Sq : IShape { public int S; }
            public class Tri : IShape { }
            public class Demo
            {
                static int Area(object o) => 0;
                public static int Run()
                {
                    IShape a = new Sq { S = 2 };
                    IShape ok = new Tri();
                    object n = 5;
                    return Area(new Sq()) + Area(n) + (ok == null ? 1 : 0) + (a == null ? 1 : 0);
                }
            }
            """);

        var boxing = diagnostics.Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("StructToReference(Sq)")).ToArray();
        Assert.Equal(2, boxing.Length);
    }

    [Fact]
    public async Task SwitchOnInterface_ReportsMissingImplementations()
    {
        var diagnostics = await AnalyzeAsync("""
            public interface IShape { }
            public class Sq : IShape { public int S; }
            public class Ci : IShape { public int R; }
            public class Demo
            {
                public static int A(IShape v) => v switch { Sq s => s.S };
                public static int B(IShape v) => v switch { Sq s => s.S, Ci c => c.R };
                public static int C(IShape v) { switch (v) { case Sq s: return s.S; } return 0; }
                public static int D(IShape v) => v switch { Sq or Ci => 1 };
            }
            """);

        var missing = diagnostics.Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("SwitchNotExhaustive(")).ToArray();
        Assert.Equal(2, missing.Length);
        Assert.All(missing, d => Assert.Contains("SwitchNotExhaustive(IShape: Ci)", d.GetMessage()));
    }

    [Fact]
    public async Task InterpolationFormat_ReportsUnsupportedSpecifiers()
    {
        var diagnostics = await AnalyzeAsync("""
            public class Demo
            {
                public static string Run(int i, float f)
                {
                    return $"{f:N2} {i:P0} {f:F2} {i:D3}";
                }
            }
            """);

        var formats = diagnostics.Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax
            && d.GetMessage().Contains("FormatSpecifier(")).ToArray();
        Assert.Equal(2, formats.Length);
    }
}
