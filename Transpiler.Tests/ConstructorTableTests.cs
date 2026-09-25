namespace TinyCs.Tests;

// issue #10: instance を空 table への逐次代入ではなく、field を並べた table
// constructor で一度に作る。本文先頭の純粋な代入は table へ畳み込み、直後に
// 上書きされる既定値は出さない。副作用のある initializer の評価順
// (宣言順 → 本文) と、self を読む本文の意味は変えない。
public class ConstructorTableTests
{
    private const string LogSource = """
        public static class Log
        {
            public static string S = "";

            public static int Mark(string m, int v)
            {
                S = S + m;
                return v;
            }
        }
        """;

    [Fact]
    public void SimpleCtor_BuildsTableOnce()
    {
        var lua = Transpiler.Transpile("""
            public class V
            {
                public float X;
                public float Y;
                public float Z;

                public V(float x, float y, float z)
                {
                    X = x;
                    Y = y;
                    Z = z;
                }
            }
            """);

        Assert.Contains("setmetatable({x = x, y = y, z = z}, V)", lua);
        Assert.DoesNotContain("self.x =", lua);
        Assert.Equal("6", TestHelper.RunLua(lua +
            "\nlocal v = V.new(1, 2, 3)\nprint(v.x + v.y + v.z)").Trim());
    }

    [Fact]
    public void SideEffectingInitializers_KeepDeclarationThenBodyOrder()
    {
        var output = TestHelper.TranspileAndRun(LogSource + """
            public class A
            {
                public int X = Log.Mark("x", 1);
                public int Y = 2;
                public int Z = Log.Mark("z", 3);

                public A(int y)
                {
                    Y = y;
                    Log.Mark("b", 0);
                }
            }

            public static class T
            {
                public static string Test()
                {
                    var a = new A(5);
                    return $"{Log.S}:{a.X}{a.Y}{a.Z}";
                }
            }
            """, "T.Test()");

        Assert.Equal("xzb:153", output);
    }

    [Fact]
    public void BodyOverwritingImpureInitializer_StillRunsInitializer()
    {
        var output = TestHelper.TranspileAndRun(LogSource + """
            public class A
            {
                public int X = Log.Mark("x", 1);

                public A(int x)
                {
                    X = x;
                }
            }

            public static class T
            {
                public static string Test()
                {
                    var a = new A(7);
                    return $"{Log.S}:{a.X}";
                }
            }
            """, "T.Test()");

        Assert.Equal("x:7", output);
    }

    [Fact]
    public void BodyReadingSelf_IsNotHoisted()
    {
        var output = TestHelper.TranspileAndRun("""
            public class A
            {
                public int X;
                public int Y = 10;
                public int Z;

                public A(int x)
                {
                    X = x;
                    Z = X + Y;
                    Y = 1;
                }
            }

            public static class T
            {
                public static string Test()
                {
                    var a = new A(3);
                    return $"{a.X},{a.Y},{a.Z}";
                }
            }
            """, "T.Test()");

        Assert.Equal("3,1,13", output);
    }

    [Fact]
    public void ReferenceFieldsAndCollections_DefaultCorrectly()
    {
        var output = TestHelper.TranspileAndRun("""
            using System.Collections.Generic;

            public class Bag
            {
                public string? Name;
                public List<int> Items = new List<int>();
                public int Count;

                public Bag(string name)
                {
                    Name = name;
                }
            }

            public static class T
            {
                public static string Test()
                {
                    var b = new Bag("sack");
                    var other = new Bag("box");
                    b.Items.Add(4);
                    return $"{b.Name}:{b.Items.Count}:{other.Items.Count}:{b.Count}";
                }
            }
            """, "T.Test()");

        Assert.Equal("sack:1:0:0", output);
    }

    [Fact]
    public void DerivedCtor_FoldsIntoAssignmentsAfterBase()
    {
        var output = TestHelper.TranspileAndRun("""
            public class Shape
            {
                public int Id;

                public Shape(int id)
                {
                    Id = id;
                }
            }

            public class Circle : Shape
            {
                public int R = 1;
                public string? Label;

                public Circle(int id, int r) : base(id)
                {
                    R = r;
                }

                public int Area() => R * R * 3 + Id;
            }

            public static class T
            {
                public static int Test() => new Circle(2, 4).Area();
            }
            """, "T.Test()");

        Assert.Equal("50", output);
    }

    [Fact]
    public void ExpressionBodiedAndOptionalParameterCtors()
    {
        var output = TestHelper.TranspileAndRun("""
            public class P
            {
                public int X;
                public int Y = 9;

                public P(int x, int y = 4)
                {
                    X = x;
                    Y = y;
                }
            }

            public class Q
            {
                public int V;

                public Q(int v) => V = v;
            }

            public static class T
            {
                public static int Test() => new P(1).X * 100 + new P(1).Y * 10 + new Q(3).V;
            }
            """, "T.Test()");

        Assert.Equal("143", output);
    }

    [Fact]
    public void StructCtor_InitializersAndBodyFold()
    {
        var output = TestHelper.TranspileAndRun(LogSource + """
            public struct S
            {
                public int A = Log.Mark("a", 1);
                public int B;
                public int C = 5;

                public S(int b)
                {
                    B = b;
                    Log.Mark("b", 0);
                }
            }

            public static class T
            {
                public static string Test()
                {
                    var s = new S(7);
                    var z = new S();
                    var copy = s;
                    copy.A = 100;
                    return $"{Log.S}:{s.A},{s.B},{s.C}:{z.A},{z.C}:{copy.A}";
                }
            }
            """, "T.Test()");

        Assert.Equal("ab:1,7,5:0,0:100", output);
    }

    [Fact]
    public void StructCopy_NestedStructStaysDeep()
    {
        var output = TestHelper.TranspileAndRun("""
            public struct Inner
            {
                public int V;
            }

            public struct Outer
            {
                public Inner In;
                public int W;
            }

            public static class T
            {
                public static string Test()
                {
                    var a = new Outer();
                    a.In.V = 1;
                    var b = a;
                    b.In.V = 2;
                    b.W = 3;
                    return $"{a.In.V},{a.W},{b.In.V},{b.W}";
                }
            }
            """, "T.Test()");

        Assert.Equal("1,0,2,3", output);
    }

    [Fact]
    public void RecordStructPositionalCtor_WithInitializer()
    {
        var output = TestHelper.TranspileAndRun("""
            public record struct R(int A, int B)
            {
                public int Sum = A + B;
            }

            public static class T
            {
                public static string Test()
                {
                    var r = new R(2, 3);
                    return $"{r.A},{r.B},{r.Sum}";
                }
            }
            """, "T.Test()");

        Assert.Equal("2,3,5", output);
    }
}
