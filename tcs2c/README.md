# tcs2c — TinyC# IL→C release backend

`tcs2c` は TinyC# source を `IlExport.Export` へ渡し、release 用の GNU C source
を生成する .NET 10 console application。class は type id 付き `calloc` struct、
array と `List<int>` / `List<float>` は型付き連続 buffer へ lower する。

## 使い方

```sh
dotnet run --project tcs2c -- ../tcs/samples/collision.cs -o collision.c
gcc -O2 -ffp-contract=off -fwrapv -fexcess-precision=standard \
  collision.c -o collision -lm
./collision
```

`-lm` は Math (sqrtf / powf 等) を使う入力に必要。

単一の `static void Main()` があれば生成 executable の entry にする。複数ある
場合は `--entry CLASS` で選ぶ。class library sample のように `Main` が無い入力も
全 method を C へ変換し、static initializer だけを実行する no-op entry を付ける。

## 対応面

IL 契約 (`IlExport`) に載る TinyC# は原則すべて C へ落とす。意味論の正本は
Lua backend (dev) で、`tcs2c.Tests` が同じ source の stdout 一致を要求する。

- 型: `int` / `float` / `bool` / `string` (immutable byte 列、literal は static
  object) / `char` (Lua と同じ 1 文字 string) / enum (整数定数、`ToString` も
  整数表記) / class 参照 / データ struct (C 値型) / `T[]` (inline 要素の固定長
  配列) / `List<T>` (growable buffer) / `Dictionary<int|string, V>` (chained
  hash) / `Action` `Func` (closure = lifted 関数 + 捕捉 cell)
- class: 継承 (prefix layout)、virtual dispatch (type id switch)、`base.M`、
  ctor 連鎖、`is T` / is-pattern、static field / property、auto property
  initializer、upcast、明示 downcast (`IlCast`: 実行時型が合わなければ fault)
- struct / record struct: 素の C 値型 (配列 / List / field に inline、代入 =
  値 copy)。instance method / property accessor は `Tcs_S *self` で格納場所を
  直接指す (変数 receiver はコピーゼロ、rvalue receiver は C# と同じく一時値)、
  explicit / positional ctor は zero 値 → initializer → 本文、record struct の
  `==` / with、List.Contains / IndexOf / Remove の memberwise 等価、
  `new S[n]` / `default(S)` の zero 値
- record class: positional ctor、`==` / `!=` の構造等価 (型ごとの比較関数、
  継承は実行時型へ dispatch、string は内容、record は再帰、struct は
  memberwise)、`with` (実行時型の layout で shallow copy)、分解代入、
  List.Contains / IndexOf / Remove も構造等価
- 式・文: 数値演算 (i32 wrap、f32 strict、`/` は Lua と同じ float 除算、
  `(int)f` は 0 方向 truncation)、文字列連結・補間 (`string.format`:
  `%d %s %f %e %g %x %c`、幅・精度・`-`/`0` flag)、三項・`??`・switch 式、
  while / do-while / for / foreach (List / array / Dictionary /
  `EnumerateRunes`)、break / continue、IIFE (GNU statement expression)
- runtime 表面 (`runtime/tinysystem.lua` と同じ意味論):
  - List: Add / Count / index / Remove / RemoveAt / Clear / Contains / IndexOf /
    Sort (自然順・comparison) / Where / Select / Any / All / First / Last /
    FirstOrDefault / LastOrDefault / Count(pred) / Sum / Min / Max / OrderBy /
    OrderByDescending / Take / Skip / ToList / ToDictionary (要素型ごとの
    inline loop。sort は安定 merge sort)
  - Dictionary: index get / set (indexer initializer 含む)、Add、ContainsKey、
    TryGetValue、Remove、Count、Keys / Values、foreach (KeyValuePair)
  - String: Length / index / Contains / IndexOf / Replace / StartsWith /
    EndsWith / Trim / Substring / Split / Join / IsNullOrEmpty / ToUpper /
    ToLower、`int.Parse` / `float.Parse`
  - Math (`Math` / `MathF`): Min / Max / Abs / Sign / Clamp / Floor / Ceiling /
    Round / Sqrt / Sin / Cos / Tan / Atan2 / Pow / Exp / Log / PI
  - Random: `new Random()` / `new Random(seed)` (GC object) / `Random.Shared` /
    Seed、instance の Next / Next(max) / Next(min, max) / NextFloat / NextSingle
    / Range
    (Lua 5.5 の xoshiro256** を LUA_32BITS 構成のまま移植。seed 固定時に
    Lua backend と bit 一致)
  - Console.WriteLine / Write、`Environment.GetEnvironmentVariable`
- fault: null / bounds / 0 除算 / key-not-found / 空列 First 等は
  `tcs_fault(kind)` で stderr へ出して exit 1 (il-spec §12)

明示エラー (未対応): `object` 型の local、
`StringBuilder`、char の算術 / `CompareTo`、Lua 固有の `IlIsLuaType`。Lua backend 側の既知差異は
参照型要素の `new T[n]` (要素 nil / `.Length` 0) のみ (support-matrix 参照)。

通常の `print` は stdout へ値を出す。digest kernel 回帰用だけは
`--digest-f32` を付け、各 f32 の bit 列を FNV-1a へ直接投入する。

```sh
TCS_ROOT=../tcs bash tcs2c/verify-digests.sh
```

## GC

生成 C は自前の mark-sweep GC (非移動) を runtime prelude に含む。

- **heap は精密**: 全 heap object は `TcsGcHeader` (kind + `TcsLayout`) を
  持つ。生成側が class / struct ごとに pointer slot の byte offset 表
  (`tcs_layout_C_<Class>` / `tcs_layout_S_<Struct>`、struct-in-struct は
  `offsetof` 加算で平坦化) を出し、array / List / Dictionary / closure cell
  は確保時に要素 layout を受け取る。int / float field を pointer と誤認
  することはない
- **root は static field (精密) と C stack (保守的)**: static は生成関数
  `tcs_gc_mark_statics` が走査する。stack は entry (`main` / `tcs_lib_*`)
  で記録した frame から現在の frame までを word 単位で走査し、`setjmp` で
  callee-saved register も stack に落とす。heap object の内部を指す
  interior pointer (`tcs_array_at` の要素 pointer 等) も object を生かす
- **トリガ**: 直近 GC 以降の確保 bytes が threshold (直近 GC 後の生存
  bytes、下限 `TCS_GC_MIN_THRESHOLD` = 1 MiB) に達した確保で full GC
- **string literal は static object** (`TCS_GC_STATIC`): 評価ごとの確保を
  しない。GC は static object を mark / sweep の対象外にする
- **検証**: `-DTCS_GC_STRESS=N` (N ≥ 1) で N 回の確保ごとに full GC を回す
  (`N=1` で毎回)。root 漏れは生きている object が回収されて出力が変わる
  形で現れるので、`tcs2c.Tests` は C (通常 + stress) と Lua の stdout 一致を
  要求する。AddressSanitizer と併用するときは保守的走査が fake stack を
  見ないよう `ASAN_OPTIONS=detect_stack_use_after_return=0` を付ける
- `--lib` では `tcs_lib_gc()` を公開し、host が frame 境界などで明示的に
  full GC を回せる。entry (`tcs_entry_<Class>_<Method>`) の外では GC は
  走らない (stack の底が未記録)

保守的 stack 走査は gcc / clang の最適化 (-O2) と setjmp を前提にした
一般的な方式 (Boehm GC と同じ仮定) で、pointer を隠す変換 (XOR 等) を
しない通常の C コード生成に対して安全。runtime は単一 thread 前提で、
`--lib` の host は tcs の object pointer を entry の外で保持しない
(保持したければ static field に置く)。

## テスト

```sh
dotnet test tcs2c.Tests        # C compiler (gcc / cc / clang) が無ければ skip
```

`tcs2c.Tests` は同じ TinyC# source を tcs2c→C→cc と tcs→Lua→lua32 で実行し
stdout を突き合わせる 2 backend differential (GC stress 込み) と、GC 固有の
テスト (到達可能 object の保持 / garbage の回収 / `--lib` 境界) を持つ。

## IlExport 契約

型と初期化情報は T228 契約 (`IlMethodInfo.ReturnType` / `ParameterTypes`、
`IlFieldInfo.Init`、`IlNewArray.ElementType` / `Length`、`IlTable.ElementType`) だけ
から読む。第一 milestone の `SourceFacts` / Roslyn 再解析 bridge は廃止した。

現契約には constructor 本文と local の宣言型が無い。そのためこの slice は、
initializer 付き local の型を IL から推論し、引数付き `IlNewObj` は引数を宣言順の
instance field へ代入する positional constructor に限定する（0 引数 default
constructor も可）。initializer 無し local、positional で表せない constructor、
method overload、継承、List の int/float 以外は対象を含む明示 error で拒否する。

生成 C は GNU statement expression で operand / argument の左→右評価を固定する。
strict f32 build では `-ffp-contract=off`、`-fwrapv`、
`-fexcess-precision=standard` を必須とする。
