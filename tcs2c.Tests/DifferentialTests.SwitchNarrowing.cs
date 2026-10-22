namespace TinyCs.Tcs2c.Tests;

// interface 型の入力を switch 文 / 式の宣言 pattern で class に絞った変数の member を
// 両 backend で引ける (tcs2c は絞った型の無検査 cast で束縛する。q.md Q19)
public partial class DifferentialTests
{
    [CFact]
    public void Switch_NarrowedClassVariable_MemberAccess()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public interface IShape { }
            public class Sq : IShape { public int S; }
            public class Ci : IShape { public float R; }
            public class P
            {
                static int Area(IShape v)
                {
                    switch (v)
                    {
                        case Sq a: return a.S * a.S;
                        case Ci c: return (int)(c.R * 3);
                        default: return -1;
                    }
                }
                static string Name(IShape v) => v switch { Sq a => "sq" + a.S, Ci c => "ci" + (int)c.R, _ => "?" };
                public static void Main()
                {
                    var shapes = new List<IShape> { new Sq { S = 2 }, new Ci { R = 2f }, new Sq { S = 3 } };
                    var total = 0;
                    var names = "";
                    foreach (var s in shapes) { total += Area(s); names += Name(s) + ","; }
                    Console.WriteLine(total + ":" + names);
                }
            }
            """, "P");
        Assert.Equal("19:sq2,ci2,sq3,", output);
    }
    // 別々の arm / section が同じ designation 名を使っても、arm ごとに別の変数として
    // 束縛する (C# では arm ごとに別スコープ。前宣言が 1 つの型付き local に潰れていた退行)
    [CFact]
    public void Switch_SameDesignationName_InDifferentArms()
    {
        var output = Backends.AssertParity("""
            using System;
            public interface IShape { }
            public class Sq : IShape { public int S; }
            public class Ci : IShape { public int R; }
            public class P
            {
                static int Code(IShape v) => v switch { Sq x => 1, Ci x => 2, _ => 0 };
                static int Size(IShape v) => v switch { Sq x => x.S, Ci x => x.R * 10, _ => -1 };
                static int Area(IShape v)
                {
                    switch (v)
                    {
                        case Sq x: return x.S * x.S;
                        case Ci x: return x.R * 3;
                        default: return 0;
                    }
                }
                public static void Main()
                {
                    IShape a = new Sq { S = 7 };
                    IShape b = new Ci { R = 9 };
                    Console.WriteLine(Code(a) + ":" + Code(b) + ":" + Size(a) + ":" + Size(b) + ":" + Area(a) + ":" + Area(b));
                }
            }
            """, "P");
        Assert.Equal("1:2:7:90:49:27", output);
    }
}
