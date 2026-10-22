namespace TinyCs.Tcs2c.Tests;

// struct を interface / object に入れる boxing は両 backend とも shared facts の同じ診断で
// IL を作る前に止まる (Lua は従来 `is` が黙って false、C は `IlIsType target is not a class` だった。q.md Q19)
public partial class DifferentialTests
{
    private const string StructBoxingProgram = """
        using System;
        public interface IShape { }
        public struct Sq : IShape { public int S; }
        public class P
        {
            public static void Main()
            {
                IShape v = new Sq { S = 3 };
                Console.WriteLine(v is Sq ? 1 : 0);
            }
        }
        """;

    [Fact]
    public void StructBoxing_BothBackendsStopWithDiagnostic()
    {
        foreach (var run in new Func<string>[]
                 {
                     () => Backends.RunLua([StructBoxingProgram], "P"),
                     () => Backends.EmitC([StructBoxingProgram], "P"),
                 })
        {
            var ex = Assert.Throws<InvalidOperationException>(run);
            Assert.Contains("(8,20): error TCS1001: unsupported syntax: StructToReference(Sq)", ex.Message);
        }
    }
}
