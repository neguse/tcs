using Microsoft.CodeAnalysis;

namespace TinyCs;

// --trace: 実行した C# 行の記録 (開発用)。origin の path が TracePrefixes の
// どれかで始まる文の直前に、現在行を関数 local の __tcs_at に書き、global の
// __tcs_trace[__tcs_at] = true を立てる。host は __tcs_trace の key
// (`<file>:<line>`) を読んで in place で消し、各 Lua frame の local __tcs_at を
// debug.getlocal / lua_getlocal で読んで C# の呼び出し元を組み立てる。
// 対象外: inline に描く文 (IIFE / 式本体の lambda)、top-level 文、ctor 先頭の
// table constructor に畳まれる field 代入
public partial class LuaEmitter
{
    // '/' 区切りに正規化した path の prefix。空なら trace しない (出力は不変)
    public IReadOnlyList<string> TracePrefixes { get; set; } = [];

    // emit 中の関数が trace 対象ファイルのものか (lambda は外側の関数に従う)
    private bool _traceFunction;

    public static string NormalizeTracePath(string path) => path.Replace('\\', '/');

    private bool IsTracedPath(string path)
    {
        var normalized = NormalizeTracePath(path);
        return TracePrefixes.Any(p => normalized.StartsWith(p, StringComparison.Ordinal));
    }

    // 関数本体の先頭 (parameter default より前、どの goto / label より前) で
    // 呼ぶ。__tcs_at はその関数の local なので、frame ごとに現在行を持つ
    private void BeginTraceFunction(SemanticModel model)
    {
        _traceFunction = TracePrefixes.Count > 0 && IsTracedPath(model.SyntaxTree.FilePath);
        if (_traceFunction) AppendLine(TraceLocal);
    }

    private void EndTraceFunction() => _traceFunction = false;

    private const string TraceLocal = "local __tcs_at";

    private void EmitTracePoint(SyntaxNode origin)
    {
        if (!_traceFunction) return;
        var span = origin.GetLocation().GetLineSpan();
        var key = $"{NormalizeTracePath(span.Path)}:{span.StartLinePosition.Line + 1}";
        AppendLine($"__tcs_at = {EscapeLuaString(key)}; __tcs_trace[__tcs_at] = true");
        // 文そのものも source map に載せる (trace 行が _currentSource を消費した)
        SetSource(origin);
    }
}
