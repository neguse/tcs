namespace TinyCs.Tcs2c.Tests;

// Dictionary の Keys / Values を受け手にした List の LINQ の 2 backend 一致。
// 列挙順は未規定なので、OrderBy を通すか順序に依らない値を出す
public partial class DifferentialTests
{
    [CFact]
    public void Dict_KeysValues_ListLinq()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1, ["c"] = 3 };
                    Console.WriteLine(string.Join(",", d.Keys.OrderBy(k => k)) + ":" + string.Join(",", d.Values.OrderByDescending(v => v)));
                    Console.WriteLine(d.Values.Sum() + ":" + d.Values.Min() + ":" + d.Values.Max() + ":" + d.Keys.Min() + ":" + d.Keys.Max());
                    Console.WriteLine(d.Values.Where(v => v > 1).Count + ":" + d.Keys.Any(k => k == "c") + ":" + d.Values.All(v => v > 1) + ":" + d.Values.Count(v => v >= 2));
                    Console.WriteLine(d.Values.MinBy(v => -v) + ":" + d.Keys.MaxBy(k => k.Length) .Length + ":" + d.Values.Sum(v => v * 10) + ":" + string.Join(",", d.Keys.Select(k => k + "!").OrderBy(s => s)));
                    var ints = new Dictionary<int, float> { [3] = 0.5f, [1] = 1.5f };
                    Console.WriteLine(ints.Keys.Sum() + ":" + ints.Values.Sum() + ":" + string.Join(",", ints.Keys.OrderBy(k => k)));
                }
            }
            """, "P");
        Assert.Equal("a,b,c:3,2,1\n6:1:3:a:c\n2:true:false:2\n3:1:60:a!,b!,c!\n4:2:1,3", output);
    }
}
