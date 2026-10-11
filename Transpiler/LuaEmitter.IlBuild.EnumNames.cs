using Microsoft.CodeAnalysis;

namespace TinyCs;

// enum 値の文字列化 (ToString / 文字列連結 / 補間 / Console.WriteLine)。.NET の
// Enum.ToString と同じく、定義された値は member 名、[Flags] は名前の ", " 連結、
// どちらでも表せない値は数値にする。名前の表は文字列化する地点にだけ置く
// (`String.FormatEnum(v, spec)` の第 2 引数。il-spec §13)
public partial class LuaEmitter
{
    // 値が enum か enum の T? なら String.FormatEnum、それ以外は null
    private static IlExpr? WrapEnumToString(ITypeSymbol? type, IlExpr value) =>
        EnumFormatSpec(UnwrapNullable(type)) is { } spec
            ? new IlCall("String.FormatEnum",
                [value, new IlLit(ModuleArtifactText.EscapeLuaString(spec))])
            : null;

    // spec は `名前:値` を `,` で並べた列。Flags は先頭に `*` を付け、全 member を
    // .NET の EnumInfo と同じ並び (値の符号なし昇順。宣言順で既に整列していれば
    // そのまま、でなければ Array.Sort) で持つ。Flags でない enum は値ごとに
    // .NET が選ぶ名前を 1 つだけ持つ (同じ値の member は並びの上で 32 個以下なら
    // 最初、それより多ければ二分探索で当たったもの)
    internal static string? EnumFormatSpec(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            || enumType.EnumUnderlyingType is not { } underlying)
            return null;
        var fields = enumType.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue).ToArray();
        var names = fields.Select(f => f.Name).ToArray();
        var values = fields.Select(f => Convert.ToInt64(f.ConstantValue,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var bits = underlying.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte => 8,
            SpecialType.System_Int16 or SpecialType.System_UInt16 => 16,
            SpecialType.System_Int32 or SpecialType.System_UInt32 => 32,
            _ => 64,
        };
        var mask = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
        var keys = values.Select(v => unchecked((ulong)v) & mask).ToArray();
        var order = Enumerable.Range(0, keys.Length).ToArray();
        if (!keys.Zip(keys.Skip(1)).All(p => p.First <= p.Second))
            Array.Sort((ulong[])keys.Clone(), order);
        var sortedKeys = order.Select(i => keys[i]).ToArray();

        var entries = new List<string>();
        if (enumType.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"))
        {
            entries.AddRange(order.Select(i => $"{names[i]}:{values[i]}"));
            return "*" + string.Join(",", entries);
        }
        foreach (var key in sortedKeys.Distinct())
        {
            var span = (ReadOnlySpan<ulong>)sortedKeys;
            var at = sortedKeys.Length <= 32 ? span.IndexOf(key) : span.BinarySearch(key);
            entries.Add($"{names[order[at]]}:{values[order[at]]}");
        }
        return string.Join(",", entries);
    }
}
