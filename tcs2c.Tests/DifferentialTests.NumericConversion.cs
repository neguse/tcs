namespace TinyCs.Tcs2c.Tests;

// int ↔ float の数値変換 (il-spec §5 / §6) と int の Sum の overflow (§12 / §13:
// 規範は TinySystem dotnet facade = checked な Enumerable.Sum)。期待値は同じ
// program を CoreCLR で実行した stdout
public partial class DifferentialTests
{
    [CFact]
    public void NumericConversion_IntToFloatAndBoundaries_MatchDotnet()
    {
        var output = Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public enum E { Min = -2147483648, Next }
            public class Box { public float F; }
            public class P
            {
                public static float Ret(int x) => x;
                public static float Id(float f) => f;
                public static int I(float f) => (int)f;
                public static void Main()
                {
                    int value = 16777217;
                    int neg = -16777217;
                    float rounded = value;
                    Console.WriteLine("assign " + I(rounded) + " " + I(neg));
                    Console.WriteLine("cmp " + (value == 16777216f ? 1 : 0) + " " + (value <= 16777216f ? 1 : 0) + " " + (neg == -16777216f ? 1 : 0) + " " + (16777216f != value ? 1 : 0));
                    Console.WriteLine("call " + I(Ret(value)) + " " + I(Id(value)) + " " + I(Id(neg)));
                    Console.WriteLine("cast " + I((float)value) + " " + ((float)value == 16777216f ? 1 : 0));
                    var box = new Box();
                    box.F = value;
                    float acc = 0f;
                    acc = value;
                    var arr = new float[1];
                    arr[0] = value;
                    var list = new List<float> { value };
                    list.Add(neg);
                    Console.WriteLine("store " + I(box.F) + " " + I(acc) + " " + I(arr[0]) + " " + I(list[0]) + " " + I(list[1]));
                    float? maybe = value;
                    Console.WriteLine("nullable " + I(maybe.Value));
                    float big = 100000;
                    float c = 16777217;
                    Console.WriteLine("const " + I(big * big / 1000000f) + " " + I(c) + " " + (c == 16777216f ? 1 : 0));
                    float mix = value * 1f;
                    float sum = 0.5f;
                    sum += value;
                    Console.WriteLine("arith " + I(mix) + " " + I(sum) + " " + I(value + 0f));
                    var fresh = new Box();
                    var zeros = new float[1];
                    float viaField = fresh.F + value;
                    float viaArray = zeros[0] + value;
                    Console.WriteLine("default " + I(viaField) + " " + (viaField == 16777216f ? 1 : 0) + " " + I(viaArray) + " " + (viaArray == 16777216f ? 1 : 0));
                    char ch = 'A';
                    float fc = ch;
                    Console.WriteLine("char " + I(fc * 2f));
                    int min = (int)E.Min;
                    Console.WriteLine("enum " + (min + 1 == -2147483647 ? 1 : 0) + " " + ((int)E.Next) + " " + (E.Min < E.Next ? 1 : 0));
                    float lo = -2147483648f;
                    float hi = 2147483520f;
                    Console.WriteLine("trunc " + (int)lo + " " + (int)hi + " " + (int)-2147483520f);
                }
            }
            """, "P");
        Assert.Equal("""
            assign 16777216 -16777216
            cmp 1 1 1 0
            call 16777216 16777216 -16777216
            cast 16777216 1
            store 16777216 16777216 16777216 16777216 -16777216
            nullable 16777216
            const 10000 16777216 1
            arith 16777216 16777216 16777216
            default 16777216 1 16777216 1
            char 130
            enum 1 -2147483647 1
            trunc -2147483648 2147483520 -2147483520
            """.ReplaceLineEndings("\n"), output);
    }

    private static string SumProgram(string sum) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        public class P
        {
            public static void Main()
            {
                Console.WriteLine("before");
                Console.WriteLine({{sum}});
                Console.WriteLine("after");
            }
        }
        """;

    // CoreCLR は "before" の後に OverflowException で停止する
    [CTheory]
    [InlineData("new List<int> { 2147483647, 1 }.Sum()")]
    [InlineData("new List<int> { -2147483647 - 1, -1 }.Sum()")]
    [InlineData("new List<int> { 2147483647, 1, -1 }.Sum(x => x)")]
    public void IntSum_Overflow_FaultsAfterPrecedingOutput(string sum)
    {
        Assert.Equal("before", Backends.AssertFaultParity(SumProgram(sum), "P"));
    }

    [CFact]
    public void IntSum_WithinRange_MatchDotnet()
    {
        var output = Backends.AssertParity(SumProgram(
            "new List<int> { 2147483647, -1, 1 }.Sum() + \" \" + " +
            "new List<int> { -2147483647 - 1, 1 }.Sum(x => x) + \" \" + " +
            "new List<int>().Sum() + \" \" + new List<float> { 1.5f, 2f }.Sum() + \" \" + " +
            "new List<int> { 2147483647, 2147483647 }.Sum(x => x / 2)"), "P");
        Assert.Equal("before\n2147483647 -2147483647 0 3.5 2147483646\nafter", output);
    }
}
