namespace TinyCs.Tests;

// 明示数値変換 (il-spec §5): f32 → i32 は 0 方向切り捨て。cast を素通しすると
// Lua 側で float のまま残り、後段の整数演算 (idiv / 添字 / 補間) が壊れる。
public class NumericConversionTests
{
    [Fact]
    public void FloatToIntCast_TruncatesTowardZero()
    {
        var output = TestHelper.TranspileAndRun("""
            public class T
            {
                public static int Trunc(float f) => (int)f;

                public static string Test() =>
                    $"{Trunc(3.7f)},{Trunc(-3.7f)},{Trunc(0.5f)},{Trunc(-0.5f)},{Trunc(8f)}";
            }
            """, "T.Test()");

        Assert.Equal("3,-3,0,0,8", output);
    }

    [Fact]
    public void FloatToIntCast_ResultIsInteger()
    {
        // cast 結果を整数として使う: idiv / 補間 / 乗算
        var output = TestHelper.TranspileAndRun("""
            public class T
            {
                public static string Test()
                {
                    float f = 7.9f;
                    int i = (int)f;
                    int half = (int)f / 2;
                    int twice = (int)f * 2;
                    return $"{i}|{half}|{twice}";
                }
            }
            """, "T.Test()");

        Assert.Equal("7|3|14", output);
    }

    [Fact]
    public void FloatToIntCast_IndexesList()
    {
        var output = TestHelper.TranspileAndRun("""
            using System.Collections.Generic;

            public class T
            {
                public static int Test()
                {
                    var xs = new List<int> { 10, 20, 30 };
                    float pos = 1.75f;
                    return xs[(int)pos];
                }
            }
            """, "T.Test()");

        Assert.Equal("20", output);
    }

    [Fact]
    public void FloatToIntCast_InTopLevelAndLegacyExpressions()
    {
        var output = TestHelper.TranspileAndRun("""
            float speed = 2.5f;
            var steps = (int)(speed * 3f);
            var check = steps * 10;
            """, "check");

        Assert.Equal("70", output);
    }

    [Fact]
    public void FloatToIntCast_ConstantIsFolded()
    {
        var output = TestHelper.TranspileAndRun("""
            public class T
            {
                public static int Test() => (int)3.99f + (int)-2.5f;
            }
            """, "T.Test()");

        Assert.Equal("1", output);
    }

    [Fact]
    public void FloatToIntCast_NaNFaults()
    {
        // il-spec §5 / §12: 変換元が NaN なら fault (C# unchecked は値未規定)
        var lua = Transpiler.Transpile("""
            public class T
            {
                public static int Test()
                {
                    float z = 0f;
                    return (int)(z / z);
                }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestHelper.RunLua(lua + "\nprint(T.test())"));
        Assert.Contains("float to int", ex.Message);
    }
}
