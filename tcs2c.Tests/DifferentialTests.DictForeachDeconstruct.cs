namespace TinyCs.Tcs2c.Tests;

// `foreach (var (k, v) in dict)` の 2 backend 一致。列挙順は未規定なので順序に依らない値を出す。
// closure による v の捕捉、struct の値の copy、break / continue
public partial class DifferentialTests
{
    [CFact]
    public void Dict_ForeachDeconstruct()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public struct S { public int N; public void Bump() { N += 1; } }
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
                    var sum = 0; var keys = 0;
                    foreach (var (k, v) in d) { if (k == "b") continue; sum += v; keys += k.Length; }
                    Console.WriteLine(sum + ":" + keys);
                    var fns = new List<Func<int>>();
                    foreach (var (k, v) in d) { fns.Add(() => v * 10 + k.Length); }
                    var total = 0;
                    foreach (var f in fns) total += f();
                    Console.WriteLine(total);
                    var ss = new Dictionary<int, S> { [1] = new S { N = 10 }, [2] = new S { N = 20 } };
                    var bumped = 0;
                    foreach (var (k, v) in ss) { v.Bump(); bumped += k; }
                    Console.WriteLine(bumped + ":" + ss[1].N + ":" + ss[2].N);
                    var seen = 0;
                    foreach (var (k, v) in d) { seen++; if (seen == 2) break; }
                    Console.WriteLine(seen);
                    var ints = new Dictionary<int, int> { [7] = 70 };
                    foreach (var (k, v) in ints) Console.WriteLine(k + "=" + v);
                }
            }
            """, "P");
        Assert.Equal("4:2\n63\n3:10:20\n2\n7=70", output);
    }
}
