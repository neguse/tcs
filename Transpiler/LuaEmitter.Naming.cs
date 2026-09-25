using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyCs;

// 名前の写像 (LuaNaming) を emit の各所から引くための helper。
public partial class LuaEmitter
{
    // 型 table を chunk の local に置く (issue #12)。method 本文からの型参照
    // (`V.new` / static field) が _ENV lookup でなく upvalue になる。宣言は
    // local へ代入してから同名 global にも publish する (他 chunk / host から
    // の参照は従来どおり)。hot reload (EmitInstanceRegistry) は reload chunk
    // が global を旧 identity へ付け替えて method 本文の参照を旧 table へ
    // 解決させる設計なので使わない。module artifact 経路も使わない
    public bool CacheTypeLocals { get; init; }
    private readonly HashSet<string> _typeLocals = new(StringComparer.Ordinal);

    // Lua の local 上限 (関数あたり 200) に対する余裕。top-level 文の local と
    // --prelude の local も同じ chunk に載るため、型数と top-level 文の local
    // 数の合計がこれを超える program では使わない (global 参照のまま)
    private const int MaxChunkLocalsForTypeCache = 120;

    private void EmitTypeLocals(CSharpCompilation compilation)
    {
        if (!CacheTypeLocals || EmitInstanceRegistry) return;
        var names = new List<string>();
        var topLevelLocals = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (ReferenceTrees.Contains(tree)) continue;
            var root = tree.GetCompilationUnitRoot();
            foreach (var type in TypeTableDeclarations(root.Members))
            {
                var name = type.Identifier.ValueText;
                if (!names.Contains(name)) names.Add(name);
            }
            topLevelLocals += root.Members.OfType<GlobalStatementSyntax>()
                .SelectMany(g => g.DescendantNodes())
                .Count(n => n is VariableDeclaratorSyntax
                    or ForEachStatementSyntax
                    or SingleVariableDesignationSyntax);
        }
        if (names.Count == 0
            || names.Count + topLevelLocals > MaxChunkLocalsForTypeCache)
            return;
        _typeLocals.UnionWith(names);
        // 右辺は chunk 実行開始時の global (未 emit の型は従来の global 参照と
        // 同じ値になる)。emit する型は宣言サイトで新しい table に差し替わる
        var list = string.Join(", ", names);
        AppendLine($"local {list} = {list}");
    }

    // global table として emit される宣言 (namespace は透過)。診断済みの
    // 宣言は emit されないので除く。名前が Lua の識別子にならないものも除く
    private static IEnumerable<BaseTypeDeclarationSyntax> TypeTableDeclarations(
        IEnumerable<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    foreach (var inner in TypeTableDeclarations(ns.Members))
                        yield return inner;
                    break;
                case ClassDeclarationSyntax or RecordDeclarationSyntax
                    or StructDeclarationSyntax or EnumDeclarationSyntax
                    when !TinyCsComplianceFacts.TryGetUnsupportedSyntax(member, out _)
                        && IsPlainLuaName(((BaseTypeDeclarationSyntax)member)
                            .Identifier.ValueText):
                    yield return (BaseTypeDeclarationSyntax)member;
                    break;
            }
        }
    }

    private static bool IsPlainLuaName(string s) =>
        s.Length > 0 && (char.IsAsciiLetter(s[0]) || s[0] == '_')
        && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
        && LuaNaming.Local(s) == s;

    // 型 table の宣言。chunk local 化している型は global へも publish する
    private void EmitTypeTable(string name)
    {
        AppendLine($"{name} = {{}}");
        if (_typeLocals.Contains(name))
            AppendLine($"_ENV.{name} = {name}");
    }

    /// <summary>local 束縛 (local / parameter / pattern 等) の Lua 側表記。</summary>
    private static string L(string csharpName) => LuaNaming.Local(csharpName);

    /// <summary>member 名の Lua 側表記。</summary>
    private static string N(string csharpName) => LuaNaming.Member(csharpName);

    /// <summary>symbol の Lua 側 member 名 (enum メンバは UPPER_SNAKE)。</summary>
    private static string N(ISymbol symbol) => LuaNaming.MemberName(symbol);

    /// <summary>static アクセスの型参照。参照専用型は小文字パス、ユーザ型は
    /// そのままの型名。</summary>
    private string TypeRef(INamedTypeSymbol? type) =>
        type == null ? "" :
        IsReferenceOnlyType(type) ? LuaNaming.RefTypePath(type) : type.Name;

    /// <summary>C# の const (enum メンバ以外) は値を inline する。</summary>
    private static string? ConstLiteral(ISymbol? symbol)
    {
        if (symbol is not IFieldSymbol { HasConstantValue: true } f
            || f.ContainingType?.TypeKind == TypeKind.Enum)
            return null;
        return f.ConstantValue switch
        {
            null => "nil",
            bool b => b ? "true" : "false",
            string s => EscapeLuaString(s),
            char c => EscapeLuaString(c.ToString()),
            float x => FormatLuaNumber(x),
            double x => FormatLuaNumber(x),
            decimal x => FormatLuaNumber((double)x),
            var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "nil",
        };
    }

    private static string FormatLuaNumber(double x)
    {
        if (double.IsNaN(x)) return "(0/0)";
        if (double.IsPositiveInfinity(x)) return "math.huge";
        if (double.IsNegativeInfinity(x)) return "(-math.huge)";
        var text = x.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('e')
            ? text : text + ".0";
    }

    // 同じ型の中で写像後の名前が衝突するメンバ (`Foo` と `foo`) を警告する。
    private void WarnLuaNameCollisions(TypeDeclarationSyntax type)
    {
        var seen = new Dictionary<string, (string Name, SyntaxToken Token)>(StringComparer.Ordinal);
        foreach (var (name, token) in EnumerateMemberNames(type))
        {
            var lua = LuaNaming.Member(name);
            if (seen.TryGetValue(lua, out var prev))
            {
                if (prev.Name == name) continue; // overload は同名で衝突しない
                var loc = token.GetLocation().GetLineSpan();
                var file = string.IsNullOrEmpty(loc.Path) ? "" : loc.Path;
                Warnings.Add($"{file}({loc.StartLinePosition.Line + 1}," +
                    $"{loc.StartLinePosition.Character + 1}): naming: " +
                    $"'{prev.Name}' and '{name}' both map to Lua '{lua}'");
            }
            else
            {
                seen[lua] = (name, token);
            }
        }
    }

    private static IEnumerable<(string Name, SyntaxToken Token)> EnumerateMemberNames(
        TypeDeclarationSyntax type)
    {
        if (type is RecordDeclarationSyntax { ParameterList: not null } rec)
            foreach (var p in rec.ParameterList.Parameters)
                yield return (p.Identifier.ValueText, p.Identifier);
        foreach (var member in type.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                        yield return (v.Identifier.ValueText, v.Identifier);
                    break;
                case PropertyDeclarationSyntax prop:
                    yield return (prop.Identifier.ValueText, prop.Identifier);
                    break;
                case MethodDeclarationSyntax method:
                    yield return (method.Identifier.ValueText, method.Identifier);
                    break;
            }
        }
    }
}
