using System.Diagnostics;
using TinyCs;
using TinyCs.Tcs2c;

namespace TinyCs.Tcs2c.Tests;

/// <summary>
/// 2 backend の実行ハーネス。同じ TinyC# source を tcs2c→C→gcc と
/// tcs→Lua→lua32 で実行し、stdout を突き合わせる。C 側は GC stress
/// (-DTCS_GC_STRESS=1: 確保ごとに full GC) でも同じ出力であることを
/// 要求する — 保守的 stack 走査 / 精密 heap trace の root 漏れは、
/// 生きている object が回収されて出力が変わる形で現れる。
/// </summary>
internal static class Backends
{
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);
    public static readonly string ProjectRoot = FindProjectRoot();
    public static readonly string? CCompiler = FindCCompiler();
    public static readonly string LuaPath = Path.Combine(ProjectRoot, "deps", "lua",
        OperatingSystem.IsWindows() ? "lua32.exe" : "lua32");

    public static readonly string[] StrictFlags =
        ["-O2", "-ffp-contract=off", "-fwrapv", "-fexcess-precision=standard"];

    private static string FindProjectRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "tcs.slnx"))) return dir;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return Environment.CurrentDirectory;
    }

    private static string? FindCCompiler()
    {
        var configured = Environment.GetEnvironmentVariable("CC");
        foreach (var candidate in new[] { configured, "gcc", "cc", "clang" })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                var psi = Start(candidate, ["--version"]);
                var result = Run(psi, TimeSpan.FromSeconds(10));
                if (result.ExitCode == 0) return candidate;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                or InvalidOperationException)
            {
            }
        }
        return null;
    }

    public static string Sample(string relative) =>
        File.ReadAllText(Path.Combine(ProjectRoot, relative));

    /// <summary>C source を生成する (emit だけ、compile なし)。</summary>
    public static string EmitC(string[] sources, string? entryClass,
        bool lib = false, bool digestF32 = false)
    {
        var exported = IlExport.Export(sources);
        if (exported.Diagnostics.Length > 0)
            throw new InvalidOperationException("TinyC# diagnostics:\n" +
                string.Join("\n", exported.Diagnostics));
        return new CEmitter(exported, digestF32).Emit(entryClass, lib);
    }

    /// <summary>tcs2c → C compiler → 実行。stress は GC を確保ごとに回す。</summary>
    public static string RunC(string[] sources, string? entryClass,
        bool stress = false, IEnumerable<string>? extraFlags = null)
    {
        var c = EmitC(sources, entryClass);
        return CompileAndRunC(c, stress, extraFlags);
    }

    public static string CompileAndRunC(string cSource, bool stress = false,
        IEnumerable<string>? extraFlags = null, string? extraCSource = null)
    {
        var compiler = CCompiler
            ?? throw new InvalidOperationException("no C compiler available");
        var dir = Directory.CreateTempSubdirectory("tcs2c-test-");
        try
        {
            var cPath = Path.Combine(dir.FullName, "program.c");
            File.WriteAllText(cPath, cSource);
            var exe = Path.Combine(dir.FullName,
                OperatingSystem.IsWindows() ? "program.exe" : "program");
            var args = new List<string>(StrictFlags);
            if (stress) args.Add("-DTCS_GC_STRESS=1");
            if (extraFlags != null) args.AddRange(extraFlags);
            args.Add(cPath);
            if (extraCSource != null)
            {
                var hostPath = Path.Combine(dir.FullName, "host.c");
                File.WriteAllText(hostPath, extraCSource);
                args.Add(hostPath);
            }
            args.AddRange(["-o", exe, "-lm"]);
            var compile = Run(Start(compiler, args), CompileTimeout);
            if (compile.ExitCode != 0)
                throw new InvalidOperationException(
                    $"C compile failed:\n{compile.Stderr}\n--- C ---\n{Number(cSource)}");
            var psi = Start(exe, []);
            psi.Environment["ASAN_OPTIONS"] = "detect_stack_use_after_return=0";
            var run = Run(psi, RunTimeout);
            if (run.ExitCode != 0)
                throw new InvalidOperationException(
                    $"C program exited with {run.ExitCode}:\n{run.Stderr}\n" +
                    $"stdout:\n{run.Stdout}");
            return run.Stdout.Replace("\r\n", "\n").TrimEnd('\n');
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>tcs → Lua → lua32 (LUA_32BITS)。entryClass.Main() を呼ぶ。</summary>
    public static string RunLua(string[] sources, string entryClass)
    {
        var lua = Transpiler.Transpile(sources);
        // TranspileAndRunWithRuntime と同じ runtime 束縛 (List / Dict / ... は
        // runtime/tinysystem.lua の table)
        var runtimePath = Path.Combine(ProjectRoot, "runtime", "tinysystem.lua")
            .Replace("\\", "/");
        var script = $"local TinySystem = dofile(\"{runtimePath}\")\n" +
            "List = TinySystem.List\nDict = TinySystem.Dict\nMath = TinySystem.Math\n" +
            "String = TinySystem.String\nRandom = TinySystem.Random\nChar = TinySystem.Char\n" +
            $"{lua}\n{entryClass}.{LuaNaming.Member("Main")}()\n";
        var dir = Directory.CreateTempSubdirectory("tcs2c-lua-");
        try
        {
            var path = Path.Combine(dir.FullName, "program.lua");
            File.WriteAllText(path, script);
            var run = Run(Start(LuaPath, [path]), RunTimeout);
            if (run.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Lua exited with {run.ExitCode}:\n{run.Stderr}\n--- lua ---\n{script}");
            return run.Stdout.Replace("\r\n", "\n").TrimEnd('\n');
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>C (通常 + GC stress) と Lua の stdout 一致を要求する。</summary>
    public static string AssertParity(string[] sources, string entryClass)
    {
        var lua = RunLua(sources, entryClass);
        var c = RunC(sources, entryClass);
        Assert.Equal(lua, c);
        var stressed = RunC(sources, entryClass, stress: true);
        Assert.Equal(lua, stressed);
        return lua;
    }

    public static string AssertParity(string source, string entryClass) =>
        AssertParity([source], entryClass);

    private static string Number(string text) => string.Join("\n",
        text.Split('\n').Select((l, i) => $"{i + 1,5}: {l}"));

    private static ProcessStartInfo Start(string file, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(
        ProcessStartInfo psi, TimeSpan timeout)
    {
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"cannot start {psi.FileName}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{psi.FileName} timed out after {timeout}");
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(),
            stderr.GetAwaiter().GetResult());
    }
}

/// <summary>C compiler が無い環境では skip する Fact。</summary>
public sealed class CFactAttribute : FactAttribute
{
    public CFactAttribute()
    {
        if (Backends.CCompiler is null) Skip = "no C compiler (gcc/cc/clang) on PATH";
    }
}

public sealed class CTheoryAttribute : TheoryAttribute
{
    public CTheoryAttribute()
    {
        if (Backends.CCompiler is null) Skip = "no C compiler (gcc/cc/clang) on PATH";
    }
}
