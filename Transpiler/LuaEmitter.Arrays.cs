using Microsoft.CodeAnalysis;

namespace TinyCs;

// 固定長配列 (il-spec §11) の Lua 表現。配列は長さ field `n` を持つ table
// (`table.pack` と同じ流儀) で、null 要素 (nil 穴) があっても Length と
// foreach が宣言した長さを保つ。生成は __tcs_newarr (chunk header / module
// mode は runtime の TinySystem.newarr)、リテラルは `{..., n = k}`。
// IL 経路 (IlNewArray / IlTable.Array / IlLen.Array / IlForeachList.Array) と
// legacy 経路の両方がここを使う。
public partial class LuaEmitter
{
    // new T[n] の要素 default を __tcs_newarr の第 2 引数に写す。source struct の
    // default は `S.new()` だが、配列要素は互いに独立した place (il-spec §10)
    // なので 1 つの値を共有せず、要素ごとに作る factory `S.new` を渡す
    private static string ArrayElementDefaultArg(ITypeSymbol? elementType)
    {
        var value = GetDefaultValueForType(elementType);
        return value.EndsWith(".new()", StringComparison.Ordinal)
            ? value[..^2] : value;
    }

    // 配列リテラルの Lua table: 要素に長さ field `n` を添える (null 要素の
    // nil 穴があっても Length / foreach が崩れない、il-spec §11)
    private static string ArrayLiteralLua(IReadOnlyList<string> items) =>
        items.Count == 0 ? "{n = 0}"
            : $"{{{string.Join(", ", items)}, n = {items.Count}}}";

    // property pattern `{ Length: n }` の配列 Length (System.Array.Length)
    private static bool IsArrayLength(ISymbol? symbol) =>
        symbol is IPropertySymbol
        {
            Name: "Length",
            ContainingType.SpecialType: SpecialType.System_Array
        };

    // 配列の長さ field を読む式。table リテラルは prefix 式でないので括弧で包む
    private static string ArrayLengthLua(string array) =>
        array.StartsWith('{') ? $"({array}).n" : $"{array}.n";

    // 配列の foreach (il-spec §11): 長さ field `n` までの数値 for。ipairs は
    // null 要素 (nil 穴) で止まるため使わない。collection は C# と同じく
    // 1 回だけ評価する。head の後に body と continue label を出し tail で閉じる
    private void EmitArrayForeachHead(string varName, string collection)
    {
        AppendLine("do");
        _indent++;
        AppendLine($"local __tcs_arr = {collection}");
        AppendLine("for __tcs_i = 1, __tcs_arr.n do");
        _indent++;
        AppendLine($"local {varName} = __tcs_arr[__tcs_i]");
    }

    private void EmitArrayForeachTail()
    {
        _indent--;
        AppendLine("end");
        _indent--;
        AppendLine("end");
    }
}
