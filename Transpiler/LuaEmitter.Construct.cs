using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyCs;

// instance の生成 (class / record の `new`、struct の `new` / `ctor` / `__copy`)。
// 空 table へ key を 1 つずつ足すと hash 部の再確保が key 数に応じて起き、
// 直後に上書きされる既定値の代入も無駄になる (issue #10)。field を宣言順に
// 並べた table constructor で一度に作り、本文先頭の「self.f = 純粋な値」代入は
// その中へ畳み込む。
public partial class LuaEmitter
{
    // 生成 table の field 列 (宣言順)。Pure は副作用も fault も無い値 (既定値・
    // 定数・parameter 参照) で、評価位置を動かしても観測できない。
    private sealed class InstanceTable
    {
        private readonly List<(string Name, string Value, bool Pure)> _entries = [];

        // 評価順を保てず table へ入れられなかった値 (table 生成の直後に代入文
        // として出す)。一度 spill したら以後の値もすべて spill して順序を保つ
        public List<(string Name, string Value)> Spilled { get; } = [];

        public IReadOnlyList<(string Name, string Value, bool Pure)> Entries =>
            _entries;

        // field に値を設定する。既存 entry の置き換えはその位置で評価される
        // ことになるため、元の値が Pure (捨ててよい) で、かつ新しい値が Pure
        // か後続 entry がすべて Pure (追い越しが観測できない) の時だけ許す
        public void Set(string name, string value, bool pure)
        {
            if (Spilled.Count > 0)
            {
                Spilled.Add((name, value));
                return;
            }
            var i = _entries.FindIndex(e => e.Name == name);
            if (i < 0)
            {
                _entries.Add((name, value, pure));
                return;
            }
            if (_entries[i].Pure
                && (pure || _entries.Skip(i + 1).All(e => e.Pure)))
            {
                _entries[i] = (name, value, pure);
                return;
            }
            Spilled.Add((name, value));
        }

        // 本文先頭の Pure な代入の畳み込み。元の値が Pure でなければ畳めない
        // (評価を消せない) — 呼び出し側は代入文を本文に残す
        public bool TryFold(string name, string value)
        {
            if (Spilled.Count > 0) return false;
            var i = _entries.FindIndex(e => e.Name == name);
            if (i >= 0 && !_entries[i].Pure) return false;
            if (i < 0) _entries.Add((name, value, true));
            else _entries[i] = (name, value, true);
            return true;
        }

        // nil は key を作らないので省く
        public string Render() =>
            "{" + string.Join(", ", _entries
                .Where(e => e.Value != "nil")
                .Select(e => $"{e.Name} = {e.Value}")) + "}";
    }

    private void EmitConstructor(SemanticModel model, string className,
        ConstructorDeclarationSyntax? ctor,
        List<(string Name, ExpressionSyntax? Init, ITypeSymbol? Type)> fieldInits,
        ITypeSymbol? baseClass)
    {
        var ctorParams = ctor?.ParameterList.Parameters
            .Select(p => L(p.Identifier.ValueText)).ToList() ?? [];

        AppendLine($"function {className}.new({string.Join(", ", ctorParams)})");
        _indent++;
        if (ctor != null)
            EmitParameterDefaults(model, ctor.ParameterList);

        var table = new InstanceTable();
        foreach (var (fieldName, init, type) in fieldInits)
            table.Set(fieldName,
                init != null
                    ? VisitExpression(model, init)
                    : GetDefaultValueForType(type!),
                pure: init == null || model.GetConstantValue(init).HasValue);
        var body = BuildCtorBody(model, ctor);
        var rest = body == null ? null : FoldCtorPrefix(table, body);

        string? baseCall = null;
        if (ctor?.Initializer != null
            && ctor.Initializer.IsKind(SyntaxKind.BaseConstructorInitializer))
        {
            var baseArgs = ctor.Initializer.ArgumentList.Arguments
                .Select(a => VisitExpression(model, a.Expression));
            var baseType = model.GetDeclaredSymbol(ctor)?.ContainingType?.BaseType;
            if (baseType != null && baseType.SpecialType != SpecialType.System_Object)
                baseCall = $"{baseType.Name}.new({string.Join(", ", baseArgs)})";
        }
        else if (baseClass != null)
        {
            // initializer なしでも C# は暗黙に base() を呼ぶ。基底の field
            // initializer / constructor body を実行してから派生へ差し替える
            // (this(...) initializer は TCS1001 済みで、ここでは base() 扱い)
            baseCall = $"{baseClass.Name}.new()";
        }

        if (baseCall != null)
        {
            // 基底が作った table に派生の field を足す (table constructor は
            // 使えない)。nil は key を作らないので代入しない
            AppendLine($"local self = {baseCall}");
            AppendLine($"setmetatable(self, {className})");
            foreach (var (name, value, _) in table.Entries)
                if (value != "nil")
                    AppendLine($"self.{name} = {value}");
        }
        else
        {
            AppendLine($"local self = setmetatable({table.Render()}, {className})");
        }
        foreach (var (name, value) in table.Spilled)
            AppendLine($"self.{name} = {value}");
        // reload migration 用の登録。base ctor 経由でも最派生 class が勝つ
        // (同一 key への上書き)
        if (EmitInstanceRegistry)
            AppendLine($"__tcs_instances[self] = {className}");

        if (rest != null)
        {
            if (ctor is { Body: not null } or { ExpressionBody: not null })
                IlBodies++;
            EmitIlBlock(rest);
        }
        else if (ctor?.Body != null)
        {
            LegacyBodies++;
            foreach (var stmt in ctor.Body.Statements)
                VisitStatement(model, stmt);
        }
        else if (ctor?.ExpressionBody != null)
        {
            LegacyBodies++;
            AppendLine(VisitExpression(model, ctor.ExpressionBody.Expression));
        }

        AppendLine("return self");
        _indent--;
        AppendLine("end");
        AppendLine();
    }

    // ctor 本文の IL。本文なしは空 block、IL 化できなければ null (legacy)
    private IlBlock? BuildCtorBody(SemanticModel model,
        BaseMethodDeclarationSyntax? ctor)
    {
        if (IlDisabled && ctor is { Body: not null } or { ExpressionBody: not null })
            return null;
        var acc = new List<IlStat>();
        if (ctor?.Body != null)
            return BuildStatsInto(model, ctor.Body.Statements, acc)
                ? new IlBlock([.. acc]) : null;
        if (ctor?.ExpressionBody != null)
            return BuildExprStatInto(model, ctor.ExpressionBody.Expression,
                null, acc)
                ? new IlBlock([.. acc]) : null;
        return new IlBlock([]);
    }

    // 本文先頭の連続する「self.f = 純粋な値」を table へ畳み込み、残りを返す。
    // 畳めない代入も純粋な field store なので順序を保ったまま本文に残す
    private IlBlock FoldCtorPrefix(InstanceTable table, IlBlock body)
    {
        var kept = new List<IlStat>();
        var i = 0;
        for (; i < body.Stats.Length; i++)
        {
            if (body.Stats[i] is not IlAssign
                {
                    Target: IlField { Recv: IlVar { Name: "self" }, Name: var field },
                    Value: var value,
                } assign
                || !IsPureCtorValue(value))
                break;
            if (!table.TryFold(field, RenderIl(value)))
                kept.Add(assign);
        }
        return new IlBlock([.. kept, .. body.Stats[i..]]);
    }

    // 評価位置を前へ動かしても観測できない値: literal、self 以外の local /
    // parameter 参照、それらの struct copy (fresh な table を作るだけ)
    private static bool IsPureCtorValue(IlExpr value) => value switch
    {
        IlLit => true,
        IlVar v => v.Name != "self",
        IlParen p => IsPureCtorValue(p.E),
        IlStructCopy c => IsPureCtorValue(c.E),
        _ => false,
    };

    // positional record class: param を field へ並べた table を一度に作る
    private void EmitRecordNew(string name, IReadOnlyList<string> paramNames,
        IReadOnlyList<string> fieldNames)
    {
        AppendLine($"function {name}.new({string.Join(", ", paramNames)})");
        _indent++;
        var fields = string.Join(", ",
            fieldNames.Select((f, i) => $"{f} = {paramNames[i]}"));
        AppendLine($"local self = setmetatable({{{fields}}}, {name})");
        if (EmitInstanceRegistry)
            AppendLine($"__tcs_instances[self] = {name}");
        AppendLine("return self");
        _indent--;
        AppendLine("end");
        AppendLine();
    }
}
