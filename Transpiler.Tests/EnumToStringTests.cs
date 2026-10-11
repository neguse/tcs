namespace TinyCs.Tests;

// enum の文字列化 (#133)。名前の表は文字列化する地点にだけ出し、--ref の host
// enum も stub の member 名を出す。.NET との一致は tcs2c.Tests の
// DifferentialTests.EnumToString
public class EnumToStringTests
{
    [Fact]
    public void NameTable_IsEmittedOnlyWhereStringified()
    {
        var lua = Transpiler.Transpile("""
            public enum Color { Red, Green }
            public enum Dir { Up, Down }
            public static class P
            {
                public static int Turn(Dir d) => d == Dir.Up ? 1 : 0;
                public static string Test() => "c=" + Color.Green;
            }
            """);

        Assert.Contains("\"Red:0,Green:1\"", lua);
        Assert.DoesNotContain("Up:", lua);
    }

    [Fact]
    public void ReferenceEnum_UsesStubMemberNames()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            public static class P
            {
                public static string Test() => Host.Mode.On + "|" + (Host.Mode)5;
            }
            """], referenceSources: ["""
            namespace Host { public enum Mode { Off = 0, On = 3 } }
            """]);
        Assert.True(result.Success, string.Join("\n", result.Errors));

        var runtimePath = TestHelper.FindProjectFile("runtime/tinysystem.lua");
        Assert.Equal("On|5", TestHelper.RunLua(
            $"local TinySystem = dofile(\"{runtimePath}\")\nString = TinySystem.String\n"
            + "host = { OFF = 0, ON = 3 }\n"
            + $"{result.Lua}\nprint(P.test())").Trim());
    }
}
