using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        if (IsStructuralType(patternType))
        {
            if (SymbolEqualityComparer.Default.Equals(inputType, patternType))
                return new IlBin(IlBinOp.Ne, expr, new IlLit("nil"));
            return StructuralTypeTests
                ? new IlIsRefType(expr, patternType!.ToDisplayString()) : null;
        }
        if (patternType is { IsValueType: true } && inputType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(inputType, patternType))
                return BindPatternInput(expr, _ => new IlLit("true"));
            if (IsNullableValueType(inputType) && SymbolEqualityComparer.Default
                    .Equals(UnwrapNullable(inputType), patternType))
                return new IlNullableHasValue(expr);
        }
        return typeRef == null ? null
            : new IlIsType(expr, typeRef, Direct: CanCompareMetatableDirectly(patternType));
    }

    // metatable の直接比較で足りるのは、Lua に class table がある user の class で派生が
    // 無いもの。`--ref` の class は Lua に定義が無く (global は nil)、`getmetatable(nil) == nil`
    // が真になって null が一致してしまうので、派生の有無に関わらず __tcs_is を使う (#270)
    private bool CanCompareMetatableDirectly(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class }
        && !IsReferenceOnlyType(type)
        && !HasDerivedClass(type);

    // compilation (user と `--ref` の tree) の中で T を基底にする class があるか。
    // user の class は継承できない (q.md Q15) ので、派生は `--ref` stub の class だけ。
    // compilation は Visit / EmitSingleMethod が設定する。分からない経路では派生が
    // あるものとして扱う (直接比較に落とすより chain 走査の方が安全。#271)
    private CSharpCompilation? _compilation;
    private HashSet<string>? _basesWithDerived;
    private bool HasDerivedClass(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named) return false;
        if (_compilation == null) return true;
        _basesWithDerived ??= CollectBasesWithDerived();
        return _basesWithDerived.Contains(named.OriginalDefinition.ToDisplayString());
    }

    private HashSet<string> CollectBasesWithDerived()
    {
        var bases = new HashSet<string>(StringComparer.Ordinal);
        if (_compilation == null) return bases;
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var model = _compilation.GetSemanticModel(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (decl is not (ClassDeclarationSyntax or RecordDeclarationSyntax)) continue;
                if (model.GetDeclaredSymbol(decl)?.BaseType is { SpecialType: SpecialType.None } baseType)
                    bases.Add(baseType.OriginalDefinition.ToDisplayString());
            }
        }
        return bases;
    }

    // struct の宣言 pattern の designation に束縛する値: 入力 (`S?` は値が
    // あると判った後の中の値) の copy
    private IlExpr DesignationValue(IlExpr input, ITypeSymbol inputType,
        ITypeSymbol declType)
    {
        var value = IsNullableValueType(inputType) ? new IlNullableValue(input) : input;
        return declType is INamedTypeSymbol { IsReadOnly: true }
            ? value : new IlStructCopy(value, TypeName(declType));
    }

    // 入力 `S` / `S?` に当てた struct `S` の宣言 pattern なら (入力型, S)
    private static (ITypeSymbol, ITypeSymbol)? StructDesignation(SemanticModel model,
        DeclarationPatternSyntax dp) =>
        model.GetTypeInfo(dp.Type).Type is { } declType && IsUserStruct(declType)
        && PatternInputType(model, dp) is { } inputType
        && SymbolEqualityComparer.Default.Equals(UnwrapNullable(inputType), declType)
            ? (inputType, declType) : null;

    // switch の designation の前宣言。struct と class は型だけ宣言し、arm ごとに
    // 照合の中で束縛する (struct は copy、class は絞った型への cast。照合が通った
    // 後なので C の検査付き cast は fault しない)。それ以外は入力をそのまま束縛する
    private IlLocal SwitchDesignationLocal(SemanticModel model,
        DeclarationPatternSyntax dp, string name, IlExpr sw)
    {
        if (StructDesignation(model, dp) is (_, var structType))
            return new IlLocal(name, null, structType.ToDisplayString());
        var declType = model.GetTypeInfo(dp.Type).Type;
        return declType is { TypeKind: TypeKind.Class }
            ? new IlLocal(name, null, declType.ToDisplayString())
            : new IlLocal(name, sw);
    }

    // class の宣言 pattern: 型照合が通ったら designation に絞った型で束縛する
    // (IlRefCast。Lua は素通し、C は interface 型の入力から class の member を引ける)
    private IlExpr? BuildClassDeclarationIs(SemanticModel model, IlExpr governing,
        DeclarationPatternSyntax dp, string name, ITypeSymbol declType)
    {
        var check = BuildTypeCheck(governing, PatternInputType(model, dp), declType,
            FormatTypeReference(model, dp.Type));
        if (check == null) return null;
        return new IlBin(IlBinOp.And, check, new IlIife([
            new IlAssign(new IlVar(name), new IlRefCast(governing, declType.ToDisplayString())),
            new IlReturn(new IlLit("true"))]));
    }

    // `and` の右辺の入力は左辺が絞った型。`S?` の入力を左辺が struct `S` に
    // 絞ったら (値があると判っているので) 右辺は中の値を照合する
    private static IlExpr AndRightInput(SemanticModel model, BinaryPatternSyntax bp,
        IlExpr input) =>
        bp.IsKind(SyntaxKind.AndPattern)
        && model.GetOperation(bp.Left) is IPatternOperation left
        && IsNullableValueType(left.InputType) && IsUserStruct(left.NarrowedType)
            ? new IlNullableValue(input) : input;

    private static bool IsStructuralType(ITypeSymbol? type) =>
        type is IArrayTypeSymbol || type != null
            && (IsListType(type.OriginalDefinition.ToDisplayString())
                || IsDictType(type.OriginalDefinition.ToDisplayString()));

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
                new IlAssign(target, DesignationValue(expr, inputType, declType)),
                new IlReturn(new IlLit("true"))]);
        return BindPatternInput(expr, input => new IlIife([
            new IlIf([(new IlNullableHasValue(input), new IlBlock([
                new IlAssign(target, DesignationValue(input, inputType, declType)),
                new IlReturn(new IlLit("true"))]))], null),
            new IlReturn(new IlLit("false"))]))!;
    }
}
