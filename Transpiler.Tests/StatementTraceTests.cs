using TinyCs;

namespace TinyCs.Tests;

// --trace: 実行した C# 行を global の __tcs_trace に記録し、関数ごとの local
// __tcs_at で現在行を持つ (host が debug.getlocal で C# の呼び出し元を組み立てる)
public class StatementTraceTests
{
    private const string GamePath = "samples/game/Game.cs";
    private static readonly string[] GamePrefix = ["samples/game/"];

    // __tcs_trace の key を並べて 1 行にする
    private const string PrintTraceKeys = """
        local keys = {}
        for k in pairs(__tcs_trace) do keys[#keys + 1] = k end
        table.sort(keys)
        print(table.concat(keys, ","))
        """;

    // debug.getlocal で各 frame の __tcs_at を集める host 関数 (呼び出し元が先)。
    // --ref の型は host の table で、Lua 名は host API の流儀 (host.stack)
    private const string HostStub = """
        host = {}
        function host.stack()
          local out = {}
          local level = 2
          while debug.getinfo(level, "f") do
            local i = 1
            while true do
              local name, value = debug.getlocal(level, i)
              if name == nil then break end
              if name == "__tcs_at" then out[#out + 1] = tostring(value) end
              i = i + 1
            end
            level = level + 1
          end
          return table.concat(out, " < ")
        end
        """;

    private static string Transpile(string[] sources, string[] paths,
        string[]? refs = null, bool instanceRegistry = false)
    {
        var result = Transpiler.TranspileWithDiagnostics(sources, paths,
            referenceSources: refs, instanceRegistry: instanceRegistry,
            tracePrefixes: GamePrefix);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Lua;
    }

    [Fact]
    public void Trace_RecordsExecutedLinesOfTracedFileOnly()
    {
        const string game = """
            public class Game
            {
                public static int Run(int v)
                {
                    int r = Lib.Twice(v);
                    if (v > 100)
                    {
                        r = r + 1;
                    }
                    else
                    {
                        r = r - 1;
                    }
                    return r;
                }
            }
            """;
        const string lib = """
            public class Lib
            {
                public static int Twice(int x)
                {
                    return x * 2;
                }
            }
            """;
        var lua = Transpile([game, lib], [GamePath, "cs-lib/lubx/Lib.cs"]);
        var output = TestHelper.RunLua($"""
            {lua}
            print(Game.Run(3))
            {PrintTraceKeys}
            print(_G.__tcs_at)
            """).Trim().Split('\n');
        Assert.Equal("5", output[0]);
        // 実行した行だけ。取らなかった if の枝 (8 行目) と Lib.cs は入らない
        Assert.Equal(
            $"{GamePath}:12,{GamePath}:14,{GamePath}:5,{GamePath}:6",
            output[1]);
        // __tcs_at は関数の local で、global には漏れない
        Assert.Equal("nil", output[2]);
    }

    // `return f(x)` は Lua の tail call で、その frame は stack に残らない。
    // 呼び出し元の行を残したい箇所では値を local に受けてから return する
    [Fact]
    public void Trace_CallerLinesAreReadableFromEachFrame()
    {
        const string game = """
            using System;
            public class Game
            {
                public static string Outer()
                {
                    string s = Inner(1);
                    return s;
                }
                public static string Inner(int v)
                {
                    string s = Host.Stack();
                    return s;
                }
                public static string ViaLambda()
                {
                    Func<int, string> f = x =>
                    {
                        string s = Host.Stack();
                        return s;
                    };
                    string r = f(1);
                    return r;
                }
                public static string TailReturn()
                {
                    return Inner(1);
                }
            }
            """;
        const string host = """
            public static class Host { public static string Stack() { return ""; } }
            """;
        var lua = Transpile([game], [GamePath], refs: [host]);
        var output = TestHelper.RunLua($"""
            {HostStub}
            {lua}
            print(Game.Outer())
            print(Game.ViaLambda())
            print(Game.TailReturn())
            """).Trim().Split('\n');
        Assert.Equal($"{GamePath}:11 < {GamePath}:6", output[0]);
        Assert.Equal($"{GamePath}:18 < {GamePath}:21", output[1]);
        // tail call した TailReturn の frame は無い
        Assert.Equal($"{GamePath}:11", output[2]);
    }

    [Fact]
    public void Trace_ReloadChunkTracesChangedBodyWithRealPath()
    {
        const string v1 = """
            public class Game
            {
                public int Score()
                {
                    return 1;
                }
            }
            """;
        const string v2 = """
            public class Game
            {
                public int Score()
                {
                    int s = 2;
                    return s;
                }
            }
            """;
        var v1Lua = Transpile([v1], [GamePath], instanceRegistry: true);
        var plan = HotReload.PlanReload([v1], [v2], v2FilePaths: [GamePath],
            tracePrefixes: GamePrefix);
        Assert.NotNull(plan.Chunk);
        var output = TestHelper.RunLua($"""
            {TestHelper.LoadChunk(v1Lua, "v1")}
            local g = Game.new()
            g:Score()
            local trace = __tcs_trace
            assert(trace["{GamePath}:5"], "v1 traced")
            for k in pairs(trace) do trace[k] = nil end
            {TestHelper.LoadChunk(plan.Chunk, "reload")}
            assert(__tcs_trace == trace, "reload chunk keeps the table")
            print(g:Score())
            {PrintTraceKeys}
            """).Trim().Split('\n');
        Assert.Equal("2", output[0]);
        Assert.Equal($"{GamePath}:5,{GamePath}:6", output[1]);
    }

    [Fact]
    public void Trace_PrefixIsMatchedAgainstNormalizedPath()
    {
        const string game = """
            public class Game
            {
                public static int Run() { return 1; }
            }
            """;
        var result = Transpiler.TranspileWithDiagnostics([game],
            [@"samples\game\Game.cs"], tracePrefixes: GamePrefix);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        var output = TestHelper.RunLua($"""
            {result.Lua}
            Game.Run()
            {PrintTraceKeys}
            """).Trim();
        Assert.Equal($"{GamePath}:3", output);
    }

    [Fact]
    public void NoTrace_OutputHasNoTraceCode()
    {
        const string game = """
            public class Game
            {
                public static int Run(int v)
                {
                    if (v > 0) { return v; }
                    return -v;
                }
            }
            """;
        var plain = Transpiler.Transpile(game);
        Assert.DoesNotContain("__tcs_trace", plain);
        Assert.DoesNotContain("__tcs_at", plain);
        // path を与えても prefix が無ければ同じ出力
        var withPath = Transpiler.TranspileWithDiagnostics([game], [GamePath]);
        Assert.Equal(plain, withPath.Lua);
    }

    [Fact]
    public void Cli_Trace_RecordsLinesUnderGivenPrefix()
    {
        var dir = Directory.CreateTempSubdirectory("tcs-trace-cli");
        try
        {
            var game = Path.Combine(dir.FullName, "Game.cs");
            File.WriteAllText(game, """
                public class Game
                {
                    public static int Run() { return 7; }
                }
                """);
            var outPath = Path.Combine(dir.FullName, "out.lua");
            var r = ConsoleCapture.Run(() => Program.Main(
                [game, "-o", outPath, "--trace", dir.FullName + Path.DirectorySeparatorChar]));
            Assert.Equal(0, r.ExitCode);
            var output = TestHelper.RunLua($"""
                dofile("{outPath.Replace("\\", "/")}")
                Game.Run()
                {PrintTraceKeys}
                """).Trim();
            Assert.Equal($"{game.Replace("\\", "/")}:3", output);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Cli_Trace_RejectsSnapshot()
    {
        var r = ConsoleCapture.Run(() => Program.Main(
            ["a.cs", "--snapshot", "--entry", "A", "--trace", "a/"]));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("--trace", r.Stderr);
    }

    [Fact]
    public void Cli_Trace_RequiresValue()
    {
        var r = ConsoleCapture.Run(() => Program.Main(["a.cs", "--trace"]));
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("missing value for --trace", r.Stderr);
    }
}
