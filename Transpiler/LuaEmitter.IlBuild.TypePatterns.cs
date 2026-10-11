using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace TinyCs;

// IL builder: pattern の型照合と designation の束縛
public partial class LuaEmitter
{
    // legacy EmitTypeCheck の写像。struct の入力 (`S` / `S?`) を同じ struct 型で
    // 照合するのは値の有無だけ (struct の値は型 tag を持たない)
    private IlExpr? BuildTypeCheck(IlExpr expr, ITypeSymbol? inputType,
        ITypeSymbol? patternType, string? typeRef)
    {
        if (LuaTypeNameFor(patternType) is { } luaType)
            return new IlIsLuaType(expr, luaType);
        if (patternType is { IsValueType: true } && inputType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(inputType, patternType))
                return BindPatternInput(expr, _ => new IlLit("true"));
            if (IsNullableValueType(inputType) && SymbolEqualityComparer.Default
                    .Equals(UnwrapNullable(inputType), patternType))
                return new IlNullableHasValue(expr);
        }
        return typeRef == null ? null : new IlIsType(expr, typeRef);
    }

    // 型 pattern の designation に束縛する値。struct は入力の copy で、`S?` の
    // 入力は中の値 (hasValue が保証されない束縛は値なしで default にする)
    private IlExpr DesignationValue(IlExpr input, ITypeSymbol? inputType,
        ITypeSymbol? declType, bool hasValue)
    {
        if (!IsUserStruct(declType)) return input;
        var value = !IsNullableValueType(inputType) ? input
            : hasValue ? new IlNullableValue(input)
            : new IlNullableGetOrDefault(input, DefaultIl(declType));
        return declType is INamedTypeSymbol { IsReadOnly: true }
            ? value : new IlStructCopy(value, TypeName(declType!));
    }

    private static ITypeSymbol? PatternInputType(SemanticModel model,
        SyntaxNode node) => model.GetOperation(node) switch
        {
            IPatternOperation pattern => pattern.InputType,
            IPatternCaseClauseOperation clause => clause.Pattern.InputType,
            _ => null,
        };

    // `x is S v` (x: S / S?): 値があれば copy を束縛して true
    private IlExpr BuildStructDeclarationIs(IlVar target, IlExpr expr,
        ITypeSymbol inputType, ITypeSymbol declType)
    {
        if (!IsNullableValueType(inputType))
            return new IlIife([
                new IlAssign(target, DesignationValue(expr, inputType, declType,
                    hasValue: true)),
                new IlReturn(new IlLit("true"))]);
        return BindPatternInput(expr, input => new IlIife([
            new IlIf([(new IlNullableHasValue(input), new IlBlock([
                new IlAssign(target, DesignationValue(input, inputType, declType,
                    hasValue: true)),
                new IlReturn(new IlLit("true"))]))], null),
            new IlReturn(new IlLit("false"))]))!;
    }
}
