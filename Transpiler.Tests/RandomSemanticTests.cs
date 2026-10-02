namespace TinyCs.Tests;

public class RandomSemanticTests
{
    [Fact]
    public void Random_NextFloat_InRange()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            public class T
            {
                public static bool Test()
                {
                    var f = TinySystem.Random.NextFloat();
                    return f >= 0.0f && f < 1.0f;
                }
            }
            """, "tostring(T.test())");
        Assert.Equal("true", result);
    }

    [Fact]
    public void Random_Next_InRange()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            public class T
            {
                public static bool Test()
                {
                    var n = TinySystem.Random.Next(10);
                    return n >= 0 && n < 10;
                }
            }
            """, "tostring(T.test())");
        Assert.Equal("true", result);
    }

    [Fact]
    public void Random_Range_InRange()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            public class T
            {
                public static bool Test()
                {
                    var n = TinySystem.Random.Range(5, 10);
                    return n >= 5 && n <= 10;
                }
            }
            """, "tostring(T.test())");
        Assert.Equal("true", result);
    }

    [Fact]
    public void Random_Seed_MakesSequenceDeterministic()
    {
        var result = TestHelper.TranspileAndRunWithRuntime("""
            public class T
            {
                public static string Test()
                {
                    TinySystem.Random.Seed(42);
                    var a = TinySystem.Random.Next(1000) + "," + TinySystem.Random.Range(1, 6)
                        + "," + TinySystem.Random.NextFloat();
                    TinySystem.Random.Seed(42);
                    var b = TinySystem.Random.Next(1000) + "," + TinySystem.Random.Range(1, 6)
                        + "," + TinySystem.Random.NextFloat();
                    return (a == b) + ":" + a;
                }
            }
            """, "T.test()");
        // seed 42 の列は Lua 5.5 の math.randomseed(42) そのもの (C backend も同じ)
        Assert.StartsWith("true:", result);
    }
}
