namespace TinyCs.Tests;

// issue #9: hot reload 用の weak instance registry (__tcs_instances) は
// 生成ごとの ephemeron 挿入と GC 走査のコストがあり、読むのは reload chunk
// だけ。既定では出さず、開発時の opt-in (--hot-reload / instanceRegistry)
// でだけ出す。
public class InstanceRegistryTests
{
    private const string Source = """
        public class Player
        {
            public int Hp = 10;
        }

        public record Tag(string Name);

        public static class T
        {
            public static int Test() => new Player().Hp + new Tag("a").Name.Length;
        }
        """;

    [Fact]
    public void DefaultOutput_HasNoInstanceRegistry()
    {
        var lua = Transpiler.Transpile([Source]);

        Assert.DoesNotContain("__tcs_instances", lua);
        Assert.Equal("11", TestHelper.RunLua(lua + "\nprint(T.test())").Trim());
    }

    [Fact]
    public void OptIn_RegistersClassAndRecordInstances()
    {
        var lua = Transpiler.Transpile([Source], instanceRegistry: true);

        var probe = """
            local p = Player.new()
            local t = Tag.new("x")
            print(__tcs_instances[p] == Player and __tcs_instances[t] == Tag)
            """;
        Assert.Equal("true", TestHelper.RunLua(lua + "\n" + probe).Trim());
    }

    [Fact]
    public void Cli_HotReloadFlag_EmitsInstanceRegistry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tcs-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "input.cs");
            File.WriteAllText(input, Source);
            var plain = Path.Combine(dir, "plain.lua");
            var dev = Path.Combine(dir, "dev.lua");

            var plainRun = ConsoleCapture.Run(() => Program.Main(
                [input, "-o", plain, "--no-runtime"]));
            var devRun = ConsoleCapture.Run(() => Program.Main(
                [input, "-o", dev, "--no-runtime", "--hot-reload"]));

            Assert.Equal(0, plainRun.ExitCode);
            Assert.Equal(0, devRun.ExitCode);
            Assert.DoesNotContain("__tcs_instances", File.ReadAllText(plain));
            Assert.Contains("__tcs_instances[self] = Player", File.ReadAllText(dev));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reload_RegistersInstancesCreatedAfterReload()
    {
        // v2 (reload chunk 内の定義) で生成した instance も次の reload で
        // 移行対象になる
        const string V1 = "public class Box { public int A = 1; }";
        const string V2 = "public class Box { public int A = 1; public int B = 2; }";
        const string V3 = "public class Box { public int A = 1; public int B = 2; public int C = 3; }";

        var script = $"{Transpiler.Transpile([V1], instanceRegistry: true)}\n" +
            $"{HotReload.EmitReloadChunk([V1], [V2])}\n" +
            "local late = Box.new()\n" +
            $"{HotReload.EmitReloadChunk([V2], [V3])}\n" +
            "print(late.c)";

        Assert.Equal("3", TestHelper.RunLua(script).Trim());
    }
}
