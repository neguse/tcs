using System.Text;
using TinyCs;

namespace TinyCs.Tcs2c;

// 制御フロー文の emit (if / while / repeat / numeric for / foreach)。
internal sealed partial class CEmitter
{
    private void EmitIf(IlIf conditional)
    {
        for (var i = 0; i < conditional.Arms.Length; i++)
        {
            var (condition, body) = conditional.Arms[i];
            RequireType(CType.Bool, TypeOf(condition), "if condition");
            Line($"{(i == 0 ? "if" : "else if")} ({RenderExpr(condition)}) {{");
            _indent++;
            PushScope();
            EmitStats(body.Stats);
            PopScope();
            _indent--;
            Line("}");
        }
        if (conditional.Else is not null)
        {
            Line("else {");
            _indent++;
            PushScope();
            EmitStats(conditional.Else.Stats);
            PopScope();
            _indent--;
            Line("}");
        }
    }

    private void EmitNumericFor(IlNumericFor loop)
    {
        RequireType(CType.I32, TypeOf(loop.Start), "numeric for start");
        RequireType(CType.I32, TypeOf(loop.Limit), "numeric for limit");
        var start = Temp("for_start");
        var limit = Temp("for_limit");
        var variable = new Variable($"v_{Names.Id(loop.Var)}_{_serial++}", CType.I32);
        Line("{");
        _indent++;
        Line($"int32_t {start} = {RenderExpr(loop.Start)};");
        Line($"int32_t {limit} = {RenderExpr(loop.Limit)};");
        Line($"for (int32_t {variable.CName} = {start}; {variable.CName} <= {limit}; " +
            $"{variable.CName} = {variable.CName} + INT32_C(1)) {{");
        _indent++;
        PushScope();
        AddVariable(loop.Var, variable);
        _continueTargets.Push(null);
        EmitStats(loop.Body.Stats);
        _continueTargets.Pop();
        PopScope();
        _indent--;
        Line("}");
        _indent--;
        Line("}");
    }

    private void EmitForeachList(IlForeachList loop)
    {
        var sequenceType = TypeOf(loop.Coll);
        if (sequenceType.Kind is not (CTypeKind.Array or CTypeKind.List)
            || sequenceType.Element is null)
            throw new Tcs2cException($"IlForeachList requires a typed array/List, got " +
                sequenceType);
        var sequence = Temp("foreach_sequence");
        var length = Temp("foreach_length");
        var index = Temp("foreach_index");
        var variable = new Variable($"v_{Names.Id(loop.Var)}_{_serial++}",
            sequenceType.Element);
        var lengthFunction = sequenceType.Kind == CTypeKind.Array
            ? "tcs_array_length" : "tcs_list_length";
        var atFunction = sequenceType.Kind == CTypeKind.Array
            ? "tcs_array_at" : "tcs_list_at";

        Line("{");
        _indent++;
        Line($"{sequenceType.CName} {sequence} = {RenderExpr(loop.Coll)};");
        Line($"int32_t {length} = {lengthFunction}({sequence});");
        Line($"for (int32_t {index} = 0; {index} < {length}; {index}++) {{");
        _indent++;
        PushScope();
        AddVariable(loop.Var, variable);
        _continueTargets.Push(null);
        Line($"{sequenceType.ElementCName} {variable.CName} = " +
            $"*({sequenceType.ElementCName} *){atFunction}({sequence}, {index});");
        EmitStats(loop.Body.Stats);
        _continueTargets.Pop();
        PopScope();
        _indent--;
        Line("}");
        _indent--;
        Line("}");
    }

    private void EmitWhile(IlWhile loop)
    {
        RequireType(CType.Bool, TypeOf(loop.Cond), "while condition");
        var label = loop.Trailer is null ? null : Temp("continue");
        Line($"while ({RenderExpr(loop.Cond)}) {{");
        _indent++;
        _continueTargets.Push(label);
        PushScope();
        if (label is not null) Line("{");
        if (label is not null) _indent++;
        EmitStats(loop.Body.Stats);
        if (label is not null) _indent--;
        if (label is not null) Line("}");
        PopScope();
        if (label is not null)
        {
            Line($"{label}:");
            PushScope();
            EmitStats(loop.Trailer!.Stats);
            PopScope();
            Line(";");
        }
        _continueTargets.Pop();
        _indent--;
        Line("}");
    }

    private void EmitRepeat(IlRepeat repeat)
    {
        RequireType(CType.Bool, TypeOf(repeat.Cond), "repeat condition");
        Line("do {");
        _indent++;
        _continueTargets.Push(null);
        PushScope();
        EmitStats(repeat.Body.Stats);
        PopScope();
        _continueTargets.Pop();
        _indent--;
        Line($"}} while ({RenderExpr(repeat.Cond)});");
    }

    private void EmitContinue()
    {
        if (_continueTargets.Count == 0)
            throw new Tcs2cException("continue outside a loop");
        var target = _continueTargets.Peek();
        Line(target is null ? "continue;" : $"goto {target};");
    }

    // Dictionary の foreach。反復順は Lua (pairs) と一致しない — どちらも
    // 順序未規定 (順序依存の出力は backend 間一致の対象外)
    private void EmitForeachDict(IlForeachDict loop)
    {
        var valueType = RequireDict(loop.Coll, out var dictType);
        var dictTemp = Temp("dict");
        var bucketTemp = Temp("bucket");
        var nodeTemp = Temp("node");
        Line("{");
        _indent++;
        PushScope();
        Line($"TcsDict *{dictTemp} = ({{ TcsDict *d = " +
            $"{RenderExpr(loop.Coll)}; (TcsDict *)tcs_nonnull(d); }});");
        Line($"for (size_t {bucketTemp} = 0; {bucketTemp} < {dictTemp}->bucket_count; " +
            $"{bucketTemp}++)");
        Line($"for (TcsDictNode *{nodeTemp} = {dictTemp}->buckets[{bucketTemp}]; " +
            $"{nodeTemp} != NULL; {nodeTemp} = {nodeTemp}->next)");
        Line("{");
        _indent++;
        PushScope();
        AddVariable(loop.Var, new Variable(nodeTemp,
            CType.Kvp(dictType.Key!, valueType)));
        _continueTargets.Push(null);
        EmitStats(loop.Body.Stats);
        _continueTargets.Pop();
        PopScope();
        _indent--;
        Line("}");
        PopScope();
        _indent--;
        Line("}");
    }

}
