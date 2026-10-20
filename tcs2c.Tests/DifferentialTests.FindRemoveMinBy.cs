namespace TinyCs.Tcs2c.Tests;

// FindIndex / RemoveAll (List<T>) と MinBy / MaxBy (TinySystem.Linq) の 2 backend 一致。
// 述語と key selector は要素ごとに 1 回・要素の順、RemoveAll は残る順を保ち、
// MinBy / MaxBy は同じ key で先の要素 (key が全部 NaN でも先頭)、NaN の key は最小、string の key は ordinal
public partial class DifferentialTests
{
    [CFact]
    public void Linq_FindIndexRemoveAllMinByMaxBy()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using TinySystem.Linq;
            public struct S { public int N; }
            public class Item { public string Name; public int Rank; public float Score; public Item(string n, int r, float s) { Name = n; Rank = r; Score = s; } }
            public class P
            {
                static int calls;
                public static void Main()
                {
                    var list = new List<int> { 3, 5, 8, 5 };
                    Console.WriteLine(list.FindIndex(x => { calls++; return x == 5; }) + ":" + list.FindIndex(x => x > 100) + ":" + calls);
                    var removed = list.RemoveAll(x => { calls++; return x % 2 == 1; });
                    Console.WriteLine(removed + ":" + list.Count + ":" + list[0] + ":" + calls);
                    var ss = new List<S> { new S { N = 1 }, new S { N = 2 }, new S { N = 3 }, new S { N = 4 } };
                    var sr = ss.RemoveAll(s => { s.N += 10; return s.N % 2 == 0; });
                    Console.WriteLine(sr + ":" + ss.Count + ":" + ss[0].N + ":" + ss[1].N);
                    var empty = new List<int>();
                    Console.WriteLine(empty.RemoveAll(x => true) + ":" + empty.FindIndex(x => true));
                    var items = new List<Item> { new Item("b", 2, 1.5f), new Item("a", 1, 0f / 0f), new Item("c", 2, 9f), new Item("d", 1, -1f) };
                    Console.WriteLine(items.MinBy(i => i.Rank).Name + items.MaxBy(i => i.Rank).Name + ":" + items.MinBy(i => i.Score).Name + items.MaxBy(i => i.Score).Name + ":" + items.MinBy(i => i.Name).Name + items.MaxBy(i => i.Name).Name);
                    var nan = 0f / 0f;
                    var allNan = new List<Item> { new Item("x", 0, nan), new Item("y", 0, nan) };
                    Console.WriteLine(allNan.MinBy(i => i.Score).Name + allNan.MaxBy(i => i.Score).Name);
                    var ints = new List<int> { 4, -2, 7, 7 };
                    Console.WriteLine(ints.MinBy(x => -x) + ":" + ints.MaxBy(x => x % 3));
                    var structs = new List<S> { new S { N = 5 }, new S { N = 2 } };
                    var smallest = structs.MinBy(s => s.N);
                    smallest.N = 100;
                    Console.WriteLine(smallest.N + ":" + structs[1].N);
                }
            }
            """, "P");
        Assert.Equal("1:-1:2\n3:1:8:6\n2:2:1:3\n0:-1\nab:ac:ad\nxx\n7:4\n100:2", output);
    }
}
