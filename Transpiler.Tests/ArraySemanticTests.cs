namespace TinyCs.Tests;

/// <summary>
/// 固定長配列 (il-spec §11): `new T[n]` は default(T) で埋まり、Length と
/// foreach は null 要素 (Lua の nil) があっても宣言した長さを保つ。
/// Lua では長さ field `n` を持つ table で表す。
/// </summary>
public class ArraySemanticTests
{
    private static string Run(string body, string returnType = "int") =>
        TestHelper.TranspileAndRunWithRuntime($$"""
            using System.Linq;
            public struct V { public int X; }
            public class T
            {
                public static {{returnType}} Test()
                {
                    {{body}}
                }
            }
            """, "T.test()");

    [Fact]
    public void NewArray_Length_IsDeclaredLength()
    {
        Assert.Equal("4", Run("""
            float[] a = new float[4];
            return a.Length;
            """));
    }

    [Fact]
    public void NewArray_Length_UnchangedByPartialWrites()
    {
        Assert.Equal("4", Run("""
            float[] a = new float[4];
            a[0] = 1f; a[1] = 2f;
            return a.Length;
            """));
    }

    [Theory]
    [InlineData("int[] a = new int[3]; return a[2] == 0;")]
    [InlineData("float[] a = new float[3]; return a[1] == 0f;")]
    [InlineData("bool[] a = new bool[3]; return a[0] == false;")]
    [InlineData("string[] a = new string[3]; return a[0] == null;")]
    [InlineData("bool?[] a = new bool?[3]; return a[1] == null;")]
    public void NewArray_Elements_AreDefault(string body)
    {
        Assert.Equal("true", Run(body, "bool"));
    }

    [Fact]
    public void NewArray_ValueElements_SupportArithmetic()
    {
        Assert.Equal("3", Run("""
            int[] a = new int[4];
            a[0] = 1; a[1] = 2;
            int sum = 0;
            for (int i = 0; i < a.Length; i++) sum += a[i];
            return sum;
            """));
    }

    [Fact]
    public void NewArray_Foreach_VisitsEveryElement()
    {
        Assert.Equal("4", Run("""
            float[] a = new float[4];
            int count = 0;
            foreach (var x in a) count++;
            return count;
            """));
    }

    [Fact]
    public void NewArray_ReferenceElements_KeepLengthAndForeach()
    {
        Assert.Equal("3/3/2", Run("""
            string[] a = new string[3];
            a[0] = "q";
            int count = 0, nulls = 0;
            foreach (var s in a)
            {
                count++;
                if (s == null) nulls++;
            }
            return $"{a.Length}/{count}/{nulls}";
            """, "string"));
    }

    [Fact]
    public void ArrayLiteral_WithNullElement_KeepsLength()
    {
        Assert.Equal("3/1", Run("""
            var a = new[] { "x", null, "z" };
            int nulls = 0;
            foreach (var s in a) if (s == null) nulls++;
            return $"{a.Length}/{nulls}";
            """, "string"));
    }

    [Fact]
    public void ArrayLiteral_LengthOnLiteralReceiver()
    {
        Assert.Equal("3", Run("""
            return new[] { 1, 2, 3 }.Length;
            """));
    }

    [Fact]
    public void NewArray_ZeroLength()
    {
        Assert.Equal("0/0", Run("""
            int[] a = new int[0];
            int count = 0;
            foreach (var x in a) count++;
            return $"{a.Length}/{count}";
            """, "string"));
    }

    [Fact]
    public void NewArray_NegativeLength_Faults()
    {
        Assert.Throws<InvalidOperationException>(() => Run("""
            int n = -1;
            int[] a = new int[n];
            return a.Length;
            """));
    }

    [Fact]
    public void NewArray_StructElements_AreIndependentValues()
    {
        Assert.Equal("5/0", Run("""
            V[] vs = new V[2];
            vs[0].X = 5;
            return $"{vs[0].X}/{vs[1].X}";
            """, "string"));
    }

    [Fact]
    public void NewArray_Jagged_OuterLengthAndNullRows()
    {
        Assert.Equal("3/true", Run("""
            int[][] m = new int[3][];
            return $"{m.Length}/{m[1] == null}";
            """, "string"));
    }

    [Fact]
    public void Foreach_NestedArrays_WithContinue()
    {
        Assert.Equal("4", Run("""
            int[][] m = new int[2][];
            m[0] = new[] { 1, 2 };
            m[1] = new[] { 3 };
            int sum = 0;
            foreach (var row in m)
                foreach (var v in row)
                {
                    if (v == 2) continue;
                    sum += v;
                }
            return sum;
            """));
    }

    [Fact]
    public void Foreach_EvaluatesCollectionOnce()
    {
        // C# は foreach 開始時の配列を回し続ける (途中の再代入は影響しない)
        Assert.Equal("6", Run("""
            int[] a = new[] { 1, 2, 3 };
            int sum = 0;
            foreach (var v in a)
            {
                a = new[] { 100 };
                sum += v;
            }
            return sum;
            """));
    }

    [Fact]
    public void Foreach_CapturesFreshVariablePerIteration()
    {
        Assert.Equal("4", Run("""
            var fs = new System.Collections.Generic.List<System.Func<int>>();
            foreach (var v in new[] { 1, 2, 3 }) fs.Add(() => v);
            return fs[0]() + fs[2]();
            """));
    }

    [Fact]
    public void PropertyPattern_ArrayLength()
    {
        Assert.Equal("true", Run("""
            string[] a = new string[] { "a", "b" };
            return a is { Length: 2 };
            """, "bool"));
    }

    [Fact]
    public void ConditionalAccess_ArrayLength()
    {
        Assert.Equal("2", Run("""
            string[] a = new string[] { "a", "b" };
            return a?.Length ?? -1;
            """));
    }

    [Fact]
    public void Linq_OverArrayWithNullElements_CountsEveryElement()
    {
        Assert.Equal("3/2/true", Run("""
            string[] a = new string[3];
            a[0] = "q";
            return $"{a.Count()}/{a.Count(s => s == null)}/{a.Any(s => s == null)}";
            """, "string"));
    }

    [Fact]
    public void StringJoin_ArrayWithNullElement_JoinsEmpty()
    {
        Assert.Equal("x||z", Run("""
            var a = new[] { "x", null, "z" };
            return string.Join("|", a);
            """, "string"));
    }

    [Fact]
    public void StringSplit_ResultIsArrayWithLength()
    {
        Assert.Equal("3/3", Run("""
            var parts = "a,b,c".Split(",");
            int count = 0;
            foreach (var p in parts) count++;
            return $"{parts.Length}/{count}";
            """, "string"));
    }

    [Fact]
    public void FacadeSplit_ReturnsListWithoutLengthField()
    {
        // TinySystem.String.Split facade は List<string> を返す。runtime の
        // String.Split は string.Split と共用なので、List に長さ field が残ると
        // Add の後に LINQ / Join が古い長さを読む
        var result = TestHelper.TranspileAndRunWithRuntime("""
            using System.Linq;
            public class T
            {
                public static string Test()
                {
                    var list = TinySystem.String.Split("a,b", ",");
                    list.Add("c");
                    return $"{list.Count}/{list.Count()}/{string.Join("|", list)}";
                }
            }
            """, "T.test()");
        Assert.Equal("3/3/a|b|c", result);
    }

    [Fact]
    public void FieldInitializer_NewArray_ClearLoopUsesLength()
    {
        // lubx.FixedStep の edge クリア (neguse/lub#54) と同じ形: field 初期化子の
        // new bool[n] を Length で回して消す
        var result = TestHelper.TranspileAndRunWithRuntime("""
            public class Pending
            {
                private bool[] pressed = new bool[45];
                public void Press(int i) { pressed[i] = true; }
                public void Clear()
                {
                    for (int i = 0; i < pressed.Length; i++) pressed[i] = false;
                }
                public bool Get(int i) { return pressed[i]; }
            }
            public class T
            {
                public static string Test()
                {
                    var p = new Pending();
                    p.Press(1);
                    bool before = p.Get(1);
                    p.Clear();
                    return $"{before}/{p.Get(1)}/{p.Get(44)}";
                }
            }
            """, "T.test()");
        Assert.Equal("true/false/false", result);
    }

    [Fact]
    public void StaticFieldInitializer_NewArray()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            public class T
            {
                static int[] xs = new int[3];
                public static string Test()
                {
                    xs[0] = 7;
                    return $"{xs.Length}/{xs[0]}/{xs[2]}";
                }
            }
            """, "T.test()");
        Assert.Equal("3/7/0", result);
    }
}
