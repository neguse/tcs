using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyCs;

public partial class LuaEmitter
{
    /// <summary>operator 宣言の Lua 関数名。単一 overload は metamethod 名
    /// そのもの、同じ metamethod の複数 overload は宣言順に `__mul_1`,
    /// `__mul_2` ... (EmitOperatorGroup と同じ採番)。</summary>
    internal static string? OperatorFunctionName(OperatorDeclarationSyntax op)
    {
        if (!TinyCsComplianceFacts.TryGetOperatorMetamethod(op,
                out var metamethod)
            || op.Parent is not TypeDeclarationSyntax owner)
            return null;
        var group = owner.Members.OfType<OperatorDeclarationSyntax>()
            .Where(o => TinyCsComplianceFacts.TryGetOperatorMetamethod(o,
                out var m) && m == metamethod)
            .ToList();
        return group.Count == 1
            ? metamethod
            : $"{metamethod}_{group.IndexOf(op) + 1}";
    }

    // C# の operator overload 解決は静的なので、呼び出しサイトで宣言型の
    // operator 関数を直接呼ぶ (`Vec2.__mul_2(a, s)`)。metamethod 経由だと
    // (1) 派生 instance の metatable に __add が無く (metamethod は __index
    // 継承されない) 基底の operator が動かない、(2) 複数 overload の実行時
    // 型分岐を毎回通る。参照専用型 (--ref) の operator は host 側の
    // metamethod に委ねる (Lua 定義を持たないため)。
    private string? UserOperatorCallee(SemanticModel model, ExpressionSyntax expr)
    {
        if (model.GetSymbolInfo(expr).Symbol is not IMethodSymbol
            {
                MethodKind: MethodKind.UserDefinedOperator,
                ContainingType: { } owner,
            } op
            || IsReferenceOnlyType(owner))
            return null;
        var decl = op.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax()).OfType<OperatorDeclarationSyntax>()
            .FirstOrDefault();
        return decl != null && OperatorFunctionName(decl) is { } name
            ? $"{TypeRef(owner)}.{name}"
            : null;
    }

    // User-defined operator overloads map to Lua metamethods on the class
    // table (which is also the instance metatable). Multiple C# overloads of
    // one operator share a single metamethod, so the metamethod dispatches on
    // runtime operand types: metatable identity for class/record instances,
    // type() for primitives.
    private void EmitOperators(SemanticModel model, string className,
        List<OperatorDeclarationSyntax> operators)
    {
        foreach (var group in operators
            .GroupBy(op => TinyCsComplianceFacts.TryGetOperatorMetamethod(op,
                out var metamethod) ? metamethod : "")
            .Where(g => g.Key.Length > 0))
        {
            EmitOperatorGroup(model, className, group.Key, [.. group]);
        }
    }

    private void EmitOperatorGroup(SemanticModel model, string className,
        string metamethod, List<OperatorDeclarationSyntax> overloads)
    {
        if (overloads.Count == 1)
        {
            EmitOperatorFunction(model, className, metamethod, overloads[0]);
            return;
        }

        for (var i = 0; i < overloads.Count; i++)
        {
            EmitOperatorFunction(model, className, $"{metamethod}_{i + 1}",
                overloads[i]);
        }

        // All overloads of one metamethod share the same arity
        // (binary metamethods vs __unm), so a common parameter list works.
        var paramCount = overloads[0].ParameterList.Parameters.Count;
        var dispatchParams = paramCount == 1 ? new[] { "a" } : ["a", "b"];
        var paramList = string.Join(", ", dispatchParams);

        SetSource(overloads[0]);
        _currentType?.DefinitionKeys.Add(metamethod);
        AppendLine($"function {className}.{metamethod}({paramList})");
        _indent++;
        for (var i = 0; i < overloads.Count; i++)
        {
            var condition = BuildOverloadCondition(model, overloads[i],
                dispatchParams);
            AppendLine($"{(i == 0 ? "if" : "elseif")} {condition} then");
            _indent++;
            AppendLine($"return {className}.{metamethod}_{i + 1}({paramList})");
            _indent--;
        }
        AppendLine("end");
        AppendLine($"error(\"{className}.{metamethod}: no matching operator overload\")");
        _indent--;
        AppendLine("end");
        AppendLine();
    }

    private static string BuildOverloadCondition(SemanticModel model,
        OperatorDeclarationSyntax op, string[] dispatchParams)
    {
        var conditions = new List<string>();
        var parameters = op.ParameterList.Parameters;
        for (var i = 0; i < parameters.Count && i < dispatchParams.Length; i++)
        {
            var type = model.GetTypeInfo(parameters[i].Type!).Type;
            var check = GetOperandTypeCheck(type, dispatchParams[i]);
            if (check != null) conditions.Add(check);
        }
        return conditions.Count > 0 ? string.Join(" and ", conditions) : "true";
    }

    private static string? GetOperandTypeCheck(ITypeSymbol? type, string arg)
    {
        if (type == null) return null;
        if (type.SpecialType is SpecialType.System_Int32
            or SpecialType.System_Int64 or SpecialType.System_UInt32
            or SpecialType.System_Single or SpecialType.System_Double
            || type.TypeKind == TypeKind.Enum)
        {
            return $"type({arg}) == \"number\"";
        }
        if (type.SpecialType == SpecialType.System_String)
            return $"type({arg}) == \"string\"";
        if (type.SpecialType == SpecialType.System_Boolean)
            return $"type({arg}) == \"boolean\"";
        if (type.TypeKind == TypeKind.Class
            && type.SpecialType == SpecialType.None)
        {
            return $"getmetatable({arg}) == {type.Name}";
        }
        return null;
    }

    private void EmitOperatorFunction(SemanticModel model, string className,
        string luaName, OperatorDeclarationSyntax op)
    {
        SetSource(op);
        _currentType?.DefinitionKeys.Add(luaName);
        var paramNames = op.ParameterList.Parameters
            .Select(p => p.Identifier.ValueText).ToList();
        AppendLine($"function {className}.{luaName}({string.Join(", ", paramNames)})");
        _indent++;

        if (op.Body != null)
        {
            if (!TryEmitStatsViaIl(model, op.Body.Statements))
            {
                LegacyBodies++;
                foreach (var stmt in op.Body.Statements)
                    VisitStatement(model, stmt);
            }
        }
        else if (op.ExpressionBody != null)
        {
            if (!TryEmitReturnViaIl(model, op.ExpressionBody.Expression))
            {
                LegacyBodies++;
                AppendLine($"return {VisitExpression(model, op.ExpressionBody.Expression)}");
            }
        }

        _indent--;
        AppendLine("end");
        AppendLine();
    }
}
