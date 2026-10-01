namespace TinyCs.Tcs2c.Tests;

// GC の意味論テスト: 到達可能な object が回収されない (各 root / 各 container
// 種別経由)、到達不能な garbage が回収されて heap が有界に保たれる、
// lib 出荷形でも entry 境界で GC が回る。stress (確保ごとに full GC) で
// Lua と同じ stdout を要求する。
public class GcTests
{
    [CFact]
    public void Churn_KeepsLiveGraph_AndMatchesLua()
    {
        const string source = """
            using System.Collections.Generic;
            public class Node
            {
                public int V;
                public Node Next;
                public string Tag;
                public Node(int v, Node next) { V = v; Next = next; Tag = "n" + v.ToString(); }
            }
            public class Churn
            {
                public static List<int> Keep = new List<int>();
                public static Node Head;
                public static void Main()
                {
                    int sum = 0;
                    for (int i = 0; i < 20000; i++)
                    {
                        var n = new Node(i, i % 1000 == 0 ? Head : null);
                        if (i % 1000 == 0) Head = n;
                        var arr = new int[16];
                        arr[3] = i;
                        var l = new List<float>();
                        l.Add(1.5f);
                        l.Add((float)i);
                        var s = "x" + i.ToString();
                        sum = sum + arr[3] % 7 + (i % 2 == 0 ? 1 : 0) + s.Length;
                        if (i % 5000 == 0) Keep.Add(sum);
                    }
                    int chain = 0;
                    var c = Head;
                    while (c != null) { chain = chain + 1; sum = sum + c.Tag.Length; c = c.Next; }
                    System.Console.WriteLine(sum);
                    System.Console.WriteLine(chain);
                    System.Console.WriteLine(Keep.Count);
                }
            }
            """;
        Backends.AssertParity(source, "Churn");
    }

    [CFact]
    public void Roots_AllContainerKinds_SurviveStressGc()
    {
        // class field / struct-in-class / struct 配列 / ref 配列 / Dict 値 /
        // string key / closure 捕捉 cell / static — 各経路で保持した object が
        // garbage の大量確保の後も生きている
        const string source = """
            using System;
            using System.Collections.Generic;
            public struct Slot { public int Id; public string Name; public Payload Data; }
            public class Payload { public string Text; public int[] Nums; public Payload(string t) { Text = t; Nums = new int[4]; Nums[2] = t.Length; } }
            public class Holder
            {
                public Slot Primary;
                public Slot[] Slots = new Slot[3];
                public Payload[] Refs = new Payload[2];
                public Dictionary<string, Payload> ByName = new Dictionary<string, Payload>();
                public Dictionary<int, Slot> ById = new Dictionary<int, Slot>();
                public Func<string> Thunk;
            }
            public class Program
            {
                public static Holder Static;
                public static Slot StaticSlot;
                public static void Main()
                {
                    var h = new Holder();
                    h.Primary.Id = 1; h.Primary.Name = "pri" + "mary"; h.Primary.Data = new Payload("alpha");
                    var s1 = new Slot(); s1.Name = "slot" + 1.ToString(); s1.Data = new Payload("beta");
                    h.Slots[1] = s1;
                    h.Refs[0] = new Payload("gamma");
                    h.ByName["k" + 1.ToString()] = new Payload("delta");
                    var s = new Slot(); s.Name = "by" + "id"; s.Data = new Payload("epsilon");
                    h.ById[7] = s;
                    var captured = new Payload("zeta");
                    h.Thunk = () => captured.Text + "!";
                    Static = h;
                    StaticSlot.Data = new Payload("eta");
                    h = null;
                    int junk = 0;
                    for (int i = 0; i < 3000; i++)
                    {
                        var p = new Payload("junk" + i.ToString());
                        var d = new Dictionary<string, int>();
                        d[p.Text] = i;
                        junk = junk + p.Nums[2] + d.Count;
                    }
                    var g = Static;
                    Console.WriteLine(g.Primary.Name + ":" + g.Primary.Data.Text + ":" + g.Primary.Data.Nums[2].ToString());
                    Console.WriteLine(g.Slots[1].Name + ":" + g.Slots[1].Data.Text);
                    Console.WriteLine(g.Refs[0].Text);
                    Console.WriteLine(g.ByName["k1"].Text);
                    Console.WriteLine(g.ById[7].Name + ":" + g.ById[7].Data.Text);
                    var th = g.Thunk;
                    Console.WriteLine(th());
                    Console.WriteLine(StaticSlot.Data.Text);
                    Console.WriteLine(junk);
                }
            }
            """;
        Backends.AssertParity(source, "Program");
    }

    [CFact]
    public void Garbage_IsReclaimed_HeapStaysBounded()
    {
        // 到達不能な確保を大量に繰り返しても heap が有界: 生成 C に GC 統計の
        // 読み出しを足して、総確保 bytes >> 生存 bytes を確認する
        const string source = """
            public class Blob { public int[] Data; public Blob() { Data = new int[256]; } }
            public class Program
            {
                public static void Main()
                {
                    int acc = 0;
                    for (int i = 0; i < 20000; i++)
                    {
                        var b = new Blob();
                        b.Data[i % 256] = i;
                        acc = acc + b.Data[i % 256] % 3;
                    }
                    System.Console.WriteLine(acc);
                }
            }
            """;
        var c = Backends.EmitC([source], "Program");
        // main の直前に統計印字を差し込む (runtime 内部の static 変数を読む)
        c = c.Replace("    tcs_gc_leave(saved);\n    return 0;",
            "    printf(\"collections=%zu live=%zu\\n\", tcs_gc_collections, tcs_gc_live_bytes);\n" +
            "    tcs_gc_leave(saved);\n    return 0;");
        var output = Backends.CompileAndRunC(c);
        var lines = output.Split('\n');
        Assert.Equal("19999", lines[0]);
        var stats = lines[1].Split(' ');
        var collections = long.Parse(stats[0].Split('=')[1]);
        var live = long.Parse(stats[1].Split('=')[1]);
        // 20000 × ~1KB = ~20MB の確保に対し、既定 threshold (1MB) で複数回 GC が
        // 走り、直近 GC 後の生存 bytes は 1 Blob 分程度に収まる
        Assert.True(collections >= 5, $"expected several collections, got {collections}");
        Assert.True(live < 64 * 1024, $"live bytes after last GC too large: {live}");
    }

    [CFact]
    public void LibMode_EntryBoundaries_CollectAndRetainStatics()
    {
        // --lib: host の main から entry を繰り返し呼ぶ。static に保持した
        // object は entry を跨いで生き、host は tcs_lib_gc で明示 GC できる
        const string source = """
            using System.Collections.Generic;
            public class State
            {
                public static List<int> Log = new List<int>();
                public static string Last;
                public static void Step()
                {
                    for (int i = 0; i < 500; i++)
                    {
                        var s = "step" + i.ToString();
                        if (i == 499) Last = s;
                    }
                    Log.Add(Last.Length);
                }
                public static void Report()
                {
                    System.Console.WriteLine(Log.Count);
                    System.Console.WriteLine(Last);
                }
            }
            """;
        var c = Backends.EmitC([source], null, lib: true);
        const string host = """
            void tcs_lib_init(void);
            void tcs_lib_gc(void);
            void tcs_entry_State_step(void);
            void tcs_entry_State_report(void);
            int main(void)
            {
                int i;
                tcs_lib_init();
                for (i = 0; i < 3; i++) {
                    tcs_entry_State_step();
                    tcs_lib_gc();
                }
                tcs_entry_State_report();
                return 0;
            }
            """;
        var output = Backends.CompileAndRunC(c, stress: true, extraCSource: host);
        Assert.Equal("3\nstep499", output);
    }

    [CFact]
    public void StringLiterals_AreStaticObjects_NotPerEvaluation()
    {
        const string source = """
            public class Program
            {
                public static string Pick(int i) => i % 2 == 0 ? "even" : "odd";
                public static void Main()
                {
                    int n = 0;
                    for (int i = 0; i < 10; i++) n = n + Pick(i).Length;
                    System.Console.WriteLine(n);
                    System.Console.WriteLine(Pick(4) == Pick(2));
                }
            }
            """;
        var c = Backends.EmitC([source], "Program");
        Assert.Contains("TCS_GC_STATIC", c);
        Assert.DoesNotContain("tcs_string_new((const unsigned char *)\"\\x65", c);
        Assert.Equal("35\ntrue", Backends.CompileAndRunC(c, stress: true));
        Assert.Equal("35\ntrue", Backends.RunLua([source], "Program"));
    }
}
