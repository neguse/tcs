namespace TinyCs.Tcs2c.Tests;

// callback (述語 / selector / key selector) が受け手を変える LINQ の 2 backend 差分
// (q.md Q18: 走査は呼び出し時点の Count まで、要素は callback の前に読む)
public partial class DifferentialTests
{
    [CFact]
    public void Linq_Callbacks_IgnoreElementsAddedByCallback()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                public static void Main()
                {
                    var xs = new List<int> { 1, 2 };
                    Console.WriteLine(xs.Max(x => { if (x == 1) xs.Add(3); return x; }));
                    xs = new List<int> { 1, 2 };
                    Console.WriteLine(xs.Sum(x => { if (x == 1) xs.Add(3); return x; }));
                    xs = new List<int> { 1, 2 };
                    Console.WriteLine(xs.Where(x => { if (x == 1) xs.Add(3); return x > 0; }).Count);
                    xs = new List<int> { 1, 2 };
                    Console.WriteLine(xs.FirstOrDefault(x => { if (x == 1) xs.Add(3); return x == 3; }));
                    xs = new List<int> { 2, 1 };
                    Console.WriteLine(xs.OrderBy(x => { if (x == 2) xs.Add(0); return x; }).Count);
                }
            }
            """, "P");
        Assert.Equal("2\n3\n2\n0\n2", output);
    }

    [CFact]
    public void Linq_Callbacks_ReadElementsBeforeCallback()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                public static void Main()
                {
                    var xs = new List<int> { 1 };
                    Console.WriteLine(xs.FirstOrDefault(x => { xs[0] = 9; return true; }));
                    xs = new List<int> { 1 };
                    Console.WriteLine(xs.Where(x => { xs[0] = 9; return true; })[0]);
                    xs = new List<int> { 2, 1 };
                    var c = xs.OrderBy(x => { xs[0] = 9; xs[1] = 8; return x; });
                    Console.WriteLine(c[0] + "," + c[1]);
                }
            }
            """, "P");
        Assert.Equal("1\n1\n1,2", output);
    }
}
