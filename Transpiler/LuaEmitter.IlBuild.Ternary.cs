using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyCs;

// 条件式 (?:) の IL 構築。文位置 (return / local 初期化 / local への代入) は
// if 連鎖へ statement 化し、式位置は分岐値の falsy 可能性を IlTernary に
// 載せて emitter が and/or か IIFE を選ぶ (issue #12)。
public partial class LuaEmitter
{
    // 値が Lua の falsy (nil / false) になり得ない式か。条件式を `c and t or f`
    // で render できる根拠になる。数値・enum・struct は非 null で false にも
    // ならない。参照型は生成式・非 null literal・連結/補間の結果だけ
    private static bool IsNeverFalsy(SemanticModel model, ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
            expr = paren.Expression;
        var type = model.GetTypeInfo(expr).Type;
        if (type is null
            || type.OriginalDefinition.SpecialType
                == SpecialType.System_Nullable_T)
            return false;
        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                return lit.Kind() is SyntaxKind.NumericLiteralExpression
                    or SyntaxKind.StringLiteralExpression
                    or SyntaxKind.CharacterLiteralExpression
                    or SyntaxKind.TrueLiteralExpression;
            case InterpolatedStringExpressionSyntax
                or BaseObjectCreationExpressionSyntax
                or ArrayCreationExpressionSyntax
                or ImplicitArrayCreationExpressionSyntax:
                return true;
            case BinaryExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.AddExpression,
                } when type.SpecialType == SpecialType.System_String:
                return true;
        }
        return type.TypeKind == TypeKind.Enum
            || IsUserStruct(type)
            || type.SpecialType is SpecialType.System_Int32
                or SpecialType.System_Single or SpecialType.System_Double
                or SpecialType.System_Int64 or SpecialType.System_Int16
                or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_UInt16 or SpecialType.System_UInt32
                or SpecialType.System_UInt64 or SpecialType.System_Char;
    }

    // 条件式を「分岐値を sink した文」の if 連鎖へ展開する。入れ子の条件式も
    // 分岐ごとに再帰展開し (外側だけ文にして内側を IIFE に残さない)、else 側
    // の入れ子は elseif へ平らに並べる。評価順は cond → 選ばれた分岐のまま
    private static IlIf TernaryAsIf(IlTernary ternary, Func<IlExpr, IlStat> sink)
    {
        var arms = new List<(IlExpr, IlBlock)>();
        IlExpr current = ternary;
        while (StripIlParen(current) is IlTernary t)
        {
            arms.Add((t.Cond, TernaryBranch(t.T, sink)));
            current = t.F;
        }
        return new IlIf([.. arms], TernaryBranch(current, sink));
    }

    private static IlBlock TernaryBranch(IlExpr value, Func<IlExpr, IlStat> sink) =>
        StripIlParen(value) is IlTernary nested
            ? new IlBlock([TernaryAsIf(nested, sink)])
            : new IlBlock([sink(value)]);

    private static IlExpr? StripIlParen(IlExpr? e)
    {
        while (e is IlParen p) e = p.E;
        return e;
    }
}
