using System.Text;

namespace TinyCs.Tcs2c.Tests;

// TinySystem.Text (RuneCount / RuneSubstring / Utf8Bytes) が tcs2c (runtime/utf8)、Lua (tcs_utf8 module)、.NET (TinySystem/Utf8.cs) で同じ結果を返す
public partial class DifferentialTests
{
    private static string Utf8Program(string body) => $$"""
        using System;
        using System.Collections.Generic;
        using TinySystem.Text;
        public class Holder { public Utf8Bytes Bytes; public string Name; }
        public class P
        {
            public static void Main()
            {
                {{body}}
            }
        }
        """;

    private const string Utf8Corpus =
        """var corpus = new[] { "", "abc", "é", "日本語", "\U0001F600", "a\U0001F600béc", "éé日\U0001F600\U0001F600" };""";

    [CFact]
    public void Utf8_RuneCountAndSubstring_AgreeOnEveryRange()
    {
        var output = Backends.AssertAllPathsAgree(Utf8Program(Utf8Corpus + """
            foreach (var s in corpus)
            {
                int n = s.RuneCount();
                Console.WriteLine(n);
                for (int start = 0; start <= n; start++)
                    for (int count = 0; count <= n - start; count++)
                        Console.WriteLine(start + "," + count + "=" + s.RuneSubstring(start, count) + "|" + Runes.RuneCount(Runes.RuneSubstring(s, start, count)));
            }
            string none = null;
            Console.WriteLine(none?.RuneCount() ?? -1);
            Console.WriteLine(none?.RuneSubstring(0, 0) ?? "null");
            """), "P");
        Assert.Contains("1,1=\U0001F600|1", output);
    }

    [CFact]
    public void Utf8Bytes_LengthIndexSliceToString_AgreeOnEveryRange()
    {
        var output = Backends.AssertAllPathsAgree(Utf8Program(Utf8Corpus + """
            foreach (var s in corpus)
            {
                var u = new Utf8Bytes(s);
                string bytes = "";
                for (int i = 0; i < u.Length; i++) bytes += u[i] + " ";
                Console.WriteLine(u.Length + ":" + bytes + ":" + u.ToString());
                for (int start = 0; start <= u.Length; start++)
                    for (int count = 0; count <= u.Length - start; count++)
                    {
                        // 文字の途中で切った slice の ToString は、壊れた並びを U+FFFD にする
                        var slice = u.Slice(start, count);
                        string first = count == 0 ? "-" : "" + slice[0];
                        Console.WriteLine(start + "," + count + "=" + slice.Length + "," + first + "," + slice.ToString() + "," + slice.Slice(0, count).ToString().RuneCount());
                    }
            }
            """), "P");
        Assert.Contains("240 159 152 128", output);
        Assert.Contains("�", output);
    }

    [CFact]
    public void Utf8Bytes_FlowsThroughLocalsFieldsListsAndNull()
    {
        var output = Backends.AssertAllPathsAgree(Utf8Program("""
            var h = new Holder();
            Console.WriteLine(h.Bytes == null);
            Console.WriteLine(h.Bytes?.Length ?? -1);
            h.Bytes = new Utf8Bytes("日本");
            int first = h.Bytes[0];
            byte second = h.Bytes[1];
            var third = h.Bytes[2];
            Console.WriteLine(first + second + third);
            Console.WriteLine(h.Bytes?.Length ?? -1);
            Console.WriteLine(h.Bytes?.Slice(3, 3)?.ToString() ?? "null");
            var list = new List<Utf8Bytes> { h.Bytes, h.Bytes.Slice(3, 3) };
            Console.WriteLine(list[1].ToString() + list[1].Length + (list[0] == h.Bytes) + (list[1] == list[0]));
            Utf8Bytes same = list[0];
            Console.WriteLine(same == h.Bytes);
            Console.WriteLine(h.Bytes?[0] ?? -1);
            """), "P");
        Assert.Equal("""
            True
            -1
            546
            6
            本
            本3TrueFalse
            True
            230
            """.ReplaceLineEndings("\n"), output);
    }

    [CTheory]
    [InlineData("""new Utf8Bytes("abc")[3]""")]
    [InlineData("""new Utf8Bytes("abc")[-1]""")]
    [InlineData("""new Utf8Bytes("abc").Slice(1, 1)[1]""")]
    [InlineData("""new Utf8Bytes("abc").Slice(-1, 1).Length""")]
    [InlineData("""new Utf8Bytes("abc").Slice(4, 0).Length""")]
    [InlineData("""new Utf8Bytes("abc").Slice(1, 3).Length""")]
    [InlineData("""new Utf8Bytes("abc").Slice(1, -1).Length""")]
    [InlineData("""new Utf8Bytes(Nothing()).Length""")]
    [InlineData(""" "é".RuneSubstring(2, 0)""")]
    [InlineData(""" "é".RuneSubstring(0, 2)""")]
    [InlineData(""" "é".RuneSubstring(-1, 1)""")]
    [InlineData(""" "é".RuneSubstring(0, -1)""")]
    [InlineData("Nothing().RuneCount()")]
    [InlineData("Nothing().RuneSubstring(0, 0)")]
    public void Utf8_OutOfRangeAndNull_FaultAfterPrecedingOutput(string call)
    {
        Assert.Equal("before", Backends.AssertFaultOnAllPaths($$"""
            using System;
            using TinySystem.Text;
            public class P
            {
                static string Nothing() { return null; }
                public static void Main()
                {
                    Console.WriteLine("before");
                    Console.WriteLine({{call}});
                }
            }
            """, "P"));
    }

    [Theory]
    [InlineData("Func<string, int> f = Runes.RuneCount;", "TCS1001")]
    [InlineData("Func<int> f = \"\".RuneSubstring(0, 0).RuneCount;", "TCS1001")]
    [InlineData("Func<int, int, Utf8Bytes> f = new Utf8Bytes(\"\").Slice;", "TCS1001")]
    [InlineData("Console.WriteLine(\"\" + new Utf8Bytes(\"\"));", "UnsupportedToString")]
    [InlineData("Console.WriteLine($\"{new Utf8Bytes(\"\")}\");", "UnsupportedToString")]
    [InlineData("Console.WriteLine(new Utf8Bytes(\"\"));", "UnsupportedToString")]
    public void Utf8_UnsupportedForms_AreDiagnosed(string statement, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Backends.EmitC([Utf8Program(statement)], "P"));
        Assert.Contains(expected, ex.Message);
        ex = Assert.Throws<InvalidOperationException>(() => Backends.RunLua([Utf8Program(statement)], "P"));
        Assert.Contains(expected, ex.Message);
    }

    // Trim と引数なしの Split の空白は .NET の char.IsWhiteSpace と同じ。空白は全部 U+3000 以下
    [CFact]
    public void TrimAndSplit_WhiteSpaceMatchesDotnetForEveryRune()
    {
        var output = Backends.AssertAllPathsAgree("""
            using System;
            public class P
            {
                public static void Main()
                {
                    string trimmed = "";
                    string split = "";
                    for (int rune = 0; rune <= 0x3100; rune++)
                    {
                        if (rune >= 0xD800 && rune <= 0xDFFF) continue;
                        string text = char.ConvertFromUtf32(rune);
                        if ((text + "x" + text).Trim() == "x") trimmed += rune + ",";
                        if (("x" + text + "y").Split().Length == 2) split += rune + ",";
                    }
                    Console.WriteLine(trimmed);
                    Console.WriteLine(split);
                    Console.WriteLine(char.ConvertFromUtf32(0x10FFFF).Trim() == char.ConvertFromUtf32(0x10FFFF));
                }
            }
            """, "P");
        Assert.Contains("133,160,5760,8192,", output);
        Assert.Contains("8239,8287,12288,", output);
    }

    [CFact]
    public void TrimAndSplit_MixedTextAgrees()
    {
        var output = Backends.AssertAllPathsAgree("""
            using System;
            public class P
            {
                static void Show(string s)
                {
                    Console.WriteLine("[" + s.Trim() + "]");
                    Console.WriteLine(string.Join("|", s.Split()));
                }
                public static void Main()
                {
                    Show("");
                    Show("  ");
                    Show("abc");
                    Show(" \t\n\r\f\v abc \t\n");
                    Show("\u3000\u00a0日本 語\u2003\u0085");
                    Show("a  b\u3000\u3000c");
                    Show("é\u00a0é\u2028😀\u2029");
                    Show("\u200b\ufeffx\ufeff\u200b");
                }
            }
            """, "P");
        Assert.Contains("[日本 語]", output);
        Assert.Contains("a||b||c", output);
    }

    // lib 形のフレーム境界 GC を越えて、Utf8Bytes が持つ文字列が生き残る
    [CFact]
    public void Utf8Bytes_SurvivesFrameBoundaryGc()
    {
        var output = Backends.AssertParityLib("""
            using System;
            using TinySystem.Text;
            public class G
            {
                static Utf8Bytes kept;
                static int frame;
                static int sum;
                public static void Setup()
                {
                    string text = "";
                    for (int i = 0; i < 40; i++) text += "日" + i;
                    kept = new Utf8Bytes(text).Slice(3, 30);
                }
                public static void Frame()
                {
                    frame++;
                    string garbage = "";
                    for (int i = 0; i < 40; i++) garbage += i;
                    kept = kept.Slice(1, kept.Length - 1);
                    sum += kept[0] + garbage.RuneCount();
                }
                public static void Report()
                {
                    Console.WriteLine(sum + ":" + kept.Length + ":" + kept.ToString().RuneCount());
                }
            }
            """, "G", 5);
        Assert.Equal("1175:25:13", output);
    }

    // runtime/utf8 の復号が .NET の UTF8 と同じ位置で U+FFFD を出す。tcs の program からは壊れた byte 列を作れないので、C を直接比べる
    [CFact]
    public void Utf8_RuntimeScrubMatchesDotnetDecoderOnInvalidBytes()
    {
        var header = Path.Combine(Backends.ProjectRoot, "runtime", "utf8", "tcs_utf8.h").Replace('\\', '/');
        var impl = Path.Combine(Backends.ProjectRoot, "runtime", "utf8", "tcs_utf8.c").Replace('\\', '/');
        var driver = $$"""
            #include <stdio.h>
            #include <stdint.h>
            #include <stddef.h>
            #include "{{header}}"
            #include "{{impl}}"
            static uint32_t hash = 2166136261u;
            static void check(const unsigned char *s, size_t n)
            {
                unsigned char out[64];
                size_t length = tcs_utf8_scrub_length(s, n), i;
                tcs_utf8_scrub(s, n, out);
                for (i = 0; i < length; i++) hash = (hash ^ out[i]) * 16777619u;
                hash = (hash ^ (uint32_t)tcs_utf8_rune_count(s, n)) * 16777619u;
            }
            int main(void)
            {
                static const unsigned char tail[] = { 0x00, 0x7F, 0x80, 0x8F, 0x90, 0x9F, 0xA0, 0xBF, 0xC0, 0xF5 };
                unsigned char s[4];
                int a, b, c, d;
                for (a = 0; a < 256; a++) {
                    s[0] = (unsigned char)a;
                    check(s, 1);
                    for (b = 0; b < 256; b++) {
                        s[1] = (unsigned char)b;
                        check(s, 2);
                        for (c = 0; c < 10; c++) {
                            s[2] = tail[c];
                            check(s, 3);
                            for (d = 0; d < 10; d++) { s[3] = tail[d]; check(s, 4); }
                        }
                    }
                }
                printf("%u\n", hash);
                return 0;
            }
            """;
        Assert.Equal(DotnetScrubHash(), Backends.CompileAndRunC(driver));
    }

    private static string DotnetScrubHash()
    {
        byte[] tail = [0x00, 0x7F, 0x80, 0x8F, 0x90, 0x9F, 0xA0, 0xBF, 0xC0, 0xF5];
        uint hash = 2166136261u;
        var s = new byte[4];
        void Check(int n)
        {
            var text = Encoding.UTF8.GetString(s, 0, n);
            foreach (var b in Encoding.UTF8.GetBytes(text)) hash = unchecked((hash ^ b) * 16777619u);
            hash = unchecked((hash ^ (uint)text.EnumerateRunes().Count()) * 16777619u);
        }
        for (var a = 0; a < 256; a++)
        {
            s[0] = (byte)a;
            Check(1);
            for (var b = 0; b < 256; b++)
            {
                s[1] = (byte)b;
                Check(2);
                foreach (var c in tail)
                {
                    s[2] = c;
                    Check(3);
                    foreach (var d in tail) { s[3] = d; Check(4); }
                }
            }
        }
        return hash.ToString();
    }
}
