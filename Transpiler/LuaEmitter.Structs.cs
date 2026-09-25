using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyCs;

// struct / record struct の emit。instance は metatable 無しの
// plain table で、member は静的自由関数 (`S.M(self, ...)`) として同名 table に
// 載る — struct は継承がなく呼び出しサイトの静的型が確定するため動的
// ディスパッチ不要で、呼び出し側 (IlBuild) が直接呼ぶ。
public partial class LuaEmitter
{
    private void VisitStruct(SemanticModel model, StructDeclarationSyntax structDecl)
    {
        SetSource(structDecl);
        var name = structDecl.Identifier.ValueText;
        var symbol = model.GetDeclaredSymbol(structDecl);
        EmitTypeTable(name);
        var statics = CollectStaticFields(model, structDecl.Members);
        EmitStaticPreZero(name, statics, null);
        AppendLine();
        EmitStructNew(name, symbol);
        EmitStructCopyFunction(name, symbol);
        EmitStructAssignFunction(name, symbol);

        var ctor = structDecl.Members.OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword)
                && c.ParameterList.Parameters.Count > 0);
        if (ctor != null)
            EmitStructCtor(model, name, structDecl, ctor);

        EmitStructMembers(model, name, structDecl.Members);
        EmitStaticInitializers(model, name, statics, null);
    }

    // record struct。struct の emit の上に positional primary ctor
    // と値等価 (op_Equality) を合成する。== の呼び出しサイトは IlBuild が
    // 静的型から直接 op_Equality へ振り分ける
    private void VisitRecordStruct(SemanticModel model,
        RecordDeclarationSyntax rec)
    {
        SetSource(rec);
        var name = rec.Identifier.ValueText;
        var symbol = model.GetDeclaredSymbol(rec);
        EmitTypeTable(name);
        var statics = CollectStaticFields(model, rec.Members);
        EmitStaticPreZero(name, statics, null);
        AppendLine();
        EmitStructNew(name, symbol);

        // positional primary ctor: zero → positional 代入 → initializer の順
        // (initializer は primary ctor param を参照できる — param 名で解決)
        if (rec.ParameterList is { Parameters.Count: > 0 } parameterList)
        {
            var paramNames = parameterList.Parameters
                .Select(p => p.Identifier.ValueText).ToList();
            _currentType?.DefinitionKeys.Add("ctor");
            AppendLine($"function {name}.ctor({string.Join(", ", paramNames.Select(L))})");
            _indent++;
            var table = ZeroStructTable(symbol);
            foreach (var p in paramNames)
                table.Set(N(p), L(p), pure: true);
            SetMemberInitializers(model, table, rec.Members);
            AppendLine($"local self = {table.Render()}");
            foreach (var (field, value) in table.Spilled)
                AppendLine($"self.{field} = {value}");
            AppendLine("return self");
            _indent--;
            AppendLine("end");
            AppendLine();
        }

        EmitStructCopyFunction(name, symbol);
        EmitStructAssignFunction(name, symbol);

        // 値等価。ネスト struct 値は推移的に field 展開する (struct は
        // 循環できないので停止する)
        _currentType?.DefinitionKeys.Add("op_Equality");
        AppendLine($"function {name}.op_Equality(a, b)");
        _indent++;
        var parts = symbol == null
            ? []
            : ValueEqualityParts(symbol, "a", "b").ToList();
        AppendLine(parts.Count == 0
            ? "return true"
            : $"return {string.Join(" and ", parts)}");
        _indent--;
        AppendLine("end");
        AppendLine();

        EmitStructMembers(model, name, rec.Members);
        EmitStaticInitializers(model, name, statics, null);
    }

    // zero 初期化コンストラクタ。`new S()` / default(S) / field default が通る
    private void EmitStructNew(string name, INamedTypeSymbol? symbol)
    {
        _currentType?.DefinitionKeys.Add("new");
        AppendLine($"function {name}.new()");
        _indent++;
        AppendLine($"return {ZeroStructTable(symbol).Render()}");
        _indent--;
        AppendLine("end");
        AppendLine();
    }

    // 全 member を zero 値で並べた生成 table (struct ctor の出発点)
    private static InstanceTable ZeroStructTable(INamedTypeSymbol? symbol)
    {
        var table = new InstanceTable();
        foreach (var (memberName, memberType) in ValueMembers(symbol))
            table.Set(memberName, GetDefaultValueForType(memberType), pure: true);
        return table;
    }

    // member の emit。instance は明示 self の自由関数、static は self なしの
    // 関数。operator は呼び出しサイトが静的に解決する関数 (`S.__add`) だけで、
    // metatable が無いので metamethod 用の実行時 dispatcher は作らない。
    // override (ToString 等) は診断済み (Shared facts) なので emit しない
    private void EmitStructMembers(SemanticModel model, string name,
        IEnumerable<MemberDeclarationSyntax> members)
    {
        var operators = new List<OperatorDeclarationSyntax>();
        foreach (var member in members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method
                    when !method.Modifiers.Any(SyntaxKind.OverrideKeyword):
                    VisitMethod(model, name, method, explicitSelf: true);
                    break;
                case PropertyDeclarationSyntax prop
                    when !IsAutoProperty(prop) && prop.AccessorList != null:
                    VisitCustomProperty(model, name, prop, explicitSelf: true);
                    break;
                case PropertyDeclarationSyntax prop
                    when prop.ExpressionBody != null:
                    VisitExpressionBodiedProperty(model, name, prop,
                        explicitSelf: true);
                    break;
                case OperatorDeclarationSyntax op
                    when TinyCsComplianceFacts.TryGetOperatorMetamethod(op, out _):
                    operators.Add(op);
                    break;
            }
        }
        EmitOperators(model, name, operators, runtimeDispatch: false);
    }

    // ref parameter への代入 (`p = v`) の書き戻し先。呼び出し側の値 (table) を
    // 保ったまま field を in-place で上書きする。mutable な struct member は
    // 再帰的に in-place (その table への ref も新しい値を見る)、readonly
    // struct の member は不変値なので参照を差し替える。readonly struct 自体は
    // ref parameter を持てない (Shared facts) ので作らない
    private void EmitStructAssignFunction(string name, INamedTypeSymbol? symbol)
    {
        if (symbol is { IsReadOnly: true }) return;
        _currentType?.DefinitionKeys.Add("__assign");
        AppendLine($"function {name}.__assign(d, s)");
        _indent++;
        foreach (var (memberName, memberType) in ValueMembers(symbol))
        {
            var deep = IsUserStruct(memberType)
                && memberType is not INamedTypeSymbol { IsReadOnly: true };
            AppendLine(deep
                ? $"{memberType.Name}.__assign(d.{memberName}, s.{memberName})"
                : $"d.{memberName} = s.{memberName}");
        }
        _indent--;
        AppendLine("end");
        AppendLine();
    }

    // field / property initializer は明示 ctor 実行時のみ走る (C# 11 意味論)
    private void SetMemberInitializers(SemanticModel model, InstanceTable table,
        IEnumerable<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field
                    when !field.Modifiers.Any(SyntaxKind.StaticKeyword)
                        && !field.Modifiers.Any(SyntaxKind.ConstKeyword):
                    foreach (var v in field.Declaration.Variables)
                        if (v.Initializer != null)
                            table.Set(N(v.Identifier.ValueText),
                                VisitExpression(model, v.Initializer.Value),
                                model.GetConstantValue(v.Initializer.Value).HasValue);
                    break;
                case PropertyDeclarationSyntax { Initializer: not null } prop
                    when !prop.Modifiers.Any(SyntaxKind.StaticKeyword):
                    table.Set(N(prop.Identifier.ValueText),
                        VisitExpression(model, prop.Initializer.Value),
                        model.GetConstantValue(prop.Initializer.Value).HasValue);
                    break;
            }
        }
    }

    // 明示 ctor は zero 初期化 (S.new) の上で field initializer → 本文の順で
    // 実行する (C# の struct ctor 意味論)。`new S()` は ctor を通らない zero
    // 値なので、呼び出しは builder が S.ctor へ振り分ける
    private void EmitStructCtor(SemanticModel model, string name,
        StructDeclarationSyntax structDecl, ConstructorDeclarationSyntax ctor)
    {
        SetSource(ctor);
        var ctorParams = ctor.ParameterList.Parameters
            .Select(p => L(p.Identifier.ValueText)).ToList();
        _currentType?.DefinitionKeys.Add("ctor");
        AppendLine($"function {name}.ctor({string.Join(", ", ctorParams)})");
        _indent++;
        EmitParameterDefaults(model, ctor.ParameterList);
        var table = ZeroStructTable(model.GetDeclaredSymbol(structDecl));
        SetMemberInitializers(model, table, structDecl.Members);
        var body = BuildCtorBody(model, ctor);
        var rest = body == null ? null : FoldCtorPrefix(table, body);
        AppendLine($"local self = {table.Render()}");
        foreach (var (field, value) in table.Spilled)
            AppendLine($"self.{field} = {value}");
        if (rest != null)
        {
            if (ctor is { Body: not null } or { ExpressionBody: not null })
                IlBodies++;
            EmitIlBlock(rest);
        }
        else if (ctor.Body != null)
        {
            LegacyBodies++;
            WarnIfStructInLegacyBody(model, ctor.Body);
            foreach (var stmt in ctor.Body.Statements)
                VisitStatement(model, stmt);
        }
        AppendLine("return self");
        _indent--;
        AppendLine("end");
        AppendLine();
    }

    // 型別 copy 関数。struct-in-struct は再帰 copy (shallow だと copy 経由の
    // 部分書き込みが alias する)。readonly struct の member は不変なので共有
    private void EmitStructCopyFunction(string name, INamedTypeSymbol? symbol)
    {
        _currentType?.DefinitionKeys.Add("__copy");
        AppendLine($"function {name}.__copy(s)");
        _indent++;
        var fields = ValueMembers(symbol).Select(m =>
        {
            var deep = IsUserStruct(m.Type)
                && m.Type is not INamedTypeSymbol { IsReadOnly: true };
            return deep
                ? $"{m.Name} = {m.Type.Name}.__copy(s.{m.Name})"
                : $"{m.Name} = s.{m.Name}";
        });
        AppendLine($"return {{{string.Join(", ", fields)}}}");
        _indent--;
        AppendLine("end");
        AppendLine();
    }

    // 値を構成する member (positional prop の backing field 込み、
    // EqualityContract のような合成 property は field を持たないので除外される)
    private static IEnumerable<(string Name, ITypeSymbol Type)> ValueMembers(
        ITypeSymbol? type)
    {
        if (type == null) yield break;
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsStatic || field.IsConst) continue;
            // IL builder が写像後の名前で参照するので、field 名も Lua 側の名前で返す
            yield return field.AssociatedSymbol is IPropertySymbol prop
                ? (LuaNaming.MemberName(prop), field.Type)
                : (LuaNaming.MemberName(field), field.Type);
        }
    }

    private static IEnumerable<string> ValueEqualityParts(ITypeSymbol type,
        string a, string b)
    {
        foreach (var (name, memberType) in ValueMembers(type))
        {
            if (IsUserStruct(memberType))
            {
                foreach (var inner in ValueEqualityParts(memberType,
                    $"{a}.{name}", $"{b}.{name}"))
                    yield return inner;
            }
            else
            {
                yield return $"{a}.{name} == {b}.{name}";
            }
        }
    }

    // struct 値が legacy fallback 経路に流れると copy 意味論が消えるため、
    // silent wrong-code にせず診断する (値型対応の安全網)
    private void WarnIfStructInLegacyBody(SemanticModel model, SyntaxNode body)
    {
        var offender = body.DescendantNodesAndSelf()
            .FirstOrDefault(n =>
                (n is ObjectCreationExpressionSyntax or VariableDeclarationSyntax
                    or ParameterSyntax)
                && n switch
                {
                    ObjectCreationExpressionSyntax oc =>
                        IsUserStruct(model.GetTypeInfo(oc).Type),
                    VariableDeclarationSyntax vd =>
                        IsUserStruct(model.GetTypeInfo(vd.Type).Type),
                    ParameterSyntax { Type: { } pt } =>
                        IsUserStruct(model.GetTypeInfo(pt).Type),
                    _ => false,
                });
        if (offender != null)
            _ = WarnUnsupported(offender, "struct value in legacy-emitted body");
    }

    internal static bool IsUserStruct(ITypeSymbol? type) =>
        type is { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None }
        && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
        && type.TypeKind != TypeKind.Enum
        && type.Locations.Any(l => l.IsInSource);

    // 値型の copy 地点 (il-spec §10): 代入 / 引数 / return / 値文脈読み。
    // fresh な値 (object creation / initializer IIFE / with 式 / copy 済み) は
    // 他から参照されないので copy 不要。readonly (record) struct は不変で
    // alias が観測不能なため copy を全省略する
    private IlExpr WrapStructCopy(SemanticModel model, ExpressionSyntax src,
        IlExpr built)
    {
        var type = model.GetTypeInfo(src).Type;
        if (!IsUserStruct(type)
            || type is INamedTypeSymbol { IsReadOnly: true })
            return built;
        if (built is IlNewObj or IlIife or IlStructCopy or IlWith)
            return built;
        // `new S(args)` は S.ctor の IlCall に降りるが、結果は常に fresh
        if (src is BaseObjectCreationExpressionSyntax)
            return built;
        if (IsFreshUserCallResult(model, src))
            return built;
        return new IlStructCopy(built, type!.Name);
    }

    // source で宣言した method / operator の戻り値は callee の return (copy
    // 地点) で copy 済み (または fresh) なので、呼び出し側で重ねて copy
    // しない (`p = p + v` の確保を 1 回にする)。runtime / BCL / --ref の関数
    // (List.FirstOrDefault 等) は要素 alias を返し得るので対象外
    private bool IsFreshUserCallResult(SemanticModel model, ExpressionSyntax src)
    {
        while (src is ParenthesizedExpressionSyntax paren)
            src = paren.Expression;
        if (src is not (InvocationExpressionSyntax or BinaryExpressionSyntax
                or PrefixUnaryExpressionSyntax))
            return false;
        return model.GetSymbolInfo(src).Symbol is IMethodSymbol
            {
                MethodKind: MethodKind.Ordinary or MethodKind.UserDefinedOperator,
            } method
            && method.DeclaringSyntaxReferences.Length > 0
            && !IsReferenceOnlyType(method.ContainingType);
    }

    // struct 値をその場で書き換える代入先: ref parameter と struct method の
    // `this`。Lua の local を差し替えても呼び出し側へ届かないため、
    // 型名を返して S.__assign(target, value) の in-place 書き戻しにさせる
    private static string? ByReferenceStructTarget(SemanticModel model,
        ExpressionSyntax target)
    {
        while (target is ParenthesizedExpressionSyntax paren)
            target = paren.Expression;
        var type = target switch
        {
            IdentifierNameSyntax id when model.GetSymbolInfo(id).Symbol
                is IParameterSymbol { RefKind: RefKind.Ref } p => p.Type,
            ThisExpressionSyntax => model.GetTypeInfo(target).Type,
            _ => null,
        };
        return IsUserStruct(type) ? type!.Name : null;
    }

    // struct method/accessor の receiver 規則: C# の「変数」
    // (local / param / field / 配列要素 / this) なら直渡しで変異が変数に残り、
    // rvalue (property / List indexer / 呼び出し結果等) はコピーへの変異 =
    // 捨てられる。どちらも C# と一致する
    // in / ref readonly parameter は読み取り専用の変数で、C# は変更系 member
    // 呼び出しを防御コピーに対して行う (その field 経由の呼び出しも同じ)
    private static bool IsStructVariableReceiver(SemanticModel model,
        ExpressionSyntax expr) => expr switch
    {
        ThisExpressionSyntax => true,
        IdentifierNameSyntax id => model.GetSymbolInfo(id).Symbol switch
        {
            IParameterSymbol p => p.RefKind
                is not (RefKind.In or RefKind.RefReadOnlyParameter),
            ILocalSymbol or IFieldSymbol => true,
            _ => false,
        },
        MemberAccessExpressionSyntax ma =>
            model.GetSymbolInfo(ma).Symbol is IFieldSymbol
            && !IsReadOnlyParameterRooted(model, ma.Expression),
        ElementAccessExpressionSyntax ea =>
            model.GetSymbolInfo(ea).Symbol is not IPropertySymbol,
        ParenthesizedExpressionSyntax paren =>
            IsStructVariableReceiver(model, paren.Expression),
        _ => false,
    };

    private static bool IsReadOnlyParameterRooted(SemanticModel model,
        ExpressionSyntax expr)
    {
        while (true)
        {
            switch (expr)
            {
                case ParenthesizedExpressionSyntax paren:
                    expr = paren.Expression;
                    continue;
                case MemberAccessExpressionSyntax ma
                    when model.GetSymbolInfo(ma).Symbol is IFieldSymbol:
                    expr = ma.Expression;
                    continue;
                case IdentifierNameSyntax id:
                    return model.GetSymbolInfo(id).Symbol is IParameterSymbol
                    {
                        RefKind: RefKind.In or RefKind.RefReadOnlyParameter,
                    };
                default:
                    return false;
            }
        }
    }

    private IlExpr StructReceiverArg(SemanticModel model,
        ExpressionSyntax recvSyntax, IlExpr recv) =>
        IsStructVariableReceiver(model, recvSyntax)
            ? recv
            : WrapStructCopy(model, recvSyntax, recv);
}
