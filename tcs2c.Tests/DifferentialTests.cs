namespace TinyCs.Tcs2c.Tests;

// 言語機能 / runtime 表面ごとの 2 backend differential。各 program は
// Main の stdout を C (通常 + GC stress) と Lua で突き合わせる。
public class DifferentialTests
{
    [CFact]
    public void Enums_SwitchAndToString()
    {
        Backends.AssertParity("""
            using System;
            public enum Dir { North = 0, East = 1, South = 2, West = 3 }
            public class P
            {
                public static Dir Turn(Dir d) => d switch { Dir.North => Dir.East, Dir.East => Dir.South, Dir.South => Dir.West, _ => Dir.North };
                public static void Main()
                {
                    var d = Dir.North;
                    for (int i = 0; i < 5; i++) { d = Turn(d); Console.WriteLine(d.ToString()); }
                    Console.WriteLine((int)d);
                    Console.WriteLine(d == Dir.East);
                    var dict = new Dictionary<Dir, string>();
                    dict[Dir.South] = "s";
                    Console.WriteLine(dict.ContainsKey(Dir.South) + ":" + dict.ContainsKey(Dir.West));
                }
            }
            """.Replace("using System;", "using System;\nusing System.Collections.Generic;"), "P");
    }

    [CFact]
    public void Linq_WhereSelectOrderByAggregates()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public class Item { public string Name; public int Price; public Item(string n, int p) { Name = n; Price = p; } }
            public class P
            {
                public static bool Cheap(Item i) => i.Price < 50;
                public static void Main()
                {
                    var items = new List<Item> { new Item("sword", 100), new Item("potion", 10), new Item("shield", 80), new Item("arrow", 2) };
                    var names = items.Where(i => i.Price >= 50).Select(i => i.Name).ToList();
                    Console.WriteLine(string.Join(",", names));
                    Console.WriteLine(items.Select(i => i.Price).Sum());
                    Console.WriteLine(items.Min(i => i.Price) + ":" + items.Max(i => i.Price));
                    Console.WriteLine(items.OrderBy(i => i.Price).First().Name);
                    Console.WriteLine(items.OrderByDescending(i => i.Name).First().Name);
                    Console.WriteLine(items.Any(i => i.Price > 90) + ":" + items.All(i => i.Price > 5));
                    Console.WriteLine(items.Count(i => i.Price > 5) + ":" + items.Count());
                    Console.WriteLine(items.Where(Cheap).Count());
                    Console.WriteLine(items.FirstOrDefault(i => i.Price > 1000) == null);
                    Console.WriteLine(items.Last().Name + ":" + items.LastOrDefault(i => i.Price > 50).Name);
                    var prices = items.Select(i => i.Price).OrderBy(p => p).Skip(1).Take(2).ToList();
                    Console.WriteLine(prices[0] + "," + prices[1]);
                    var byName = items.ToDictionary(i => i.Name, i => i.Price);
                    Console.WriteLine(byName["shield"]);
                    var nums = new List<int> { 5, 3, 9, 1 };
                    Console.WriteLine(nums.Contains(9) + ":" + nums.IndexOf(9) + ":" + nums.IndexOf(7));
                    nums.Sort();
                    Console.WriteLine(string.Join(" ", nums.Select(n => n.ToString())));
                    nums.Sort((a, b) => b - a);
                    Console.WriteLine(string.Join(" ", nums.Select(n => n.ToString())));
                    nums.Remove(9);
                    nums.RemoveAt(0);
                    Console.WriteLine(nums.Count + ":" + nums[0]);
                    nums.Clear();
                    Console.WriteLine(nums.Count);
                    var floats = new List<float> { 1.5f, 2.25f };
                    Console.WriteLine(floats.Sum() + ":" + floats.Max());
                }
            }
            """, "P");
    }

    [CFact]
    public void Strings_RuntimeSurface()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public class P
            {
                public static void Main()
                {
                    var s = "  Hello, TinyC# World  ";
                    var t = s.Trim();
                    Console.WriteLine("[" + t + "]");
                    Console.WriteLine(t.Length);
                    Console.WriteLine(t.ToUpper() + "|" + t.ToLower());
                    Console.WriteLine(t.Contains("Tiny") + ":" + t.Contains("tiny"));
                    Console.WriteLine(t.IndexOf("o") + ":" + t.IndexOf("o", 5) + ":" + t.IndexOf("zzz"));
                    Console.WriteLine(t.StartsWith("Hello") + ":" + t.EndsWith("World") + ":" + t.EndsWith("x"));
                    Console.WriteLine(t.Replace("l", "L"));
                    Console.WriteLine(t.Substring(7) + "|" + t.Substring(0, 5));
                    var parts = "a,b,,c".Split(',');
                    Console.WriteLine(parts.Length + ":" + string.Join("/", parts));
                    var words = "one two  three".Split(' ');
                    Console.WriteLine(words.Length);
                    Console.WriteLine(string.Join("-", "x", "y", "z"));
                    Console.WriteLine(string.IsNullOrEmpty("") + ":" + string.IsNullOrEmpty("a"));
                    Console.WriteLine(t[0] + ":" + (int)t[1]);
                    var sum = 0;
                    foreach (var r in "héllo".EnumerateRunes()) sum += r.Value;
                    Console.WriteLine(sum);
                    var n = int.Parse("42") + (int)float.Parse("1.5");
                    Console.WriteLine(n);
                    var f = 3.14159f;
                    Console.WriteLine($"{f:F2}|{n,6}|{n,-4}|{n:D5}|{255:X}|{f:E2}");
                    Console.WriteLine($"{"pad",6}|{t.Length}|{(f > 3 ? "big" : "small")}");
                    var sb = "";
                    for (int i = 0; i < 3; i++) sb += i.ToString() + ";";
                    Console.WriteLine(sb);
                    Console.WriteLine("abc" == "ab" + "c");
                    Console.WriteLine(1.5f.ToString() + "|" + true.ToString() + "|" + 7.ToString());
                }
            }
            """, "P");
    }

    [CFact]
    public void Dictionaries_AllOperations()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public class P
            {
                public static void Main()
                {
                    var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
                    d["c"] = 3;
                    d.Add("d", 4);
                    d["a"] = 10;
                    Console.WriteLine(d.Count + ":" + d["a"] + ":" + d.ContainsKey("zz"));
                    if (d.TryGetValue("b", out var b)) Console.WriteLine("b=" + b);
                    if (!d.TryGetValue("q", out var q)) Console.WriteLine("q=" + q);
                    Console.WriteLine(d.Remove("b") + ":" + d.Remove("b") + ":" + d.Count);
                    var total = 0;
                    foreach (var kv in d) total += kv.Value + kv.Key.Length;
                    Console.WriteLine(total);
                    var keys = new List<string>();
                    foreach (var k in d.Keys) keys.Add(k);
                    keys.Sort();
                    Console.WriteLine(string.Join(",", keys));
                    var values = new List<int>();
                    foreach (var v in d.Values) values.Add(v);
                    values.Sort();
                    Console.WriteLine(values.Sum() + ":" + values.Count);
                    var byId = new Dictionary<int, string>();
                    for (int i = 0; i < 100; i++) byId[i * 7] = "v" + i.ToString();
                    Console.WriteLine(byId.Count + ":" + byId[70] + ":" + byId.ContainsKey(71));
                    var hits = 0;
                    for (int i = 0; i < 700; i++) if (byId.ContainsKey(i)) hits++;
                    Console.WriteLine(hits);
                }
            }
            """, "P");
    }

    [CFact]
    public void Math_Facade()
    {
        Backends.AssertParity("""
            using System;
            public class P
            {
                public static void Main()
                {
                    Console.WriteLine(Math.Max(3, 7) + ":" + Math.Min(3, 7) + ":" + Math.Abs(-5) + ":" + Math.Sign(-2) + ":" + Math.Clamp(15, 0, 10));
                    Console.WriteLine(Math.Max(1.5f, 2f) + ":" + Math.Abs(-1.25f) + ":" + Math.Clamp(0.5f, 1f, 2f));
                    Console.WriteLine(Math.Floor(2.7f) + ":" + Math.Ceiling(2.1f) + ":" + Math.Round(2.5f) + ":" + Math.Round(3.5f) + ":" + Math.Round(2.345f, 2));
                    Console.WriteLine(Math.Sqrt(16f) + ":" + Math.Pow(2f, 10f) + ":" + Math.Exp(0f) + ":" + Math.Log(1f));
                    Console.WriteLine((int)(Math.Sin(0f) * 100f) + ":" + (int)(Math.Cos(0f) * 100f) + ":" + (int)(Math.Atan2(1f, 1f) * 1000f));
                    Console.WriteLine((int)(Math.PI * 1000f));
                    Console.WriteLine((int)2.7f + ":" + (int)-2.7f + ":" + (int)(7f / 2f));
                    Console.WriteLine(7 / 2 + ":" + -7 / 2 + ":" + 7 % 3 + ":" + -7 % 3 + ":" + 7.5f % 2f);
                    // 負数の >> は Lua が論理シフト (support-matrix の既知差異) なので正数のみ
                    Console.WriteLine((5 << 2) + ":" + (16 >> 2) + ":" + (6 & 3) + ":" + (6 | 3) + ":" + (6 ^ 3) + ":" + ~5);
                    int big = 2147483647;
                    Console.WriteLine(big + 1);
                    Console.WriteLine(1f / 3f);
                    Console.WriteLine(100000000f + 1f);
                }
            }
            """, "P");
    }

    [CFact]
    public void Classes_InheritanceVirtualAndTypeTests()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public class Shape
            {
                public string Name;
                public static int Created;
                public Shape(string name) { Name = name; Created = Created + 1; }
                public virtual float Area() => 0f;
                public virtual string Describe() => Name + ":" + Area().ToString();
            }
            public class Rect : Shape
            {
                public float W; public float H;
                public Rect(float w, float h) : base("rect") { W = w; H = h; }
                public override float Area() => W * H;
            }
            public class Square : Rect
            {
                public Square(float s) : base(s, s) { Name = "square"; }
                public override string Describe() => "sq:" + base.Describe();
            }
            public class Circle : Shape
            {
                public float R;
                public Circle(float r) : base("circle") { R = r; }
                public override float Area() => 3f * R * R;
            }
            public class P
            {
                public static void Main()
                {
                    var shapes = new List<Shape> { new Rect(2f, 3f), new Square(2f), new Circle(1f), new Shape("blob") };
                    foreach (var s in shapes) Console.WriteLine(s.Describe());
                    var rects = 0; var squares = 0;
                    foreach (var s in shapes)
                    {
                        if (s is Rect) rects++;
                        if (s is Square sq) { squares++; Console.WriteLine(sq.W); }
                        if (s is Circle c) Console.WriteLine("r=" + c.R);
                    }
                    Console.WriteLine(rects + ":" + squares + ":" + Shape.Created);
                    Shape nothing = null;
                    Console.WriteLine(nothing is Shape);
                    Console.WriteLine(nothing == null);
                }
            }
            """, "P");
    }

    [CFact]
    public void Closures_CaptureAndFuncLists()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public class Counter
            {
                public int N;
                public Func<int> Make(int step) { return () => { N = N + step; return N; }; }
            }
            public class P
            {
                public static int Twice(int x) => x * 2;
                public static void Main()
                {
                    var c = new Counter();
                    var inc = c.Make(3);
                    inc(); inc();
                    Console.WriteLine(c.N + ":" + inc());
                    var fs = new List<Func<int, int>>();
                    for (int i = 0; i < 3; i++) { var k = i; fs.Add(x => x + k); }
                    fs.Add(Twice);
                    var total = 0;
                    foreach (var f in fs) total += f(10);
                    Console.WriteLine(total);
                    Action<string> log = m => Console.WriteLine("log:" + m);
                    log("hi");
                    var acc = 0;
                    Action add = () => { acc += 5; };
                    add(); add();
                    Console.WriteLine(acc);
                    Func<int, int, int> op = (a, b) => a * b + acc;
                    Console.WriteLine(op(3, 4));
                }
            }
            """, "P");
    }

    [CFact]
    public void Structs_ValueSemanticsInArraysAndLists()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public struct Vec { public float X; public float Y; }
            public struct Body { public Vec Pos; public Vec Vel; public int Id; public string Tag; }
            public class P
            {
                public static Vec Add(Vec a, Vec b) { var r = new Vec(); r.X = a.X + b.X; r.Y = a.Y + b.Y; return r; }
                public static void Main()
                {
                    var bodies = new Body[3];
                    for (int i = 0; i < 3; i++)
                    {
                        var b = new Body();
                        b.Id = i; b.Tag = "b" + i.ToString();
                        b.Pos.X = i; b.Vel.Y = 1.5f;
                        bodies[i] = b;
                    }
                    for (int step = 0; step < 4; step++)
                        for (int i = 0; i < 3; i++)
                        {
                            var moved = Add(bodies[i].Pos, bodies[i].Vel);
                            bodies[i].Pos = moved;
                        }
                    var copy = bodies[2];
                    copy.Pos.X = 99f;
                    Console.WriteLine(bodies[2].Pos.X + ":" + bodies[2].Pos.Y + ":" + copy.Pos.X + ":" + bodies[2].Tag);
                    var list = new List<Vec>();
                    var v = new Vec(); v.X = 1f;
                    list.Add(v);
                    v.X = 2f;
                    list.Add(v);
                    Console.WriteLine(list[0].X + ":" + list[1].X + ":" + list.Count);
                }
            }
            """, "P");
    }

    [CFact]
    public void ControlFlow_LoopsAndStatics()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public class Config { public static int Limit = 10; public static string Label { get; set; } = "cfg"; public static List<int> Seen = new List<int>(); }
            public class P
            {
                public static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
                public static void Main()
                {
                    var i = 0;
                    while (true) { i++; if (i % 2 == 0) continue; if (i > Config.Limit) break; Config.Seen.Add(i); }
                    Console.WriteLine(string.Join(",", Config.Seen.Select(x => x.ToString())));
                    var j = 0;
                    do { j += 3; } while (j < 10);
                    Console.WriteLine(j);
                    var pairs = "";
                    for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) { if (b == a) continue; if (a + b > 3) break; pairs += a.ToString() + b.ToString() + " "; }
                    Console.WriteLine(pairs);
                    Config.Label = Config.Label + "!";
                    Console.WriteLine(Config.Label + ":" + Fib(15));
                    var grid = new List<List<int>>();
                    for (int r = 0; r < 3; r++) { var row = new List<int>(); for (int c = 0; c < 3; c++) row.Add(r * c); grid.Add(row); }
                    var sum = 0;
                    foreach (var row in grid) foreach (var cell in row) sum += cell;
                    Console.WriteLine(sum + ":" + grid[2][2]);
                    var sw = i switch { 11 => "eleven", 12 => "twelve", _ => "other" };
                    Console.WriteLine(sw);
                    string maybe = null;
                    Console.WriteLine((maybe ?? "fallback") + ":" + (maybe == null));
                    Console.WriteLine(Config.Seen.Count > 3 && Config.Seen[0] == 1 ? "yes" : "no");
                }
            }
            """.Replace("using System.Collections.Generic;", "using System.Collections.Generic;\nusing System.Linq;"), "P");
    }

    [CFact]
    public void DelegateFields_AndNumericTryParse()
    {
        Backends.AssertParity("""
            using System;
            public class Btn
            {
                public Action OnClick;
                public Func<int, int> Map { get; set; }
                public static Func<string> Describe;
                public void Fire() { OnClick(); }
                public string Name() { return Describe(); }
            }
            public class P
            {
                public static void Main()
                {
                    var b = new Btn();
                    var n = 0;
                    b.OnClick = () => { n++; };
                    b.Map = x => x * 2;
                    Btn.Describe = () => "btn";
                    b.Fire();
                    b.OnClick();
                    Console.WriteLine(n + ":" + b.Map(21) + ":" + Btn.Describe() + ":" + b.Name());
                    var s = 0;
                    if (int.TryParse("42", out var a)) s += a;
                    if (!int.TryParse("4x2", out var bad)) s += 1000 + bad;
                    int c;
                    var okc = int.TryParse(" 7 ", out c);
                    s += okc ? c : -1;
                    if (float.TryParse("1.5", out var f)) s += (int)(f * 2f);
                    if (!float.TryParse("abc", out var g)) s += 100 + (int)g;
                    Console.WriteLine(s);
                }
            }
            """, "P");
    }

    [CFact]
    public void Records_EqualityWithInheritanceAndDeconstruction()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public record Pt(int X, int Y)
            {
                public int Manhattan() => (X < 0 ? -X : X) + (Y < 0 ? -Y : Y);
            }
            public record Named(string Name, Pt Pos);
            public record Shape(string Kind);
            public record Circle(string Kind, float R) : Shape(Kind);
            public class P
            {
                public static void Main()
                {
                    var a = new Pt(1, 2);
                    var b = new Pt(1, 2);
                    var c = new Pt(2, 1);
                    Pt none = null;
                    Console.WriteLine((a == b) + ":" + (a != b) + ":" + (a == c) + ":" + (a == none) + ":" + (none == null));
                    var d = a with { X = 5 };
                    Console.WriteLine(d.X + "," + d.Y + ":" + a.X + ":" + (d == a) + ":" + d.Manhattan());
                    var n1 = new Named("p", new Pt(3, 4));
                    var n2 = new Named("p", new Pt(3, 4));
                    var n3 = n2 with { Pos = new Pt(0, 0) };
                    Console.WriteLine((n1 == n2) + ":" + (n1 == n3) + ":" + n3.Pos.X + ":" + n1.Pos.Manhattan());
                    Shape s1 = new Circle("c", 1.5f);
                    Shape s2 = new Circle("c", 1.5f);
                    Shape s3 = new Shape("c");
                    var s4 = s1 with { Kind = "k" };
                    Console.WriteLine((s1 == s2) + ":" + (s1 == s3) + ":" + (s4 is Circle) + ":" + s4.Kind + ":" + ((Circle)s4).R);
                    var (px, py) = c;
                    Console.WriteLine(px + ":" + py);
                    var pts = new List<Pt> { a, b, c, d };
                    Console.WriteLine(pts.Count(p => p == a) + ":" + pts.Contains(new Pt(2, 1)) + ":" + pts.IndexOf(new Pt(5, 2)));
                    var byName = new Dictionary<string, Pt> { ["a"] = a };
                    Console.WriteLine(byName["a"] == b);
                    Console.WriteLine(pts.Sum(p => p.X));
                }
            }
            """, "P");
    }

    [CFact]
    public void Nullable_LiftedOperatorsAndMembers()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public struct Vec { public int X; public int Y; }
            public class Slot { public int? Hp; public float? Speed; public bool? Flag; public Vec? Pos; public static int? Counter; }
            public class P
            {
                public static int Calls;
                public static int Side() { Calls++; return 7; }
                public static string Show(int? v) => v.HasValue ? "v=" + v.Value.ToString() : "none";
                public static int? Pick(bool take) => take ? 5 : null;
                public static void Main()
                {
                    int? a = 10;
                    int? n = null;
                    int b = 3;
                    Console.WriteLine(Show(a) + "|" + Show(n) + "|" + Show(Pick(true)) + "|" + Show(Pick(false)));
                    Console.WriteLine((a == null) + ":" + (n == null) + ":" + (a != null) + ":" + (n != null) + ":" + (n is null) + ":" + (a is not null));
                    Console.WriteLine((a ?? 0) + ":" + (n ?? -1) + ":" + n.GetValueOrDefault() + ":" + n.GetValueOrDefault(9) + ":" + a.GetValueOrDefault(9));
                    int? s1 = a + b;
                    int? s2 = n + b;
                    int? s3 = a * a - b;
                    int? s4 = a / 3;
                    int? s5 = a % 3;
                    int? s6 = -a;
                    int? s7 = n * 2;
                    Console.WriteLine(Show(s1) + "|" + Show(s2) + "|" + Show(s3) + "|" + Show(s4) + "|" + Show(s5) + "|" + Show(s6) + "|" + Show(s7));
                    Console.WriteLine((a == 10) + ":" + (n == 10) + ":" + (a != n) + ":" + (n == n) + ":" + (a < 20) + ":" + (n < 20) + ":" + (a >= 10) + ":" + (n >= 0) + ":" + (a > n));
                    float? f = 1.5f;
                    float? g = null;
                    Console.WriteLine((f * 2f) + "|" + (g * 2f) + "|" + (f + a) + "|" + (f / 4f) + "|" + (f % 1f) + "|" + (f == 1.5f) + "|" + (g == null));
                    bool? t = true;
                    bool? u = false;
                    bool? v = null;
                    Console.WriteLine((t & v) + "|" + (u & v) + "|" + (t | v) + "|" + (u | v) + "|" + (v & v) + "|" + (!v) + "|" + (!t) + "|" + (t ^ u) + "|" + (t ^ v));
                    int? c = 1;
                    c++;
                    c += 5;
                    c = c << 2;
                    c--;
                    n++;
                    n += 1;
                    Console.WriteLine(Show(c) + "|" + Show(n) + "|" + Show(c & 6) + "|" + Show(c | 1) + "|" + Show(~c) + "|" + Show(c ^ 3));
                    var sl = new Slot();
                    Console.WriteLine(Show(sl.Hp) + "|" + sl.Speed.HasValue + "|" + (sl.Flag ?? true) + "|" + sl.Pos.HasValue);
                    sl.Hp = 4;
                    var pos = new Vec(); pos.X = 2; pos.Y = 3;
                    sl.Pos = pos;
                    Slot.Counter = sl.Hp * 2;
                    Console.WriteLine(Show(sl.Hp) + "|" + sl.Pos.Value.X + "," + sl.Pos.Value.Y + "|" + Show(Slot.Counter) + "|" + (sl.Pos == null));
                    sl.Hp = null;
                    Console.WriteLine(Show(sl.Hp) + "|" + $"[{sl.Hp}][{a}][{f}][{g}][{t}][{v}]" + "|" + ("x" + n + a));
                    Calls = 0;
                    var w1 = a ?? Side();
                    var w2 = n ?? Side();
                    var w3 = a.GetValueOrDefault(Side());
                    Console.WriteLine(w1 + ":" + w2 + ":" + w3 + ":" + Calls);
                    var list = new List<int?> { 1, 3 };
                    list.Add(Pick(true));
                    var sum = 0;
                    foreach (var item in list) sum += item ?? 100;
                    Console.WriteLine(sum + ":" + list.Count + ":" + (list[1] == 3));
                    var dict = new Dictionary<string, int?>();
                    dict["b"] = 2;
                    Console.WriteLine(Show(dict["b"]) + ":" + (dict["b"] ?? 0));
                    int? sw = 2;
                    var label = sw switch { 1 => "one", 2 => "two", null => "nil", _ => "other" };
                    Console.WriteLine(label);
                    Console.WriteLine((a ?? 0) + (s1 ?? 0));
                }
            }
            """, "P");
    }

    [CFact]
    public void Random_SeededSequencesMatchLua()
    {
        // Lua 5.5 の xoshiro256** (LUA_32BITS 構成) を C に移植した合意 PRNG。
        // seed 固定で Next / Next(max) / Next(min, max) / Range / NextFloat が
        // bit 一致する
        Backends.AssertParity("""
            using TinySystem;
            public class P
            {
                public static void Main()
                {
                    Random.Seed(12345);
                    var s = "";
                    for (int i = 0; i < 6; i++) s += Random.Next(100) + ",";
                    System.Console.WriteLine(s);
                    s = "";
                    for (int i = 0; i < 6; i++) s += Random.Range(-5, 5) + ",";
                    System.Console.WriteLine(s);
                    s = "";
                    for (int i = 0; i < 4; i++) s += Random.NextFloat() + ",";
                    System.Console.WriteLine(s);
                    s = "";
                    for (int i = 0; i < 4; i++) s += Random.Next(1000, 2000) + "," + Random.Next() % 1000 + ";";
                    System.Console.WriteLine(s);
                    Random.Seed(-7);
                    System.Console.WriteLine(Random.Next(1 << 30) + ":" + Random.Next(3) + ":" + Random.Range(0, 0) + ":" + Random.Next(2147483647));
                    Random.Seed(12345);
                    System.Console.WriteLine(Random.Next(100));
                }
            }
            """, "P");
    }

    // struct / record struct の member (il-spec §10): 明示 ctor + field
    // initializer (`new S()` は zero のまま)、変数 receiver (local / 配列要素
    // / class field) の method 変異はその場に残り、List indexer (rvalue) は
    // copy への変異、custom / auto / expression-bodied property、record
    // struct の positional ctor / ==/!= / with / 入れ子、List.Contains /
    // IndexOf の値等価、`new S[n]` の zero 要素、default(S)
    [CFact]
    public void Structs_MembersCtorPropertiesAndRecordStruct()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public struct Vec
            {
                public float X;
                public float Y;
                public Vec(float x, float y) { X = x; Y = y; }
                public void Scale(float k) { X = X * k; Y = Y * k; }
                public float Len2() => X * X + Y * Y;
                public Vec Plus(Vec o) { var r = new Vec(X + o.X, Y + o.Y); return r; }
            }
            public struct Counter
            {
                public int N;
                public int Step = 2;
                public string Tag { get; set; }
                public int Doubled => N * 2;
                public int Clamped
                {
                    get { return N > 10 ? 10 : N; }
                    set { N = value < 0 ? 0 : value; }
                }
                public Counter(int n) { N = n; Tag = "c" + n.ToString(); }
                public void Inc() { N = N + Step; }
                public void Reset() { if (N == 0) return; N = 0; }
            }
            public struct Body
            {
                public Vec Pos;
                public Vec Vel;
                public void Step() { Pos = Pos.Plus(Vel); Vel.Scale(0.5f); }
            }
            public record struct Point(int X, int Y)
            {
                public int Manhattan() => (X < 0 ? -X : X) + (Y < 0 ? -Y : Y);
            }
            public readonly record struct Pair(Point A, string Name);
            public class Holder { public Counter C; public Vec V = new Vec(1f, 2f); }
            public class P
            {
                public static Counter Make(int n) { var c = new Counter(n); c.Inc(); return c; }
                public static void Main()
                {
                    var v = new Vec(3f, 4f);
                    v.Scale(2f);
                    Console.WriteLine(v.X + ":" + v.Y + ":" + v.Len2());
                    var arr = new Vec[2];
                    arr[1] = v;
                    arr[1].Scale(0.5f);
                    Console.WriteLine(arr[1].X + ":" + arr[0].X + ":" + v.X);
                    var list = new List<Vec>();
                    list.Add(v);
                    list[0].Scale(10f);
                    Console.WriteLine(list[0].X);
                    var c = new Counter(5);
                    c.Inc();
                    Console.WriteLine(c.N + ":" + c.Step + ":" + c.Tag + ":" + c.Doubled + ":" + c.Clamped);
                    c.Clamped = -3;
                    Console.WriteLine(c.N + ":" + c.Clamped);
                    c.N = 50; Console.WriteLine(c.Clamped);
                    var z = new Counter();
                    Console.WriteLine(z.N + ":" + z.Step + ":" + (z.Tag == null));
                    z.Reset(); z.Inc(); Console.WriteLine(z.N);
                    var h = new Holder();
                    h.C.Inc(); h.C.Inc();
                    h.V.Scale(3f);
                    Console.WriteLine(h.C.N + ":" + h.V.X + ":" + h.V.Y);
                    var m = Make(1);
                    Console.WriteLine(m.N + ":" + m.Tag);
                    var b = new Body();
                    b.Vel = new Vec(2f, 2f);
                    b.Step(); b.Step();
                    Console.WriteLine(b.Pos.X + ":" + b.Vel.X);
                    var p = new Point(3, -4);
                    var q = p with { Y = 4 };
                    Console.WriteLine(p.Manhattan() + ":" + q.Manhattan() + ":" + (p == q) + ":" + (p == new Point(3, -4)) + ":" + (p != q));
                    var pair = new Pair(p, "a");
                    var pair2 = pair with { Name = "a" };
                    Console.WriteLine((pair == pair2) + ":" + (pair == new Pair(q, "a")) + ":" + pair.A.X + ":" + pair2.Name);
                    var d = new Dictionary<string, Point>();
                    d["k"] = p;
                    Console.WriteLine(d["k"].Manhattan() + ":" + d["k"].X);
                    var init = new Counter(1) { N = 7, Tag = "t" };
                    Console.WriteLine(init.N + ":" + init.Tag + ":" + init.Step);
                    var pts = new List<Point> { new Point(1, 1), new Point(2, 2) };
                    Console.WriteLine(pts.Contains(new Point(2, 2)) + ":" + pts.IndexOf(new Point(1, 1)));
                    var dp = default(Point);
                    Console.WriteLine(dp.X + ":" + (dp == new Point(0, 0)));
                }
            }
            """, "P");
    }

    // `?.` (il-spec §3): `S?` receiver は HasValue / Value の明示ノード
    // (method は .Value の copy に対して呼ぶ)、参照型 receiver の値型 member は
    // T? に wrap、nested `?.`、`??=`、bool? の文字列化
    [CFact]
    public void Nullable_ConditionalAccessAndCoalesceAssign()
    {
        Backends.AssertParity("""
            using System;
            using System.Collections.Generic;
            public struct Vec { public int X; public int Y; public int Sum() => X + Y; public void Bump() { X = X + 100; } public int Twice => X * 2; }
            public readonly record struct Ro(int A) { public int Dbl() => A * 2; }
            public class Node { public int Hp; public int? Mana; public Node Next; public string Name = "n"; public Vec Pos; public int Len() => Name.Length; public Node Self() => this; }
            public class P
            {
                public static string Show(int? v) => v.HasValue ? "v=" + v.Value.ToString() : "none";
                public static void Main()
                {
                    Vec? v = new Vec { X = 1, Y = 2 };
                    Vec? nv = null;
                    int? a = v?.X;
                    int? b = nv?.X;
                    int? c = v?.Sum();
                    int? d = nv?.Sum();
                    int? t = v?.Twice;
                    v?.Bump();
                    Console.WriteLine(Show(a) + "|" + Show(b) + "|" + Show(c) + "|" + Show(d) + "|" + Show(t) + "|" + Show(v?.X));
                    Node n = new Node { Hp = 3 };
                    n.Pos.X = 9;
                    Node nn = null;
                    int? e = n?.Hp;
                    int? f = nn?.Hp;
                    int? g = n?.Len();
                    string h = nn?.Name;
                    string h2 = n?.Name;
                    int? m = n?.Mana;
                    int? k = n?.Next?.Hp;
                    int? k2 = n?.Self()?.Hp;
                    int? px = n?.Pos.X;
                    Vec? pv = nn?.Pos;
                    Console.WriteLine(Show(e) + "|" + Show(f) + "|" + Show(g) + "|" + (h == null) + "|" + h2 + "|" + Show(m) + "|" + Show(k) + "|" + Show(k2) + "|" + Show(px) + "|" + pv.HasValue);
                    int? q = null; q ??= 4;
                    int? r = 9; r ??= 1;
                    bool? bq = null;
                    bool? bt = true;
                    Ro? ro = new Ro(5);
                    Console.WriteLine(Show(q) + "|" + Show(r) + "|" + bq + "|" + bt + "|" + Show(ro?.Dbl()) + "|" + (nv?.Sum() == null) + "|" + (v?.Sum() > 2));
                    var list = new List<int?> { 1, 2 };
                    Console.WriteLine(Show(list?.Count));
                }
            }
            """, "P");
    }
}
