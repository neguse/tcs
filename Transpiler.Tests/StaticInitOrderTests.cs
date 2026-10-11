using TinyCs;

namespace TinyCs.Tests;

// static initializer の初期化順 (StaticInitOrder)。単一 chunk の Lua / C の
// 実行結果は tcs2c.Tests の DifferentialTests.StaticInitOrder.cs。module mode
// (registry) では fresh VM で全型を declare / define してから、initializer が
// 読む他の型 (他 module の method 本文の先も含む) を先に初期化する
public class StaticInitOrderTests
{
    // initializer は全型の後にまとめて出すが、その警告も結果に載る
    [Fact]
    public void DeferredInitializerWarningsAreReported()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            public class Test
            {
                public static int A = 1;
                public static int H = unchecked(A * 2);
            }
            """]);
        Assert.Contains(result.Warnings, w => w.Contains("UncheckedExpression"));
    }

    private static string Snapshot(IncrementalCompilationSession session) =>
        ModuleLinker.LinkSnapshot(session.Artifacts, session.Revision, null,
            LuaRuntime.LoadTinySystemSource(),
            LuaRuntime.LoadRuntimeFile(LuaRuntime.RegistryRelativePath));

    private static string[] RunSnapshot(string snapshot, string script)
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, snapshot);
        try
        {
            return TestHelper.RunLua(
                    $"dofile(\"{file.Replace("\\", "/")}\")\n" +
                    "local reg = _G.__tcs_module_runtime.registry\n" + script)
                .Trim().Split('\n').Select(l => l.Trim()).ToArray();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void FreshSnapshotInitializesReferencedTypesFirst()
    {
        var session = new IncrementalCompilationSession();
        session.OpenProject([
            ("a.cs", """
                public class A
                {
                    public static B Inst = new B(5);
                    public static int K = B.Base + 1;
                    public static int L = Lib.Get();
                }
                """),
            ("b.cs", """
                public class B
                {
                    public static int Base = 10;
                    public int V;
                    public B(int v) { V = v; }
                }
                """),
            ("lib.cs", "public class Lib { public static int Get() { return Z.W * 2; } }"),
            ("z.cs", "public class Z { public static int W = B.Base + 11; }"),
        ]);
        var output = RunSnapshot(Snapshot(session), """
            local A = reg.types["a.cs#A"]
            print(A.inst.v + A.k)
            print(A.l)
            """);
        Assert.Equal(["16", "42"], output);
    }

    // 依存は他 module の method 本文から来るので、fast path (body edit) で
    // その本文を変えても次の fresh VM は新しい依存の順で初期化する
    [Fact]
    public void BodyEditInAnotherModuleUpdatesInitOrder()
    {
        var lib = "public class Lib { public static int Get() { return 1; } }";
        var session = new IncrementalCompilationSession();
        session.OpenProject([
            ("a.cs", "public class A { public static int X = Lib.Get(); }"),
            ("lib.cs", lib),
            ("z.cs", "public class Z { public static int W = Seed(); static int Seed() { return 7; } }"),
        ]);
        var r = session.Update("lib.cs", lib.Replace("return 1;", "return Z.W;"));
        Assert.True(r.Success && r.FastPath);
        var output = RunSnapshot(Snapshot(session),
            "print(reg.types[\"a.cs#A\"].x)");
        Assert.Equal(["7"], output);
    }
}
