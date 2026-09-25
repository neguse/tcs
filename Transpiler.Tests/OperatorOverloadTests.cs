namespace TinyCs.Tests;

// ユーザー定義演算子オーバーロード (binary + - * / % / unary -) の
// Lua metamethod (__add/__sub/__mul/__div/__mod/__unm) 写像を固定する。
// 同一演算子の複数 overload は metamethod 内の実行時型分岐で dispatch する。
public class OperatorOverloadTests
{
    private const string Vec2Source = """
        public class Vec2
        {
            public float X;
            public float Y;

            public Vec2(float x, float y)
            {
                X = x;
                Y = y;
            }

            public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
            public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
            public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
            public static Vec2 operator *(Vec2 a, Vec2 b) => new Vec2(a.X * b.X, a.Y * b.Y);
            public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
            public static Vec2 operator *(float s, Vec2 a) => new Vec2(s * a.X, s * a.Y);
            public static Vec2 operator /(Vec2 a, Vec2 b) => new Vec2(a.X / b.X, a.Y / b.Y);
            public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
        }
        """;

    [Fact]
    public void BinaryAdd_SingleOverload()
    {
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var v = new Vec2(1, 2) + new Vec2(10, 20);
                    return v.X * 100 + v.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("1122", result);
    }

    [Fact]
    public void BinarySubtract_SingleOverload()
    {
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var v = new Vec2(10, 20) - new Vec2(1, 2);
                    return v.X * 100 + v.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("918", result);
    }

    [Fact]
    public void UnaryMinus()
    {
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var v = -new Vec2(3, -4);
                    return v.X * 100 + v.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("-296", result);
    }

    [Fact]
    public void MultiplyOverloads_DispatchOnOperandTypes()
    {
        // Vec2*Vec2 / Vec2*float / float*Vec2 が同じ __mul に共存し、
        // 実行時の operand 型で正しい overload に分岐する。
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var vv = new Vec2(2, 3) * new Vec2(4, 5);   // (8, 15)
                    var vs = new Vec2(2, 3) * 10.0f;            // (20, 30)
                    var sv = 100.0f * new Vec2(2, 3);           // (200, 300)
                    return vv.X + vv.Y + vs.X + vs.Y + sv.X + sv.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("573.0", result);
    }

    [Fact]
    public void DivideOverloads_DispatchOnOperandTypes()
    {
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var vv = new Vec2(8, 15) / new Vec2(4, 5);  // (2, 3)
                    var vs = new Vec2(20, 30) / 10.0f;          // (2, 3)
                    return vv.X + vv.Y + vs.X + vs.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("10.0", result);
    }

    [Fact]
    public void Modulo_SingleOverload()
    {
        var result = TestHelper.TranspileAndRun("""
            public class Wrap
            {
                public int Value;

                public Wrap(int value)
                {
                    Value = value;
                }

                public static Wrap operator %(Wrap a, Wrap b) => new Wrap(a.Value % b.Value);
            }

            public class T
            {
                public static int Test()
                {
                    return (new Wrap(17) % new Wrap(5)).Value;
                }
            }
            """, "T.test()");
        Assert.Equal("2", result);
    }

    [Fact]
    public void CompoundAssignment_UsesOperatorOverload()
    {
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var v = new Vec2(1, 2);
                    v += new Vec2(10, 20);
                    v *= 2.0f;
                    return v.X * 100 + v.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("2244.0", result);
    }

    [Fact]
    public void ChainedExpression_MixesOverloadsAndPrecedence()
    {
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var a = new Vec2(1, 2);
                    var b = new Vec2(3, 4);
                    var v = a + b * 2.0f - a;  // b * 2 = (6, 8)
                    return v.X * 100 + v.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("608.0", result);
    }

    [Fact]
    public void OperatorWithStatementBody()
    {
        var result = TestHelper.TranspileAndRun("""
            public class Acc
            {
                public int Total;

                public Acc(int total)
                {
                    Total = total;
                }

                public static Acc operator +(Acc a, Acc b)
                {
                    var sum = a.Total + b.Total;
                    return new Acc(sum);
                }
            }

            public class T
            {
                public static int Test() => (new Acc(40) + new Acc(2)).Total;
            }
            """, "T.test()");
        Assert.Equal("42", result);
    }

    [Fact]
    public void RecordClass_OperatorOverload()
    {
        var result = TestHelper.TranspileAndRun("""
            public record Point(float X, float Y)
            {
                public static Point operator +(Point a, Point b) => new Point(a.X + b.X, a.Y + b.Y);
            }

            public class T
            {
                public static float Test()
                {
                    var p = new Point(1, 2) + new Point(10, 20);
                    return p.X * 100 + p.Y;
                }
            }
            """, "T.test()");
        Assert.Equal("1122", result);
    }

    [Fact]
    public void InstanceMembersAndOperators_Coexist()
    {
        var result = TestHelper.TranspileAndRun(Vec2Source + """
            public class T
            {
                public static float Test()
                {
                    var v = new Vec2(3, 4) + new Vec2(0, 0);
                    return Dot(v, v);
                }

                private static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
            }
            """, "T.test()");
        Assert.Equal("25", result);
    }

    // 基底クラスで宣言した operator は派生 instance でも使える (C# の
    // overload 解決は静的)。Lua の metamethod は __index 継承されないため、
    // 呼び出しサイトで宣言型の operator 関数を直接呼ぶ。
    private const string InheritedOperatorSource = """
        public class Money
        {
            public int Cents;

            public Money(int cents)
            {
                Cents = cents;
            }

            public static Money operator +(Money a, Money b) => new Money(a.Cents + b.Cents);
            public static Money operator -(Money a) => new Money(-a.Cents);
            public static Money operator *(Money a, int k) => new Money(a.Cents * k);
            public static Money operator *(Money a, Money b) => new Money(a.Cents * b.Cents);
        }

        public class Tip : Money
        {
            public Tip(int cents) : base(cents) { }
        }
        """;

    [Fact]
    public void BaseClassBinaryOperator_WorksOnDerivedInstances()
    {
        var result = TestHelper.TranspileAndRun(InheritedOperatorSource + """
            public class T
            {
                public static int Test()
                {
                    var sum = new Tip(1) + new Tip(2);
                    return sum.Cents;
                }
            }
            """, "T.test()");
        Assert.Equal("3", result);
    }

    [Fact]
    public void BaseClassUnaryOperator_WorksOnDerivedInstances()
    {
        var result = TestHelper.TranspileAndRun(InheritedOperatorSource + """
            public class T
            {
                public static int Test() => (-new Tip(5)).Cents;
            }
            """, "T.test()");
        Assert.Equal("-5", result);
    }

    [Fact]
    public void BaseClassOverloadedOperator_DispatchesStaticallyForDerived()
    {
        // 複数 overload の実行時 dispatcher は getmetatable(a) == Money で
        // 判定するため派生 instance を取りこぼしていた
        var result = TestHelper.TranspileAndRun(InheritedOperatorSource + """
            public class T
            {
                public static int Test()
                {
                    var scaled = new Tip(3) * 4;
                    var squared = new Tip(3) * new Tip(5);
                    return scaled.Cents * 100 + squared.Cents;
                }
            }
            """, "T.test()");
        Assert.Equal("1215", result);
    }

    [Fact]
    public void BaseClassOperator_CompoundAssignmentOnDerived()
    {
        var result = TestHelper.TranspileAndRun(InheritedOperatorSource + """
            public class T
            {
                public static int Test()
                {
                    Money total = new Tip(1);
                    total += new Tip(4);
                    total *= 3;
                    return total.Cents;
                }
            }
            """, "T.test()");
        Assert.Equal("15", result);
    }

    [Fact]
    public void EqualityOperators_ReportUnsupportedSyntax()
    {
        // 通常 class の operator == / != はスコープ外 (record __eq のみ対応)。
        var result = Transpiler.TranspileWithDiagnostics(["""
            public class Id
            {
                public int Value;

                public static bool operator ==(Id a, Id b) => a.Value == b.Value;
                public static bool operator !=(Id a, Id b) => a.Value != b.Value;
                public override bool Equals(object o) => false;
                public override int GetHashCode() => 0;
            }
            """]);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(2, result.Warnings.Count(w =>
            w.Contains(TinyCsDiagnosticIds.UnsupportedSyntax)
            && w.Contains("OperatorDeclaration")));
        Assert.Contains(result.Warnings, w => w.Contains("OperatorDeclaration(==)"));
        Assert.Contains(result.Warnings, w => w.Contains("OperatorDeclaration(!=)"));
    }

    [Fact]
    public void ConversionOperator_ReportsUnsupportedSyntax()
    {
        var result = Transpiler.TranspileWithDiagnostics(["""
            public class Meters
            {
                public float Value;

                public static implicit operator float(Meters m) => m.Value;
            }
            """]);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Contains(result.Warnings, w =>
            w.Contains(TinyCsDiagnosticIds.UnsupportedSyntax)
            && w.Contains("ConversionOperatorDeclaration"));
    }

    [Fact]
    public void SupportedOperators_ProduceNoWarnings()
    {
        var result = Transpiler.TranspileWithDiagnostics([Vec2Source]);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("OperatorDeclaration"));
    }
}
