namespace TinyCs.Tcs2c.Tests;

// 型をまたぐ static initializer の初期化順の 2 backend differential。期待値は
// 同じ program を CoreCLR で実行した stdout
public partial class DifferentialTests
{
    // a.cs の initializer が b.cs の型の static を読む。直接の参照のほか、
    // constructor 本文 / static getter / virtual・interface 呼び出しの先で読むものも、
    // 読まれる型を先に初期化する。ファイルの並びに依存しない
    private const string InitOrderA = """
        using System;
        public class A
        {
            public static B Inst = new B(5);
            public static int K = B.Base + 1;
            public static int ViaCtor = new Maker().Made;
            public static int ViaGetter = Config.Doubled;
            public static int ViaVirtual = Pick().Value();
            public static int ViaInterface = Rate(new Fixed());
            static Shape Pick() { return new Circle(); }
            static int Rate(IRate r) { return r.Rate(); }
            public static void Main()
            {
                Console.WriteLine(Inst.V + K);
                Console.WriteLine(ViaCtor + "," + ViaGetter + "," + ViaVirtual + "," + ViaInterface);
                Console.WriteLine(CycleA.Value + "," + CycleB.Value);
            }
        }
        public class CycleA { public static int Value = CycleB.Value + 1; }
        public class CycleB { public static int Value = CycleA.Value + 2; }
        """;

    private const string InitOrderB = """
        public class B { public static int Base = 10; public int V; public B(int v) { V = v; } }
        public class Maker { public int Made; public Maker() { Made = Table.Size * 2; } }
        public class Table { public static int Size = Compute(); static int Compute() { return 21; } }
        public class Config { public static int Raw = 4 + Table.Size; public static int Doubled => Raw * 2; }
        public abstract class Shape { public abstract int Value(); }
        public class Circle : Shape { public override int Value() { return Late.N; } }
        public class Late { public static int N = Table.Size + 1; }
        public interface IRate { int Rate(); }
        public class Fixed : IRate { public int Rate() { return Spare.M; } }
        public class Spare { public static int M = Table.Size + 2; }
        """;

    [CFact]
    public void StaticInitializers_InitializeReferencedTypesFirst()
    {
        var output = Backends.AssertParity([InitOrderA, InitOrderB], "A");
        Assert.Equal("16\n42,50,22,23\n3,2", output);
    }

    [CFact]
    public void StaticInitializers_DoNotDependOnFileOrder()
    {
        var output = Backends.AssertParity([InitOrderB, InitOrderA], "A");
        Assert.Equal("16\n42,50,22,23\n3,2", output);
    }
}
