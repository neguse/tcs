using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TinyCs;

// module の型の static initializer の依存 (EmittedTypeInfo.InitDependencies)。
// snapshot の init_order (fresh VM の初期化順) の元になる。
public sealed partial class IncrementalCompilationSession
{
    // 前回の依存解析で本文を辿った method (StaticInitDependencies.MethodKey)
    private HashSet<string> _initClosure = [];

    // static initializer の依存 (StaticInitOrder) は他 module の method 本文の
    // 先にも及ぶので、emit した module に限らず全 artifact の型で取り直す
    private void RefreshInitDependencies()
    {
        var trees = _moduleOrder.Select(p => _trees[p]).ToList();
        var analysis = new StaticInitDependencies(_compilation!, trees);
        var deps = StaticInitOrder.DependenciesByName(analysis, trees,
            new LuaEmitter().TypeName);
        foreach (var t in _lastGood.Values.SelectMany(a => a.Types))
            t.InitDependencies = deps.GetValueOrDefault(t.Name) ?? [];
        _initClosure = [.. analysis.VisitedMethods.Select(StaticInitDependencies.MethodKey)];
    }

    // fast path の body edit が static initializer から辿る method の本文か
    // (そうでなければ依存は変わらないので取り直さない)
    private bool EditReachesInitializers(SyntaxTree tree, TextSpan bodySpan)
    {
        var node = tree.GetCompilationUnitRoot().FindNode(new TextSpan(bodySpan.Start, 0));
        var member = node.AncestorsAndSelf().FirstOrDefault(n =>
            n is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax);
        return member == null
            || _compilation!.GetSemanticModel(tree).GetDeclaredSymbol(member)
                is not IMethodSymbol method
            || _initClosure.Contains(StaticInitDependencies.MethodKey(method));
    }
}
