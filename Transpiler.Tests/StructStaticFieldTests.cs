namespace TinyCs.Tests;

// struct / record struct の static field はサブセット外。Lua は読むと nil、
// C は止まるので、transpiler は Lua を出さない error、check は exit 1 にする (#154)。
// const は使用箇所で値に畳まれるので対象外
[Collection(ConsoleCollection.Name)]
public class StructStaticFieldTests
{
    private const string Source = """
        public struct S { public int V; public static int X = 1; }
        public record struct RS(int a) { public static readonly int Y = 2; }
        public static class P
        {
            public static int Test() => S.X + RS.Y;
        }
        """;

    [Fact]
    public void Transpile_StructStaticFieldIsBuildBlockingError()
    {
        var result = Transpiler.TranspileWithDiagnostics([Source]);

        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Contains(
            "error TCS1001: unsupported syntax: StructStaticField", e));
        Assert.StartsWith("(1,33)", result.Errors[0]);
        Assert.StartsWith("(2,34)", result.Errors[1]);
        Assert.True(TinyCsComplianceFacts.IsBuildBlocking("StructStaticField"));
    }

    [Fact]
    public void Check_StructStaticFieldExits1()
    {
        var dir = Directory.CreateTempSubdirectory("tcs_struct_static_");
        try
        {
            var input = Path.Combine(dir.FullName, "input.cs");
            File.WriteAllText(input, Source);
            var (exitCode, _, stderr) =
                ConsoleCapture.Run(() => Program.Main(["check", input]));
            Assert.Equal(1, exitCode);
            Assert.Contains("input.cs(1,33): error TCS1001: unsupported syntax: StructStaticField",
                stderr);
            Assert.Contains("input.cs(2,34): error TCS1001: unsupported syntax: StructStaticField",
                stderr);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void StructConstIsNotDiagnosed()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            public struct S { public int V; public const int K = 5; }
            public record struct RS(int a) { public const int K = 3; }
            public static class P
            {
                public static int Test() => S.K + RS.K;
            }
            """]);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }
}
