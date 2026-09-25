namespace TinyCs.Tests;

// issue #11: struct の static member・算術 operator・ref / in parameter。
// Vector3 / Quaternion のような数学型を値型のまま書けるようにする。
// struct は metatable を持たないので、operator も static member も呼び出し
// サイトが静的型から `S.__add(a, b)` / `S.M(...)` を直接呼ぶ。
public class StructMemberTests
{
    private const string Vec3Source = """
        public struct Vec3
        {
            public float X;
            public float Y;
            public float Z;

            public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
            public static int Created;
            public const float Unit = 1f;

            public Vec3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
                Created++;
            }

            public static Vec3 Up => new Vec3(0f, Unit, 0f);

            public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
            public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
            public static Vec3 operator *(float s, Vec3 a) => a * s;

            public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            public string Show() => $"({X},{Y},{Z})";
        }
        """;

    [Fact]
    public void StaticMembersAndOperators_ComputeWithValueSemantics()
    {
        var output = TestHelper.TranspileAndRun(Vec3Source + """
            public static class T
            {
                public static string Test()
                {
                    var p = Vec3.Zero;
                    p = p + Vec3.Up * 2f;
                    p += new Vec3(1f, 0f, 1f);
                    var q = -p;
                    var r = 3f * q - p;
                    p.X = 10f;
                    return $"{p.Show()}|{q.Show()}|{r.Show()}|{Vec3.Zero.Show()}|{Vec3.Dot(p, q)}";
                }
            }
            """, "T.Test()");

        Assert.Equal("(10,2,1)|(-1,-2,-1)|(-4,-8,-4)|(0,0,0)|-15", output);
    }

    [Fact]
    public void StaticFieldCounters_AndStaticInitializerCallingOwnCtor()
    {
        var output = TestHelper.TranspileAndRun(Vec3Source + """
            public static class T
            {
                public static int Test()
                {
                    var before = Vec3.Created;
                    var a = new Vec3(1f, 2f, 3f) + new Vec3(1f, 1f, 1f);
                    return (Vec3.Created - before) * 100 + (int)a.Z;
                }
            }
            """, "T.Test()");

        // 2 回の生成 + operator + の結果 1 回
        Assert.Equal("304", output);
    }

    [Fact]
    public void OperatorResult_IsNotCopiedTwice()
    {
        var lua = Transpiler.Transpile(Vec3Source + """
            public static class T
            {
                public static float Test(Vec3 a, Vec3 b)
                {
                    var c = a + b;
                    return c.X;
                }
            }
            """);

        Assert.Contains("local c = Vec3.__add(a, b)", lua);
    }

    [Fact]
    public void ReadonlyStructAndRecordStruct_Operators()
    {
        var output = TestHelper.TranspileAndRun("""
            public readonly struct Quat
            {
                public readonly float W;
                public readonly float V;

                public Quat(float w, float v)
                {
                    W = w;
                    V = v;
                }

                public static Quat Identity => new Quat(1f, 0f);

                public static Quat operator *(Quat a, Quat b) =>
                    new Quat(a.W * b.W - a.V * b.V, a.W * b.V + a.V * b.W);
            }

            public record struct Money(int Cents)
            {
                public static Money operator +(Money a, Money b) => new Money(a.Cents + b.Cents);
                public static Money operator %(Money a, Money b) => new Money(a.Cents % b.Cents);
            }

            public static class T
            {
                public static string Test()
                {
                    var q = Quat.Identity * new Quat(0f, 1f);
                    var q2 = q * q;
                    var m = new Money(250) + new Money(75);
                    var rest = m % new Money(100);
                    return $"{q.W},{q.V}|{q2.W},{q2.V}|{m.Cents}|{rest.Cents}|{(m == new Money(325) ? 1 : 0)}";
                }
            }
            """, "T.Test()");

        Assert.Equal("0,1|-1,0|325|25|1", output);
    }

    private const string CounterSource = """
        public struct Counter
        {
            public int N;

            public void Bump()
            {
                N++;
            }

            public void Reset()
            {
                this = new Counter();
            }
        }

        public class Holder
        {
            public Counter C;
        }
        """;

    [Fact]
    public void RefParameter_FieldWritesAndWholeAssignReachCaller()
    {
        var output = TestHelper.TranspileAndRun(CounterSource + """
            public static class T
            {
                private static void Add(ref Counter c, int k)
                {
                    c.N += k;
                    c.Bump();
                }

                private static void Replace(ref Counter c, int n)
                {
                    var fresh = new Counter();
                    fresh.N = n;
                    c = fresh;
                    fresh.N = -1;
                }

                private static void Forward(ref Counter c) => Add(ref c, 100);

                public static string Test()
                {
                    var local = new Counter();
                    Add(ref local, 5);
                    var arr = new Counter[2];
                    Add(ref arr[1], 2);
                    var h = new Holder();
                    Replace(ref h.C, 40);
                    Forward(ref h.C);
                    var copy = local;
                    Replace(ref local, 7);
                    return $"{local.N},{copy.N},{arr[0].N},{arr[1].N},{h.C.N}";
                }
            }
            """, "T.Test()");

        Assert.Equal("7,6,0,3,141", output);
    }

    [Fact]
    public void RefParameter_WholeAssignUpdatesNestedStructInPlace()
    {
        // 外側を ref で置き換えた後、内側 member への ref も新しい値を見る
        var output = TestHelper.TranspileAndRun("""
            public struct Inner
            {
                public int V;
            }

            public struct Outer
            {
                public Inner I;
                public int W;
            }

            public static class T
            {
                private static int Swap(ref Outer o, ref Inner inner)
                {
                    var next = new Outer();
                    next.I.V = 9;
                    next.W = 4;
                    o = next;
                    return inner.V * 10 + o.W;
                }

                public static int Test()
                {
                    var a = new Outer();
                    a.I.V = 1;
                    return Swap(ref a, ref a.I) * 10 + a.I.V;
                }
            }
            """, "T.Test()");

        Assert.Equal("949", output);
    }

    [Fact]
    public void InParameter_PassesWithoutCopyAndDefensivelyCopiesMutation()
    {
        const string source = CounterSource + """
            public static class T
            {
                private static int Peek(in Counter c)
                {
                    c.Bump();
                    return c.N;
                }

                private static int Read(in Counter c) => c.N * 2;

                public static string Test()
                {
                    var mine = new Counter();
                    mine.N = 3;
                    var seen = Peek(in mine);
                    var viaPlain = Peek(mine);
                    return $"{seen},{viaPlain},{mine.N},{Read(mine)}";
                }
            }
            """;
        var lua = Transpiler.Transpile(source);

        Assert.Contains("T.read(mine)", lua);
        Assert.Equal("3,3,3,6", TestHelper.TranspileAndRun(source, "T.Test()"));
    }

    [Fact]
    public void ThisAssignment_InStructMethodReachesVariable()
    {
        var output = TestHelper.TranspileAndRun(CounterSource + """
            public static class T
            {
                public static string Test()
                {
                    var c = new Counter();
                    c.Bump();
                    c.Bump();
                    var before = c.N;
                    c.Reset();
                    var h = new Holder();
                    h.C.Bump();
                    h.C.Reset();
                    return $"{before},{c.N},{h.C.N}";
                }
            }
            """, "T.Test()");

        Assert.Equal("2,0,0", output);
    }

    [Fact]
    public void ClassStaticInitializer_CanCallOwnCtorAndMethods()
    {
        var output = TestHelper.TranspileAndRun("""
            public class V
            {
                public static V Zero = new V(1, 2);
                public static int Twice(int a) => a * 2;
                public static int T2 = Twice(4);
                public int X;
                public int Y;

                public V(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }

            public static class T
            {
                public static int Test() => V.Zero.X * 100 + V.Zero.Y * 10 + V.T2;
            }
            """, "T.Test()");

        Assert.Equal("128", output);
    }

    [Fact]
    public void RefParameterOfNonStruct_StillReportsDiagnostic()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            public readonly struct Frozen
            {
                public readonly int V;
            }

            public static class T
            {
                public static void Bump(ref int x) { x++; }
                public static void Set(ref Frozen f) { f = new Frozen(); }
            }
            """]);

        Assert.Equal(2, result.Warnings.Count(w => w.Contains("RefParameter")));
    }
}
