using Microsoft.CodeAnalysis;
using Xunit;

namespace TinyCs.Analyzers.Tests;

// Lua / 生成コードの予約と重なる識別子 (q.md Q16)。transpiler は build を止める
// error、analyzer は他の build を止める診断と同じく warning
public partial class TinyCsComplianceAnalyzerTests
{
    [Fact]
    public async Task ReservedIdentifiers_ReportUnsupportedSyntax()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace A.B_C { public class X { } }
            public class My_Type { }
            public class P
            {
                public int __hp;
                public int 速度;
                public void @new() { }
                public int ctor { get; set; }
                public static int F(int __a) { var __b = __a; return __b; }
                public int max_hp;
                public int _init;
            }
            """);

        var names = diagnostics
            .Where(d => d.Id == TinyCsDiagnosticIds.UnsupportedSyntax)
            .Select(d =>
            {
                Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
                var line = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                return $"{line}:{d.GetMessage()}";
            })
            .OrderBy(s => int.Parse(s.Split(':')[0]))
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "1:TinyC# does not support syntax 'UnderscoreNamespaceName(B_C)'",
            "2:TinyC# does not support syntax 'UnderscoreTypeName(My_Type)'",
            "5:TinyC# does not support syntax 'DoubleUnderscoreIdentifier(__hp)'",
            "6:TinyC# does not support syntax 'NonAsciiIdentifier(速度)'",
            "7:TinyC# does not support syntax 'ReservedClassKey(new)'",
            "8:TinyC# does not support syntax 'ReservedClassKey(ctor)'",
            "9:TinyC# does not support syntax 'DoubleUnderscoreIdentifier(__a)'",
            "9:TinyC# does not support syntax 'DoubleUnderscoreIdentifier(__b)'",
        ], names);
    }

    [Fact]
    public async Task FileScopedNamespaceWithUnderscore_Reports()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Game_Core;
            public class X { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("UnderscoreNamespaceName(Game_Core)", diagnostic.GetMessage());
    }
}
