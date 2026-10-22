namespace TinyCs.Tcs2c.Tests;

// copy constructor と collection initializer の併用の 2 backend differential。
// 期待値は同じ program を CoreCLR で実行した stdout
public partial class DifferentialTests
{
    [CFact]
    public void ListCopyConstructor_WithInitializer_KeepsSourceElements()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public class P
            {
                public static void Main()
                {
                    var xs = new List<int> { 1, 2 };
                    var ys = new List<int>(xs) { 3 };
                    Console.WriteLine(ys.Count);
                    foreach (var y in ys) Console.WriteLine(y);
                    Console.WriteLine(xs.Count);
                }
            }
            """, "P");
        Assert.Equal("3\n1\n2\n3\n2", output);
    }
}
