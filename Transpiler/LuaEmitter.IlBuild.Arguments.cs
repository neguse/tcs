using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyCs;

// 呼び出し引数の IL 構築 (値渡し copy / struct の ref・in 参照渡し) と
// out 引数を伴う Dictionary.TryGetValue の lowering。
public partial class LuaEmitter
{
    // 引数の IL。値渡しは il-spec §10 の copy 地点。struct の ref / in /
    // ref readonly parameter は呼び出し側の値 (table) をそのまま渡す: ref は
    // callee の field 書き込みと S.__assign による代入を呼び出し側と共有し、
    // in は callee が変更できない (変更系 member 呼び出しは防御コピー)。
    // それ以外の ref / out 引数は未対応 (null → legacy fallback)
    private List<IlExpr>? BuildArgs(SemanticModel model, IMethodSymbol? method,
        IEnumerable<ArgumentSyntax> arguments)
    {
        var args = new List<IlExpr>();
        var i = -1;
        foreach (var a in arguments)
        {
            i++;
            var param = method != null && i < method.Parameters.Length
                ? method.Parameters[i] : null;
            var byReference = param is
                {
                    RefKind: RefKind.Ref or RefKind.In
                        or RefKind.RefReadOnlyParameter,
                }
                && IsUserStruct(param.Type);
            var keywordOk = a.RefKindKeyword.Kind() switch
            {
                SyntaxKind.None => true,
                SyntaxKind.InKeyword => param?.RefKind
                    is RefKind.In or RefKind.RefReadOnlyParameter,
                SyntaxKind.RefKeyword => byReference,
                _ => false,
            };
            if (!keywordOk) return null;
            var built = BuildExpr(model, a.Expression);
            if (built == null) return null;
            args.Add(byReference ? built : WrapStructCopy(model, a.Expression, built));
        }
        return args;
    }

    // Dictionary.TryGetValue(key, out v) — legacy IIFE の写像
    private IlExpr? BuildDictTryGetValue(SemanticModel model,
        InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax ma,
        IMethodSymbol methodSym)
    {
        if (invocation.ArgumentList.Arguments.Count != 2) return null;
        var keyArg = invocation.ArgumentList.Arguments[0];
        var outArg = invocation.ArgumentList.Arguments[1];
        if (!keyArg.RefKindKeyword.IsKind(SyntaxKind.None)) return null;
        var key = BuildExpr(model, keyArg.Expression);
        var recv = BuildExpr(model, ma.Expression);
        if (key == null || recv == null) return null;
        IlExpr? target = outArg.Expression switch
        {
            DeclarationExpressionSyntax decl =>
                new IlVar(VisitDeclarationExpression(decl)),
            IdentifierNameSyntax id => new IlVar(L(id.Identifier.ValueText)),
            _ => null,
        };
        if (target == null) return null;
        var defaultValue = GetDefaultValueForType(
            methodSym.Parameters.Length > 1
                ? methodSym.Parameters[1].Type : null);
        // multi-return intrinsic (il-spec §13)。nil 比較の desugar を IL に
        // 残さない (C backend が「nil = 不在」を型付けできないため)
        return new IlIife([
            new IlMultiAssign(
                [new IlVar("__tcs_found"), new IlVar("__tcs_v")],
                [new IlCall("Dict.TryGet",
                    [recv, key, new IlLit(defaultValue)])],
                Declare: true),
            new IlAssign(target, new IlVar("__tcs_v")),
            new IlReturn(new IlVar("__tcs_found"))]);
    }
}
