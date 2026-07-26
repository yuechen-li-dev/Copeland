using Copeland.TS.Mir;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.TS.Backend.CSharp;

public static class CSharpBackend
{
    private enum AsyncStateKind
    {
        Statement,
        Return,
        Branch,
        Jump,
        Await,
        Evaluate,
        Propagate,
    }

    private sealed class HandlerTransfer(string errorTemporary, string label)
    {
        public string ErrorTemporary { get; } = errorTemporary;
        public string Label { get; } = label;
    }

    private sealed class AsyncState(
        int id,
        AsyncStateKind kind,
        MirStatement? statement,
        int nextState,
        int thenState = -1,
        int elseState = -1,
        MirFrameSlotId? awaitedComputationSlot = null,
        MirExpression? condition = null,
        MirFrameSlotId? valueSlot = null,
        MirExpression? expression = null,
        MirPropagationTarget? propagationTarget = null,
        int handlerState = -1,
        MirFrameSlotId? handlerErrorSlot = null)
    {
        public int Id { get; } = id;
        public AsyncStateKind Kind { get; } = kind;
        public MirStatement? Statement { get; } = statement;
        public int NextState { get; } = nextState;
        public int ThenState { get; } = thenState;
        public int ElseState { get; } = elseState;
        public MirFrameSlotId? AwaitedComputationSlot { get; } = awaitedComputationSlot;
        public MirExpression? Condition { get; } = condition;
        public MirFrameSlotId? ValueSlot { get; } = valueSlot;
        public MirExpression? Expression { get; } = expression;
        public MirPropagationTarget? PropagationTarget { get; } = propagationTarget;
        public int HandlerState { get; } = handlerState;
        public MirFrameSlotId? HandlerErrorSlot { get; } = handlerErrorSlot;
    }

    private sealed class CSharpEmissionState(IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records)
    {
        public Dictionary<MirHandlerId, HandlerTransfer> Handlers { get; } = [];
        public Stack<MirExpression?> ContinueIncrements { get; } = new();
        public IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> Records { get; } = records;
        public int TryIndex { get; set; }
    }

    private static readonly AsyncLocal<CSharpEmissionState?> CurrentEmissionState = new();
    private static readonly AsyncLocal<string?> CurrentSourcePath = new();

    public static CSharpCompilation Emit(MirProgram program)
        => EmitCore(program, null);

    /// <summary>
    /// Emits a root application and binds targetless tsonCall to the one
    /// manifest-declared default sidecar. This is the production emission seam.
    /// </summary>
    public static CSharpCompilation EmitForRootManifest(MirProgram program, Copeland.TS.Manifest.CopelandManifest manifest)
    {
        CSharpSidecarContract? contract = null;
        if (ProgramUsesTsonTransport(program)
            && !CSharpSidecarContracts.TryCreate(program, manifest, out contract, out CSharpDiagnostic? diagnostic))
        {
            return new CSharpCompilation(string.Empty, [diagnostic!]);
        }

        return EmitCore(program, ProgramUsesTsonTransport(program) ? contract : null);
    }

    private static CSharpCompilation EmitCore(MirProgram program, CSharpSidecarContract? sidecarContract)
    {
        var diagnostics = MirValidator.Validate(program)
            .Select(diagnostic => new CSharpDiagnostic("COPE-CS-0002", $"Invalid MIR: {diagnostic.Message}"))
            .ToList();
        if (diagnostics.Count > 0)
        {
            return new CSharpCompilation(string.Empty, diagnostics);
        }
        foreach (MirNpmImport import in program.NpmImports.Where(import => !import.IsAvailableToClrSidecar))
        {
            diagnostics.Add(new CSharpDiagnostic("COPE-CS-0001", $"npm import '{import.LocalBinding}' is unavailable for the CLR sidecar backend."));
        }
        if (diagnostics.Count > 0)
        {
            return new CSharpCompilation(string.Empty, diagnostics);
        }
        var writer = new CSharpTextWriter();
        string? previousSourcePath = CurrentSourcePath.Value;
        CurrentSourcePath.Value = program.CSharpSourcePath;
        var enumNames = program.Enums.Select(@enum => @enum.Name).ToHashSet(StringComparer.Ordinal);
        var recordsById = program.Records.ToDictionary(record => record.Id);
        var tsonTableIds = program.TsonEncodingPlans
            .Where(plan => plan.TablePlan is not null)
            .Select(plan => plan.TablePlan!.TableId)
            .ToHashSet();
        var usesResult = ProgramUsesResult(program) || program.Tables.Count > 0;
        var usesUnwrap = ProgramUsesUnwrap(program);
        var needsUnit = EnumerateTypes(program).Any(ContainsVoidResult);
        var errorTypes = CollectErrorNominalTypes(program, enumNames);
        var usesAsync = program.Functions.Any(function => function.IsAsync);
        var usesGenerators = program.Functions.Any(function => function.IsGenerator);
        var usesTsonTransport = ProgramUsesTsonTransport(program);
        var usesSystemTextJson = ProgramUsesSystemTextJson(program);
        var usesBatch = ProgramUsesBatch(program);

        writer.WriteLine("// <auto-generated />"); writer.WriteLine("#nullable enable"); writer.WriteLine();
        foreach (string @namespace in program.CSharpUsings)
        {
            writer.WriteLine($"using {@namespace};");
        }
        if (program.CSharpUsings.Count > 0)
        {
            writer.WriteLine();
        }
        writer.WriteLine("namespace Copeland.Generated;"); writer.WriteLine();
        if (needsUnit) EmitUnit(writer);
        if (usesResult) EmitResult(writer);
        if (usesAsync) EmitAsyncRuntime(writer);
        if (usesGenerators) EmitGeneratorRuntime(writer);
        if (usesTsonTransport) EmitTsonTransportRuntime(writer, sidecarContract);
        foreach (var errorType in errorTypes) writer.WriteLine($"public readonly record struct {CSharpNameMangler.Mangle(errorType)};");
        if (errorTypes.Count > 0) writer.WriteLine();
        foreach (var record in program.Records) EmitRecord(writer, record, usesSystemTextJson);
        if (program.Records.Count > 0) writer.WriteLine();
        foreach (var mirEnum in program.Enums) EmitEnum(writer, mirEnum);
        if (program.Enums.Count > 0) writer.WriteLine();
        foreach (MirFlowDefinition flow in program.Flows)
        {
            EmitFlow(writer, flow, enumNames, diagnostics);
            writer.WriteLine();
        }
        EmitCallableDelegates(writer, program);
        EmitCapturedCallableEnvironments(writer, program);
        if (program.Tables.Count > 0)
        {
            EmitColumnSupport(writer);
            foreach (var table in program.Tables)
            {
                EmitTable(writer, table, recordsById, tsonTableIds.Contains(table.Id));
            }
        }
        writer.WriteLine("public static class CopelandModule"); writer.WriteLine("{"); writer.Indent();
        foreach (var table in program.Tables)
        {
            writer.WriteLine($"private static readonly {TableTypeName(table.Id)} {TableSingletonName(table.Id)} = {TableTypeName(table.Id)}.Create();");
        }
        if (program.Tables.Count > 0)
        {
            writer.WriteLine();
        }
        if (usesUnwrap)
        {
            EmitUnwrapPanic(writer);
        }
        if (usesBatch)
        {
            EmitBatchTestSeam(writer);
        }
        if (program.TsonEncodingPlans.Count > 0)
        {
            EmitTsonEncodingRuntime(writer, program.TsonEncodingPlans, recordsById, usesTsonTransport);
        }
        foreach (var function in program.Functions)
        {
            if (function.IsAsync)
            {
                EmitAsyncFrame(writer, function);
            }
        }
        foreach (var function in program.Functions) EmitFunction(writer, function, enumNames, recordsById, diagnostics);
        writer.Unindent(); writer.WriteLine("}");
        CurrentSourcePath.Value = previousSourcePath;
        return diagnostics.Count == 0
            ? new CSharpCompilation(writer.ToString(), diagnostics, sidecarContract)
            : new CSharpCompilation(string.Empty, diagnostics);
    }

    private static void EmitFlow(CSharpTextWriter writer, MirFlowDefinition flow, IReadOnlySet<string> enumNames, List<CSharpDiagnostic> diagnostics)
    {
        string flowName = CSharpNameMangler.Mangle(flow.Name);
        string boardType = RecordTypeName(flow.BoardType.RecordTypeId);
        string resultType = flowName + "TransitionResult";
        var expressionContext = new MirFunction("<flow>", [], new MirNamedType("void"), [], []);
        writer.WriteLine($"public readonly record struct {resultType}(string Kind, string FromState, string? ToState, string Event, long Revision, bool IsTerminal, object? Error);");
        writer.WriteLine($"public static class {flowName}");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"public static Session Start() => new Session();");
        writer.WriteLine("public sealed class Session");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"private {boardType} board;");
        writer.WriteLine($"private string state = {CSharpLiteralWriter.Write(flow.InitialState)};");
        writer.WriteLine("private bool terminal;");
        writer.WriteLine("private bool sending;");
        writer.WriteLine("private long revision;");
        int initializerTempIndex = 0;
        string initializers = string.Join(", ", flow.BoardFields.Select(field => EmitExpression(writer, field.Initializer, expressionContext, enumNames, ref initializerTempIndex, diagnostics)));
        writer.WriteLine($"internal Session() {{ board = new {boardType}({initializers}); }}");
        writer.WriteLine($"public {boardType} Board => board;");
        writer.WriteLine("public string State => state;");
        writer.WriteLine("public bool IsTerminal => terminal;");
        writer.WriteLine("public long Revision => revision;");
        foreach (MirFlowEvent @event in flow.Events)
        {
            EmitFlowSend(writer, flow, @event, resultType, expressionContext, enumNames, diagnostics);
        }
        foreach (MirFlowState state in flow.States.Where(state => state.Terminal is not null))
        {
            EmitFlowTerminal(writer, flow, state, resultType, expressionContext, enumNames, diagnostics);
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitFlowSend(CSharpTextWriter writer, MirFlowDefinition flow, MirFlowEvent @event, string resultType, MirFunction expressionContext, IReadOnlySet<string> enumNames, List<CSharpDiagnostic> diagnostics)
    {
        string parameters = string.Join(", ", @event.Payloads.Select(parameter => MapValueStorageType(parameter.Type) + " " + CSharpNameMangler.Mangle(parameter.Name)));
        writer.WriteLine($"public {resultType} Send{CSharpNameMangler.Mangle(@event.Name)}({parameters})");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (sending) throw new global::System.InvalidOperationException(\"A Copeland flow session cannot receive a reentrant event.\");");
        writer.WriteLine("if (terminal) return new(" + CSharpLiteralWriter.Write("Terminal") + ", state, null, " + CSharpLiteralWriter.Write(@event.Name) + ", revision, true, null);");
        writer.WriteLine("sending = true;");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("switch (state)");
        writer.WriteLine("{");
        writer.Indent();
        foreach (MirFlowState state in flow.States)
        {
            MirFlowTransition? transition = state.Transitions.SingleOrDefault(candidate => candidate.EventName == @event.Name);
            if (transition is null) continue;
            writer.WriteLine($"case {CSharpLiteralWriter.Write(state.Name)}:");
            writer.Indent();
            int tempIndex = 0;
            if (transition.Guard is not null)
            {
                string guard = EmitExpression(writer, transition.Guard, expressionContext, enumNames, ref tempIndex, diagnostics);
                writer.WriteLine($"if (!({guard})) return new({CSharpLiteralWriter.Write("Unhandled")}, state, null, {CSharpLiteralWriter.Write(@event.Name)}, revision, false, null);");
            }
            writer.WriteLine($"{RecordTypeName(flow.BoardType.RecordTypeId)} nextBoard = this.board;");
            writer.WriteLine($"{RecordTypeName(flow.BoardType.RecordTypeId)} board = nextBoard;");
            foreach (MirFlowBoardUpdate update in transition.Updates)
            {
                string value = EmitExpression(writer, update.Value, expressionContext, enumNames, ref tempIndex, diagnostics);
                string constructorArguments = string.Join(", ", flow.BoardFields.Select(field =>
                    field.Id == update.FieldId
                        ? value
                        : "nextBoard." + RecordFieldName(field.Id)));
                writer.WriteLine($"nextBoard = new {RecordTypeName(flow.BoardType.RecordTypeId)}({constructorArguments});");
                writer.WriteLine("board = nextBoard;");
            }
            writer.WriteLine("string fromState = state;");
            writer.WriteLine("this.board = nextBoard;");
            writer.WriteLine($"state = {CSharpLiteralWriter.Write(transition.TargetState)};");
            writer.WriteLine("revision++;");
            MirFlowState target = flow.States.Single(state => state.Name == transition.TargetState);
            if (target.Terminal is not null)
            {
                writer.WriteLine($"return Enter{CSharpNameMangler.Mangle(target.Name)}(fromState, {CSharpLiteralWriter.Write(@event.Name)});");
            }
            else
            {
                writer.WriteLine($"return new({CSharpLiteralWriter.Write("Transitioned")}, fromState, state, {CSharpLiteralWriter.Write(@event.Name)}, revision, false, null);");
            }
            writer.Unindent();
        }
        writer.WriteLine("default:");
        writer.Indent();
        writer.WriteLine($"return new({CSharpLiteralWriter.Write("Unhandled")}, state, null, {CSharpLiteralWriter.Write(@event.Name)}, revision, false, null);");
        writer.Unindent();
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("sending = false;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitFlowTerminal(CSharpTextWriter writer, MirFlowDefinition flow, MirFlowState state, string resultType, MirFunction expressionContext, IReadOnlySet<string> enumNames, List<CSharpDiagnostic> diagnostics)
    {
        MirFlowTerminal terminal = state.Terminal!;
        int tempIndex = 0;
        string? error = terminal.IsFailure && terminal.Expression is not null
            ? EmitExpression(writer, terminal.Expression, expressionContext, enumNames, ref tempIndex, diagnostics)
            : terminal.IsFailure ? CSharpLiteralWriter.Write("Flow failed.") : "null";
        writer.WriteLine($"private {resultType} Enter{CSharpNameMangler.Mangle(state.Name)}(string fromState, string eventName)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"{RecordTypeName(flow.BoardType.RecordTypeId)} board = this.board;");
        writer.WriteLine("terminal = true;");
        string kind = terminal.IsFailure ? "Failed" : "Completed";
        writer.WriteLine($"return new({CSharpLiteralWriter.Write(kind)}, fromState, state, eventName, revision, true, {(terminal.IsFailure ? error : "null")});");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitBatchTestSeam(CSharpTextWriter writer)
    {
        writer.WriteLine("// Private test seam: never reachable from authored Copeland code.");
        writer.WriteLine("private static global::System.Action? __cope_batch_item_entered_for_testing;");
        writer.WriteLine("private static int __cope_batch_max_degree_for_testing;");
        writer.WriteLine();
    }

    private static void EmitAsyncRuntime(CSharpTextWriter writer)
    {
        writer.WriteLine("public sealed class CopeAsync<T>");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("private readonly global::System.Collections.Generic.List<(global::System.Action Success, global::System.Action Cancelled, global::System.Action Failed, global::System.Action Panicked)> continuations = []; ");
        writer.WriteLine("private int terminalState;");
        writer.WriteLine("public bool IsCompleted => terminalState != 0;");
        writer.WriteLine("public bool IsCancelled => terminalState == 2;");
        writer.WriteLine("public bool IsPanicked => terminalState == 3;");
        writer.WriteLine("public bool IsTransportFailed => terminalState == 4;");
        writer.WriteLine("public T Value { get; private set; } = default!;");
        writer.WriteLine("public bool Subscribe(global::System.Action success, global::System.Action cancelled, global::System.Action panicked) => SubscribeTransport(success, cancelled, panicked, panicked);");
        writer.WriteLine("public bool SubscribeTransport(global::System.Action success, global::System.Action cancelled, global::System.Action failed, global::System.Action panicked)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (IsCompleted) return true;");
        writer.WriteLine("continuations.Add((success, cancelled, failed, panicked));");
        writer.WriteLine("return false;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("public void Resolve(T value)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (terminalState != 0) return;");
        writer.WriteLine("terminalState = 1;");
        writer.WriteLine("Value = value;");
        writer.WriteLine("var pending = continuations.ToArray();");
        writer.WriteLine("continuations.Clear();");
        writer.WriteLine("foreach (var continuation in pending) continuation.Success();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("public void Cancel()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (terminalState != 0) return;");
        writer.WriteLine("terminalState = 2;");
        writer.WriteLine("var pending = continuations.ToArray();");
        writer.WriteLine("continuations.Clear();");
        writer.WriteLine("foreach (var continuation in pending) continuation.Cancelled();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("public void Fail()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (terminalState != 0) return;");
        writer.WriteLine("terminalState = 4;");
        writer.WriteLine("var pending = continuations.ToArray();");
        writer.WriteLine("continuations.Clear();");
        writer.WriteLine("foreach (var continuation in pending) continuation.Failed();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("public void Panic()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (terminalState != 0) return;");
        writer.WriteLine("terminalState = 3;");
        writer.WriteLine("var pending = continuations.ToArray();");
        writer.WriteLine("continuations.Clear();");
        writer.WriteLine("foreach (var continuation in pending) continuation.Panicked();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("internal static class CopeAsyncPending");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("internal static CopeAsync<T> Create<T>() => new CopeAsync<T>();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitGeneratorRuntime(CSharpTextWriter writer)
    {
        writer.WriteLine("internal sealed class CopeGeneratorEnumerable<T>(global::System.Func<global::System.Collections.Generic.IEnumerator<T>> create) : global::System.Collections.Generic.IEnumerable<T>");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("public global::System.Collections.Generic.IEnumerator<T> GetEnumerator() => new CopeGeneratorEnumerator<T>(create());");
        writer.WriteLine("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("internal sealed class CopeGeneratorEnumerator<T>(global::System.Collections.Generic.IEnumerator<T> inner) : global::System.Collections.Generic.IEnumerator<T>");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("private bool disposed;");
        writer.WriteLine("private bool running;");
        writer.WriteLine("public T Current => inner.Current;");
        writer.WriteLine("object global::System.Collections.IEnumerator.Current => Current!;");
        writer.WriteLine("public bool MoveNext()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (disposed) return false;");
        writer.WriteLine("if (running) throw new global::System.InvalidOperationException(\"A Copeland generator cannot be resumed while it is already running.\");");
        writer.WriteLine("running = true;");
        writer.WriteLine("try { return inner.MoveNext(); }");
        writer.WriteLine("finally { running = false; }");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("public void Reset() => throw new global::System.NotSupportedException();");
        writer.WriteLine("public void Dispose() { if (disposed) return; disposed = true; inner.Dispose(); }");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitTsonTransportRuntime(CSharpTextWriter writer, CSharpSidecarContract? sidecarContract)
    {
        writer.WriteLine("internal static class CopeTsonTransport");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"internal const string BindingId = {CSharpLiteralWriter.Write(sidecarContract?.LogicalBindingId ?? string.Empty)};");
        writer.WriteLine($"internal const string ProtocolVersion = {CSharpLiteralWriter.Write(sidecarContract?.ProtocolVersion ?? CSharpSidecarContracts.ProtocolVersion)};");
        writer.WriteLine($"internal const string ExpectedDigest = {CSharpLiteralWriter.Write(sidecarContract?.ExpectedDigest ?? string.Empty)};");
        writer.WriteLine("private static readonly object Gate = new();");
        writer.WriteLine("private interface IPending { void Receive(string kind, string payload); void Fail(); }");
        writer.WriteLine("private sealed class Pending<T>(CopeAsync<T> computation, global::System.Func<string, string, T> decode) : IPending");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("public void Receive(string kind, string payload)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (kind == \"cancel\") { computation.Cancel(); return; }");
        writer.WriteLine("if (kind == \"failure\") { computation.Fail(); return; }");
        writer.WriteLine("if (kind != \"ok\" && kind != \"remote-error\") { computation.Fail(); return; }");
        writer.WriteLine("try { computation.Resolve(decode(kind, payload)); } catch { computation.Fail(); }");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("public void Fail() => computation.Fail();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("private static readonly global::System.Collections.Generic.Dictionary<string, IPending> PendingByCorrelation = new(global::System.StringComparer.Ordinal);");
        writer.WriteLine("private static long nextCorrelation;");
        writer.WriteLine("internal static global::System.Action<string>? Dispatch;");
        writer.WriteLine("internal static CopeAsync<T> Start<T>(string operation, string request, global::System.Func<string, string, T> decode)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("string correlation = (++nextCorrelation).ToString(global::System.Globalization.CultureInfo.InvariantCulture);");
        writer.WriteLine("var computation = CopeAsyncPending.Create<T>();");
        writer.WriteLine("lock (Gate) { PendingByCorrelation.Add(correlation, new Pending<T>(computation, decode)); }");
        writer.WriteLine("try { Dispatch?.Invoke(Envelope(correlation, \"request\", operation, request)); } catch { FailCorrelation(correlation); }");
        writer.WriteLine("return computation;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("internal static bool Receive(string envelope)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (!TryReadEnvelope(envelope, out string correlation, out string kind, out _, out string payload)) return false;");
        writer.WriteLine("IPending? pending;");
        writer.WriteLine("lock (Gate) { if (!PendingByCorrelation.Remove(correlation, out pending)) return false; }");
        writer.WriteLine("pending.Receive(kind, payload);");
        writer.WriteLine("return true;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("internal static void ConnectionLost()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("global::System.Collections.Generic.List<IPending> pending;");
        writer.WriteLine("lock (Gate) { pending = new global::System.Collections.Generic.List<IPending>(PendingByCorrelation.Values); PendingByCorrelation.Clear(); }");
        writer.WriteLine("foreach (IPending item in pending) item.Fail();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("private static void FailCorrelation(string correlation)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("IPending? pending;");
        writer.WriteLine("lock (Gate) { if (!PendingByCorrelation.Remove(correlation, out pending)) return; }");
        writer.WriteLine("pending.Fail();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("internal static string Envelope(string correlation, string kind, string operation, string payload)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("return \"const $schema: string = \\\"copeland://interop/transport/v1\\\"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope({\\\"correlation\\\":\\\"\" + Escape(correlation) + \"\\\",\\\"kind\\\":\\\"\" + Escape(kind) + \"\\\",\\\"operation\\\":\\\"\" + Escape(operation) + \"\\\",\\\"payload\\\":\\\"\" + Escape(payload) + \"\\\",});\";");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("""
            private static bool TryReadEnvelope(string value, out string correlation, out string kind, out string operation, out string payload)
            {
                const string prefix = "const $schema: string = \"copeland://interop/transport/v1\"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope({";
                int position = 0;
                if (!value.StartsWith(prefix, global::System.StringComparison.Ordinal)
                    || !ReadField(value, ref position, prefix, "\"correlation\":", out correlation)
                    || !ReadField(value, ref position, string.Empty, "\"kind\":", out kind)
                    || !ReadField(value, ref position, string.Empty, "\"operation\":", out operation)
                    || !ReadField(value, ref position, string.Empty, "\"payload\":", out payload)
                    || position > value.Length - 3
                    || string.CompareOrdinal(value, position, "});", 0, 3) != 0
                    || position + 3 != value.Length)
                {
                    correlation = kind = operation = payload = string.Empty;
                    return false;
                }
                return true;
            }

            private static bool ReadField(string value, ref int position, string prefix, string label, out string result)
            {
                result = string.Empty;
                if (prefix.Length > 0) position = prefix.Length;
                if (position > value.Length - label.Length || string.CompareOrdinal(value, position, label, 0, label.Length) != 0) return false;
                position += label.Length;
                if (position >= value.Length || value[position++] != '"') return false;
                var builder = new global::System.Text.StringBuilder();
                while (position < value.Length)
                {
                    char current = value[position++];
                    if (current == '"')
                    {
                        if (position >= value.Length || value[position++] != ',') return false;
                        result = builder.ToString();
                        return true;
                    }
                    if (current != '\\') { builder.Append(current); continue; }
                    if (position >= value.Length) return false;
                    char escape = value[position++];
                    if (escape == 'n') builder.Append('\n');
                    else if (escape == 'r') builder.Append('\r');
                    else if (escape == 't') builder.Append('\t');
                    else if (escape == '"' || escape == '\\') builder.Append(escape);
                    else return false;
                }
                return false;
            }

            private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            """);
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitAsyncFrame(CSharpTextWriter writer, MirFunction function)
    {
        writer.WriteLine($"private sealed class __CopeAsyncFrame_{CSharpNameMangler.Mangle(function.Name)}");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("public int State;");
        foreach (MirParameter parameter in function.Parameters)
        {
            writer.WriteLine($"public {MapValueStorageType(parameter.Type)} {CSharpNameMangler.Mangle(parameter.Name)} = default!;");
        }
        foreach (MirLocal local in function.Locals)
        {
            writer.WriteLine($"public {MapValueStorageType(local.Type)} {CSharpNameMangler.Mangle(local.Name)} = default!;");
        }
        if (function.SuspensionAutomaton is not null)
        {
            foreach (MirFrameSlot slot in function.SuspensionAutomaton.FrameSlots.Where(slot =>
                         slot.Id.Value.StartsWith("await_", StringComparison.Ordinal)
                         || slot.Id.Value.StartsWith("expression_", StringComparison.Ordinal)))
            {
                writer.WriteLine($"public {MapValueStorageType(slot.Type)} __{slot.Id.Value} = default!;");
            }
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitRecord(CSharpTextWriter writer, MirRecordDefinition record, bool usesSystemTextJson)
    {
        string typeName = RecordTypeName(record.Id);
        string parameters = string.Join(
            ", ",
            record.Fields.Select((field, index) => $"{MapType(field.Type)} value{index}"));

        writer.WriteLine($"public sealed class {typeName}");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"internal {typeName}({parameters})");
        writer.WriteLine("{");
        writer.Indent();
        for (int index = 0; index < record.Fields.Count; index++)
        {
            writer.WriteLine($"{RecordFieldName(record.Fields[index].Id)} = value{index};");
        }
        writer.Unindent();
        writer.WriteLine("}");

        foreach (var field in record.Fields)
        {
            writer.WriteLine();
            if (usesSystemTextJson)
            {
                writer.WriteLine("[global::System.Text.Json.Serialization.JsonInclude]");
                writer.WriteLine($"[global::System.Text.Json.Serialization.JsonPropertyName({CSharpLiteralWriter.Write(field.Name)})]");
            }
            string visibility = usesSystemTextJson || (record.IsClass && field.IsPublic) ? "public" : "internal";
            writer.WriteLine($"{visibility} {MapType(field.Type)} {RecordFieldName(field.Id)} {{ get; }}");
        }

        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitUnit(CSharpTextWriter writer)
    {
        writer.WriteLine("public readonly record struct CopeUnit"); writer.WriteLine("{"); writer.Indent();
        writer.WriteLine("public static readonly CopeUnit Value = new();"); writer.Unindent(); writer.WriteLine("}"); writer.WriteLine();
    }

    private static void EmitCallableDelegates(CSharpTextWriter writer, MirProgram program)
    {
        var callables = EnumerateTypes(program)
            .SelectMany(EnumerateCallableTypes)
            .GroupBy(CallableTypeIdentity, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        foreach (var callable in callables)
        {
            string parameters = string.Join(", ", callable.Parameters.Select((parameter, index) => $"{MapValueStorageType(parameter.Type)} value{index}"));
            writer.WriteLine($"public delegate {MapValueStorageType(callable.ReturnType)} {CallableDelegateName(callable)}({parameters});");
        }
        if (callables.Length > 0) writer.WriteLine();
    }

    private static void EmitCapturedCallableEnvironments(CSharpTextWriter writer, MirProgram program)
    {
        var functions = program.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        var constructions = EnumerateCallableConstructions(program)
            .Where(construction => construction.Captures.Count > 0)
            .GroupBy(construction => construction.CodeFunctionName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in constructions)
        {
            MirCallableConstructionExpression construction = group.First();
            if (!functions.TryGetValue(construction.CodeFunctionName, out MirFunction? code))
            {
                continue;
            }

            string environmentName = CapturedCallableEnvironmentName(code.Name);
            writer.WriteLine($"internal sealed class {environmentName}");
            writer.WriteLine("{");
            writer.Indent();
            for (int index = 0; index < construction.Captures.Count; index++)
            {
                writer.WriteLine($"private readonly {MapValueStorageType(code.Parameters[index].Type)} _capture{index};");
            }
            writer.WriteLine();
            string constructorParameters = string.Join(", ", construction.Captures.Select((capture, index) => $"{MapValueStorageType(capture.Type)} capture{index}"));
            writer.WriteLine($"internal {environmentName}({constructorParameters})");
            writer.WriteLine("{");
            writer.Indent();
            for (int index = 0; index < construction.Captures.Count; index++)
            {
                writer.WriteLine($"_capture{index} = capture{index};");
            }
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
            string invokeParameters = string.Join(", ", code.Parameters.Skip(construction.Captures.Count)
                .Select((parameter, index) => $"{MapValueStorageType(parameter.Type)} value{index}"));
            string arguments = string.Join(", ", Enumerable.Range(0, construction.Captures.Count).Select(index => $"_capture{index}")
                .Concat(Enumerable.Range(0, code.Parameters.Count - construction.Captures.Count).Select(index => $"value{index}")));
            writer.WriteLine($"internal {MapValueStorageType(code.ReturnType)} Invoke({invokeParameters}) => CopelandModule.{CSharpNameMangler.Mangle(code.Name)}({arguments});");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

    private static void EmitResult(CSharpTextWriter writer)
    {
        writer.WriteLine("public readonly struct CopeResult<TValue, TError>"); writer.WriteLine("{"); writer.Indent();
        writer.WriteLine("private readonly TValue _value;"); writer.WriteLine("private readonly TError _error;"); writer.WriteLine("public bool IsOk { get; }"); writer.WriteLine();
        writer.WriteLine("public TValue Value => IsOk ? _value : throw new global::System.InvalidOperationException(\"Result has no value.\");");
        writer.WriteLine("public TError Error => !IsOk ? _error : throw new global::System.InvalidOperationException(\"Result has no error.\");"); writer.WriteLine();
        writer.WriteLine("private CopeResult(bool isOk, TValue value, TError error)"); writer.WriteLine("{"); writer.Indent();
        writer.WriteLine("IsOk = isOk;"); writer.WriteLine("_value = value;"); writer.WriteLine("_error = error;"); writer.Unindent(); writer.WriteLine("}"); writer.WriteLine();
        writer.WriteLine("public static CopeResult<TValue, TError> Ok(TValue value) => new(true, value, default!);");
        writer.WriteLine("public static CopeResult<TValue, TError> Err(TError error) => new(false, default!, error);");
        writer.Unindent(); writer.WriteLine("}"); writer.WriteLine();
    }

    private static void EmitUnwrapPanic(CSharpTextWriter writer)
    {
        writer.WriteLine("private sealed class CopeUnwrapPanicException : global::System.Exception");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("public object? Error { get; }");
        writer.WriteLine();
        writer.WriteLine("public CopeUnwrapPanicException(object? error)");
        writer.WriteLine("    : base(\"COPE-PANIC-UNWRAP: Result unwrap encountered err\")");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("Error = error;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitEnum(CSharpTextWriter writer, MirEnum mirEnum)
    {
        var enumName = CSharpNameMangler.Mangle(mirEnum.Name); writer.WriteLine($"public abstract record {enumName}"); writer.WriteLine("{"); writer.Indent(); writer.WriteLine($"protected {enumName}()"); writer.WriteLine("{"); writer.WriteLine("}"); writer.WriteLine();
        foreach (var @case in mirEnum.Cases)
        {
            var caseName = CSharpNameMangler.Mangle(@case.Name);
            writer.WriteLine(@case.PayloadFields.Count == 0
                ? $"public sealed record {caseName} : {enumName};"
                : $"public sealed record {caseName}({string.Join(", ", @case.PayloadFields.Select(field => $"{MapType(field.Type)} {CSharpNameMangler.Mangle(field.Name)}"))}) : {enumName};");
            writer.WriteLine();
        }
        writer.Unindent(); writer.WriteLine("}");
    }

    private static void EmitColumnSupport(CSharpTextWriter writer)
    {
        writer.WriteLine("public abstract class CopeColumn<T>");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("internal abstract CopeResult<T, TableBoundsError> Get(double index);");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitTable(
        CSharpTextWriter writer,
        MirTableDefinition table,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records,
        bool emitsTsonAccessors)
    {
        string tableType = TableTypeName(table.Id);
        string rowType = TableRowTypeName(table.RowTypeId);

        foreach (var column in table.Columns)
        {
            writer.WriteLine($"public sealed class {TableColumnTypeName(column.Id)} : CopeColumn<{MapType(column.ElementType)}>");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"private readonly {MapType(column.ElementType)}[] _values;");
            writer.WriteLine();
            writer.WriteLine($"internal {TableColumnTypeName(column.Id)}({MapType(column.ElementType)}[] values)");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine("_values = values;");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
            EmitBoundsCheckedElementAccess(writer, column.ElementType, "_values", table.RowCount);
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
        }

        writer.WriteLine($"public sealed class {rowType}");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"private readonly {tableType} _table;");
        writer.WriteLine("private readonly int _index;");
        writer.WriteLine();
        writer.WriteLine($"internal {rowType}({tableType} table, int index)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("_table = table;");
        writer.WriteLine("_index = index;");
        writer.Unindent();
        writer.WriteLine("}");
        foreach (var column in table.Columns)
        {
            writer.WriteLine();
            writer.WriteLine($"internal {MapType(column.ElementType)} {TableRowFieldName(column.Id)} => _table.{TableReadMethodName(column.Id)}(_index);");
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine($"public sealed class {tableType}");
        writer.WriteLine("{");
        writer.Indent();
        foreach (var column in table.Columns)
        {
            writer.WriteLine($"private readonly {MapType(column.ElementType)}[] {TableStorageName(column.Id)};");
        }
        writer.WriteLine();
        writer.WriteLine($"private {tableType}({string.Join(", ", table.Columns.Select((column, index) => $"{MapType(column.ElementType)}[] values{index}"))})");
        writer.WriteLine("{");
        writer.Indent();
        for (int index = 0; index < table.Columns.Count; index++)
        {
            MirTableColumnDefinition column = table.Columns[index];
            writer.WriteLine($"{TableStorageName(column.Id)} = values{index};");
            writer.WriteLine($"{TableColumnPropertyName(column.Id)} = new {TableColumnTypeName(column.Id)}(values{index});");
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"internal static {tableType} Create()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"return new {tableType}(");
        writer.Indent();
        for (int index = 0; index < table.Columns.Count; index++)
        {
            MirTableColumnDefinition column = table.Columns[index];
            string values = string.Join(", ", column.Constants.Select(constant => EmitTableConstant(constant, records)));
            string comma = index == table.Columns.Count - 1 ? string.Empty : ",";
            writer.WriteLine($"new {MapType(column.ElementType)}[] {{ {values} }}{comma}");
        }
        writer.Unindent();
        writer.WriteLine(");");
        writer.Unindent();
        writer.WriteLine("}");
        foreach (var column in table.Columns)
        {
            writer.WriteLine();
            writer.WriteLine($"internal {TableColumnTypeName(column.Id)} {TableColumnPropertyName(column.Id)} {{ get; }}");
            writer.WriteLine();
            writer.WriteLine($"internal {MapType(column.ElementType)} {TableReadMethodName(column.Id)}(int index) => {TableStorageName(column.Id)}[index];");
            if (emitsTsonAccessors)
            {
                writer.WriteLine($"internal {MapType(column.ElementType)}[] {TableTsonStorageAccessName(column.Id)}() => {TableStorageName(column.Id)};");
            }
        }
        writer.WriteLine();
        writer.WriteLine($"internal CopeResult<{rowType}, TableBoundsError> GetRow(double index)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (double.IsNaN(index) || double.IsInfinity(index) || index != global::System.Math.Truncate(index))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("return CopeResult<" + rowType + ", TableBoundsError>.Err(new TableBoundsError.InvalidIndex(index));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"if (index < 0 || index >= {table.RowCount.ToString(global::System.Globalization.CultureInfo.InvariantCulture)})");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("return CopeResult<" + rowType + ", TableBoundsError>.Err(new TableBoundsError.OutOfBounds(index, " + table.RowCount.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + ".0));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("return CopeResult<" + rowType + ", TableBoundsError>.Ok(new " + rowType + "(this, (int)index));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitBoundsCheckedElementAccess(CSharpTextWriter writer, MirType elementType, string storage, int rowCount)
    {
        string elementTypeName = MapType(elementType);
        writer.WriteLine($"internal override CopeResult<{elementTypeName}, TableBoundsError> Get(double index)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (double.IsNaN(index) || double.IsInfinity(index) || index != global::System.Math.Truncate(index))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"return CopeResult<{elementTypeName}, TableBoundsError>.Err(new TableBoundsError.InvalidIndex(index));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"if (index < 0 || index >= {rowCount.ToString(global::System.Globalization.CultureInfo.InvariantCulture)})");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"return CopeResult<{elementTypeName}, TableBoundsError>.Err(new TableBoundsError.OutOfBounds(index, {rowCount.ToString(global::System.Globalization.CultureInfo.InvariantCulture)}.0));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"return CopeResult<{elementTypeName}, TableBoundsError>.Ok({storage}[(int)index]);");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static string EmitTableConstant(MirTableConstant constant, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records)
    {
        return constant switch
        {
            MirTableLiteralConstant literal => CSharpLiteralWriter.Write(literal.Value),
            MirTableArrayConstant array => $"new {MapType(array.ArrayType.ElementType)}[] {{ {string.Join(", ", array.Elements.Select(element => EmitTableConstant(element, records)))} }}",
            MirTableRecordConstant record => EmitTableRecordConstant(record, records),
            MirTableEnumConstant value => $"new {CSharpNameMangler.Mangle(value.EnumName)}.{CSharpNameMangler.Mangle(value.CaseName)}({string.Join(", ", value.Payloads.Select(payload => EmitTableConstant(payload, records)))})",
            MirTableResultConstant result => EmitTableResultConstant(result, records),
            _ => throw new InvalidOperationException($"Unsupported validated table constant {constant.GetType().Name}."),
        };
    }

    private static string EmitTableRecordConstant(MirTableRecordConstant constant, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records)
    {
        MirRecordDefinition definition = records[constant.RecordTypeId];
        var values = constant.Fields.ToDictionary(field => field.FieldId, field => field.Value);
        return $"new {RecordTypeName(constant.RecordTypeId)}({string.Join(", ", definition.Fields.Select(field => EmitTableConstant(values[field.Id], records)))})";
    }

    private static string EmitTableResultConstant(MirTableResultConstant constant, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records)
    {
        string successType = MapResultComponentType(constant.Type.SuccessType);
        string errorType = MapType(constant.Type.ErrorType);
        string factory = constant.IsOk ? "Ok" : "Err";
        return $"CopeResult<{successType}, {errorType}>.{factory}({EmitTableConstant(constant.Payload, records)})";
    }

    private static void EmitFunction(
        CSharpTextWriter writer,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records,
        List<CSharpDiagnostic> diagnostics)
    {
        if (function.IsAsync)
        {
            EmitAsyncFunction(writer, function, diagnostics);
            return;
        }
        if (function.IsGenerator)
        {
            EmitGeneratorFunction(writer, function, enumNames, records, diagnostics);
            return;
        }
        var returnType = MapType(function.ReturnType); var parameters = string.Join(", ", function.Parameters.Select(parameter => $"{MapType(parameter.Type)} {CSharpNameMangler.Mangle(parameter.Name)}"));
        writer.WriteLine($"public static {returnType} {CSharpNameMangler.Mangle(function.Name)}({parameters})"); writer.WriteLine("{"); writer.Indent();
        var tempIndex = 0;
        var previousState = CurrentEmissionState.Value;
        CurrentEmissionState.Value = new CSharpEmissionState(records);
        try
        {
            foreach (var statement in function.Body) EmitStatement(writer, statement, function, enumNames, ref tempIndex, diagnostics);
        }
        finally
        {
            CurrentEmissionState.Value = previousState;
        }
        writer.Unindent(); writer.WriteLine("}"); writer.WriteLine();
    }

    private static void EmitGeneratorFunction(
        CSharpTextWriter writer,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records,
        List<CSharpDiagnostic> diagnostics)
    {
        var iterable = (MirIterableType)function.ReturnType;
        string elementType = MapValueStorageType(iterable.ElementType);
        string returnType = MapType(function.ReturnType);
        string parameters = string.Join(", ", function.Parameters.Select(parameter => $"{MapType(parameter.Type)} {CSharpNameMangler.Mangle(parameter.Name)}"));
        string arguments = string.Join(", ", function.Parameters.Select(parameter => CSharpNameMangler.Mangle(parameter.Name)));
        string publicName = CSharpNameMangler.Mangle(function.Name);
        string rawName = "__cope_generator_" + publicName;

        writer.WriteLine($"public static {returnType} {publicName}({parameters}) => new CopeGeneratorEnumerable<{elementType}>(() => {rawName}({arguments}).GetEnumerator());");
        writer.WriteLine($"private static {returnType} {rawName}({parameters})");
        writer.WriteLine("{");
        writer.Indent();
        var tempIndex = 0;
        var previousState = CurrentEmissionState.Value;
        CurrentEmissionState.Value = new CSharpEmissionState(records);
        try
        {
            foreach (MirStatement statement in function.Body)
            {
                EmitStatement(writer, statement, function, enumNames, ref tempIndex, diagnostics);
            }
            if (!ContainsYield(function.Body))
            {
                writer.WriteLine("yield break;");
            }
        }
        finally
        {
            CurrentEmissionState.Value = previousState;
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static bool ContainsYield(IEnumerable<MirStatement> statements)
        => statements.Any(statement => statement switch
        {
            MirYieldStatement => true,
            MirIfStatement conditional => ContainsYield(conditional.ThenStatements)
                || conditional.ElseStatements is not null && ContainsYield(conditional.ElseStatements),
            MirWhileStatement loop => ContainsYield(loop.BodyStatements),
            MirForStatement loop => loop.Initializer is not null && ContainsYield([loop.Initializer]) || ContainsYield(loop.BodyStatements),
            MirForOfStatement loop => ContainsYield(loop.BodyStatements),
            _ => false,
        });

    private static void EmitAsyncFunction(CSharpTextWriter writer, MirFunction function, List<CSharpDiagnostic> diagnostics)
    {
        if (!TryGetAsyncStates(function.SuspensionAutomaton?.ExecutionPlan, out List<AsyncState> states, out int entryState))
        {
            diagnostics.Add(new CSharpDiagnostic("COPE-CS-ASYNC-0001", $"Async function '{function.Name}' requires structured suspension lowering before it can emit control flow."));
            return;
        }

        string resultType = MapValueStorageType(function.ReturnType);
        string parameters = string.Join(", ", function.Parameters.Select(parameter => $"{MapType(parameter.Type)} {CSharpNameMangler.Mangle(parameter.Name)}"));
        string frameType = "__CopeAsyncFrame_" + CSharpNameMangler.Mangle(function.Name);
        writer.WriteLine($"public static CopeAsync<{resultType}> {CSharpNameMangler.Mangle(function.Name)}({parameters})");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"var frame = new {frameType}();");
        foreach (MirParameter parameter in function.Parameters)
        {
            string name = CSharpNameMangler.Mangle(parameter.Name);
            writer.WriteLine($"frame.{name} = {name};");
        }
        writer.WriteLine($"var computation = new CopeAsync<{resultType}>();");
        writer.WriteLine($"frame.State = {entryState};");
        writer.WriteLine("void Step()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (computation.IsCompleted) return;");
        writer.WriteLine("while (true)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("switch (frame.State)");
        writer.WriteLine("{");
        writer.Indent();
        foreach (AsyncState state in states.OrderBy(state => state.Id))
        {
            writer.WriteLine($"case {state.Id}:");
            writer.Indent();
            if (state.Kind == AsyncStateKind.Branch)
            {
                writer.WriteLine($"frame.State = {EmitAsyncExpression(state.Condition!, function)} ? {state.ThenState} : {state.ElseState};");
                writer.WriteLine("continue;");
                writer.Unindent();
                continue;
            }
            if (state.Kind == AsyncStateKind.Jump)
            {
                writer.WriteLine($"frame.State = {state.NextState};");
                writer.WriteLine("continue;");
                writer.Unindent();
                continue;
            }
            if (state.Kind == AsyncStateKind.Await)
            {
                string pending = "frame.__" + state.AwaitedComputationSlot!.Value.Value;
                string resumed = "frame.__" + state.ValueSlot!.Value.Value;
                writer.WriteLine($"{pending} = {EmitAsyncExpression(state.Expression!, function)};");
                writer.WriteLine($"frame.State = {state.NextState};");
                writer.WriteLine($"if (!{pending}.SubscribeTransport(() => {{ {resumed} = {pending}.Value; Step(); }}, computation.Cancel, computation.Fail, computation.Panic)) return;");
                writer.WriteLine($"if ({pending}.IsCancelled) {{ computation.Cancel(); return; }}");
                writer.WriteLine($"if ({pending}.IsTransportFailed) {{ computation.Fail(); return; }}");
                writer.WriteLine($"if ({pending}.IsPanicked) {{ computation.Panic(); return; }}");
                writer.WriteLine($"{resumed} = {pending}.Value;");
                writer.WriteLine("continue;");
                writer.Unindent();
                continue;
            }
            if (state.Kind == AsyncStateKind.Evaluate)
            {
                writer.WriteLine($"frame.__{state.ValueSlot!.Value.Value} = {EmitAsyncExpression(state.Expression!, function)};");
                writer.WriteLine($"frame.State = {state.NextState};");
                writer.WriteLine("continue;");
                writer.Unindent();
                continue;
            }
            if (state.Kind == AsyncStateKind.Propagate)
            {
                string result = "__cope_propagate_" + state.Id;
                string success = "frame.__" + state.ValueSlot!.Value.Value;
                writer.WriteLine($"var {result} = {EmitAsyncExpression(state.Expression!, function)};");
                if (state.PropagationTarget is MirPropagationTarget.LexicalExcept)
                {
                    if (state.HandlerState < 0 || state.HandlerErrorSlot is null)
                    {
                        diagnostics.Add(new CSharpDiagnostic("COPE-CS-ASYNC-0003", $"Async function '{function.Name}' has a lexical Result propagation target without a validated handler state."));
                        writer.WriteLine("computation.Panic();");
                        writer.WriteLine("return;");
                    }
                    else
                    {
                        writer.WriteLine($"if (!{result}.IsOk) {{ frame.__{state.HandlerErrorSlot.Value.Value} = {result}.Error; frame.State = {state.HandlerState}; continue; }}");
                        writer.WriteLine($"{success} = {result}.Value;");
                        writer.WriteLine($"frame.State = {state.NextState};");
                        writer.WriteLine("continue;");
                    }
                    writer.Unindent();
                    continue;
                }

                if (state.PropagationTarget is not MirPropagationTarget.FunctionReturn
                    || function.ReturnType is not MirResultType propagatedFunctionResult)
                {
                    diagnostics.Add(new CSharpDiagnostic("COPE-CS-ASYNC-0003", $"Async function '{function.Name}' has an unsupported Result propagation target."));
                    writer.WriteLine("computation.Panic();");
                    writer.WriteLine("return;");
                    writer.Unindent();
                    continue;
                }

                string errorResult = $"CopeResult<{MapResultComponentType(propagatedFunctionResult.SuccessType)}, {MapType(propagatedFunctionResult.ErrorType)}>.Err({result}.Error)";
                writer.WriteLine($"if (!{result}.IsOk) {{ computation.Resolve({errorResult}); return; }}");
                writer.WriteLine($"{success} = {result}.Value;");
                writer.WriteLine($"frame.State = {state.NextState};");
                writer.WriteLine("continue;");
                writer.Unindent();
                continue;
            }
            MirStatement statement = state.Statement!;
            if (statement is MirVariableDeclarationStatement plainDeclaration)
            {
                writer.WriteLine($"frame.{CSharpNameMangler.Mangle(plainDeclaration.Local.Name)} = {EmitAsyncExpression(plainDeclaration.Initializer, function)};");
                writer.WriteLine($"frame.State = {state.NextState};");
                writer.WriteLine("continue;");
            }
            else if (statement is MirReturnStatement { Expression: not null } returned)
            {
                writer.WriteLine($"computation.Resolve({EmitAsyncExpression(returned.Expression, function)});");
                writer.WriteLine("return;");
            }
            else if (statement is MirReturnStatement)
            {
                writer.WriteLine("computation.Resolve(default!);");
                writer.WriteLine("return;");
            }
            else if (statement is MirExpressionStatement expression)
            {
                writer.WriteLine($"{EmitAsyncExpression(expression.Expression, function)};");
                writer.WriteLine($"frame.State = {state.NextState};");
                writer.WriteLine("continue;");
            }
            else
            {
                diagnostics.Add(new CSharpDiagnostic("COPE-CS-ASYNC-0002", $"Async function '{function.Name}' contains an unsupported straight-line statement '{statement.GetType().Name}'."));
                writer.WriteLine("return;");
            }
            writer.Unindent();
        }
        writer.WriteLine("default:");
        writer.Indent();
        writer.WriteLine("return;");
        writer.Unindent();
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("Step();");
        writer.WriteLine("return computation;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static bool TryGetAsyncStates(MirAsyncExecutionPlan? plan, out List<AsyncState> states, out int entryState)
    {
        states = [];
        entryState = -1;
        if (plan is null)
        {
            return false;
        }

        var numbers = plan.States.Select((state, index) => (state.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        if (!numbers.TryGetValue(plan.EntryStateId, out entryState))
        {
            return false;
        }

        foreach (MirAsyncExecutionState state in plan.States)
        {
            int id = numbers[state.Id];
            switch (state)
            {
                case MirAsyncStatementExecutionState statement when numbers.TryGetValue(statement.NextStateId, out int next):
                    states.Add(new AsyncState(id, AsyncStateKind.Statement, statement.Statement, next));
                    break;
                case MirAsyncReturnExecutionState returned:
                    states.Add(new AsyncState(id, AsyncStateKind.Return, returned.Statement, -1));
                    break;
                case MirAsyncBranchExecutionState branch
                    when numbers.TryGetValue(branch.ThenStateId, out int thenState)
                    && numbers.TryGetValue(branch.ElseStateId, out int elseState):
                    states.Add(new AsyncState(id, AsyncStateKind.Branch, null, -1, thenState, elseState, condition: branch.Condition));
                    break;
                case MirAsyncJumpExecutionState jump when numbers.TryGetValue(jump.TargetStateId, out int target):
                    states.Add(new AsyncState(id, AsyncStateKind.Jump, null, target));
                    break;
                case MirAsyncAwaitExecutionState awaitState when numbers.TryGetValue(awaitState.NextStateId, out int awaitNext):
                    states.Add(new AsyncState(id, AsyncStateKind.Await, null, awaitNext, awaitedComputationSlot: awaitState.AwaitedComputationSlot, valueSlot: awaitState.ResumedValueSlot, expression: awaitState.AwaitedComputation));
                    break;
                case MirAsyncEvaluateExpressionState evaluation when numbers.TryGetValue(evaluation.NextStateId, out int evaluationNext):
                    states.Add(new AsyncState(id, AsyncStateKind.Evaluate, null, evaluationNext, valueSlot: evaluation.TargetSlot, expression: evaluation.Expression));
                    break;
                case MirAsyncPropagateExecutionState propagation
                    when numbers.TryGetValue(propagation.NextStateId, out int propagationNext)
                    && (propagation.HandlerStateId is null || numbers.TryGetValue(propagation.HandlerStateId.Value, out _)):
                    int handlerState = propagation.HandlerStateId is { } handlerStateId
                        ? numbers[handlerStateId]
                        : -1;
                    states.Add(new AsyncState(id, AsyncStateKind.Propagate, null, propagationNext, valueSlot: propagation.SuccessValueSlot, expression: propagation.ResultExpression, propagationTarget: propagation.Target, handlerState: handlerState, handlerErrorSlot: propagation.HandlerErrorSlot));
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static string EmitAsyncExpression(MirExpression expression, MirFunction function)
    {
        return expression switch
        {
            MirLiteralExpression literal => CSharpLiteralWriter.Write(literal.Value),
            MirUnitExpression => "CopeUnit.Value",
            MirVariableExpression variable => "frame." + CSharpNameMangler.Mangle(variable.Name),
            MirAsyncFrameSlotExpression slot => "frame.__" + slot.SlotId.Value,
            MirAssignmentExpression assignment => $"frame.{CSharpNameMangler.Mangle(assignment.Name)} = {EmitAsyncExpression(assignment.Expression, function)}",
            MirBinaryExpression binary => $"({EmitAsyncExpression(binary.Left, function)} {binary.Operator} {EmitAsyncExpression(binary.Right, function)})",
            MirUnaryExpression unary => $"({unary.Operator}{EmitAsyncExpression(unary.Operand, function)})",
            MirCallExpression call => $"{CSharpNameMangler.Mangle(call.FunctionName)}({string.Join(", ", call.Arguments.Select(argument => EmitAsyncExpression(argument, function)))})",
            MirTsonTransportExpression transport => EmitAsyncTsonTransport(transport, function),
            MirNpmCallExpression npm => EmitAsyncNpmCall(npm, function),
            MirClrInvocationExpression invocation => EmitClrInvocation(invocation, function),
            MirClrPropertyAccessExpression property => EmitClrProperty(property, function),
            MirOkExpression ok => $"CopeResult<{MapResultComponentType(((MirResultType)ok.Type).SuccessType)}, {MapType(((MirResultType)ok.Type).ErrorType)}>.Ok({EmitAsyncExpression(ok.Payload, function)})",
            MirErrExpression err => $"CopeResult<{MapResultComponentType(((MirResultType)err.Type).SuccessType)}, {MapType(((MirResultType)err.Type).ErrorType)}>.Err({EmitAsyncExpression(err.Payload, function)})",
            _ => throw new InvalidOperationException($"Async function '{function.Name}' uses expression '{expression.GetType().Name}' which has not been lowered into an explicit state expression."),
        };
    }

    private static string EmitAsyncTsonTransport(MirTsonTransportExpression transport, MirFunction function)
    {
        var result = (MirResultType)transport.AsyncType.EventualType;
        string resultType = $"CopeResult<{MapResultComponentType(result.SuccessType)}, {MapType(result.ErrorType)}>";
        string operation = EmitAsyncExpression(transport.Operation, function);
        string request = EmitAsyncExpression(transport.Request, function);
        string encodedRequest = $"{TsonEncodeMethodName(transport.RequestPlanId)}({request})";
        string decodedResponse = $"{TsonDecodeMethodName(transport.ResponsePlanId)}(payload)";
        string decodedError = $"{TsonDecodeMethodName(transport.RemoteErrorPlanId)}(payload)";
        return $"CopeTsonTransport.Start<{resultType}>({operation}, {encodedRequest}.Value, (kind, payload) => kind == \"ok\" ? {resultType}.Ok({decodedResponse}) : {resultType}.Err({decodedError}))";
    }

    private static string EmitAsyncNpmCall(MirNpmCallExpression npm, MirFunction function)
    {
        var result = (MirResultType)npm.AsyncType.EventualType;
        string resultType = $"CopeResult<{MapResultComponentType(result.SuccessType)}, {MapType(result.ErrorType)}>";
        string request = npm.ArgumentTuple is MirRecordConstructionExpression tuple
            ? $"new {RecordTypeName(tuple.RecordTypeId)}({string.Join(", ", tuple.Initializers.Select(initializer => EmitAsyncExpression(initializer.Value, function)))})"
            : EmitAsyncExpression(npm.ArgumentTuple, function);
        string operation = CSharpLiteralWriter.Write("npm:" + npm.PackageName + "@" + npm.PackageVersion + ":" + npm.ExportName);
        string response = $"{TsonDecodeMethodName(npm.ResponsePlanId)}(payload).{RecordFieldName(npm.ResponseValueFieldId)}";
        string error = $"{TsonDecodeMethodName(npm.RemoteErrorPlanId)}(payload).{RecordFieldName(npm.RemoteErrorValueFieldId)}";
        return $"CopeTsonTransport.Start<{resultType}>({operation}, {TsonEncodeMethodName(npm.RequestPlanId)}({request}).Value, (kind, payload) => kind == \"ok\" ? {resultType}.Ok({response}) : {resultType}.Err({error}))";
    }

    private static string EmitClrInvocation(MirClrInvocationExpression invocation, MirFunction function)
    {
        string arguments = string.Join(", ", invocation.Arguments.Select(argument => EmitAsyncExpression(argument, function)));
        return EmitClrInvocationCore(invocation.Member, invocation.Receiver is null ? null : EmitAsyncExpression(invocation.Receiver, function), arguments);
    }

    private static string EmitClrProperty(MirClrPropertyAccessExpression property, MirFunction function)
        => EmitClrPropertyCore(property.Property, property.Receiver is null ? null : EmitAsyncExpression(property.Receiver, function));

    private static string EmitClrInvocation(CSharpTextWriter writer, MirClrInvocationExpression invocation, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        string arguments = string.Join(", ", EmitArguments(invocation.Arguments, writer, function, enumNames, ref tempIndex, diagnostics));
        string? receiver = invocation.Receiver is null ? null : EmitExpression(writer, invocation.Receiver, function, enumNames, ref tempIndex, diagnostics);
        return EmitClrInvocationCore(invocation.Member, receiver, arguments);
    }

    private static string EmitClrProperty(CSharpTextWriter writer, MirClrPropertyAccessExpression property, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        string? receiver = property.Receiver is null ? null : EmitExpression(writer, property.Receiver, function, enumNames, ref tempIndex, diagnostics);
        return EmitClrPropertyCore(property.Property, receiver);
    }

    private static string EmitClrInvocationCore(MirClrMemberIdentity member, string? receiver, string arguments)
    {
        string declaringType = "global::" + member.DeclaringType;
        if (member.IsConstructor) return $"new {declaringType}({arguments})";
        string target = member.IsStatic ? declaringType : receiver ?? throw new InvalidOperationException("CLR instance invocation has no receiver.");
        string genericSuffix = member.GenericArguments.Count == 0 ? string.Empty : "<" + string.Join(", ", member.GenericArguments.Select(MapType)) + ">";
        return $"{target}.{member.MemberName}{genericSuffix}({arguments})";
    }

    private static string EmitClrPropertyCore(MirClrMemberIdentity property, string? receiver)
    {
        string target = property.IsStatic ? "global::" + property.DeclaringType : receiver ?? throw new InvalidOperationException("CLR instance property has no receiver.");
        return $"{target}.{property.MemberName}";
    }

    private static void EmitStatement(CSharpTextWriter writer, MirStatement statement, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                writer.WriteLine($"{MapValueStorageType(declaration.Local.Type)} {CSharpNameMangler.Mangle(declaration.Local.Name)} = {EmitExpression(writer, declaration.Initializer, function, enumNames, ref tempIndex, diagnostics)};"); break;
            case MirResourceUsingDeclarationStatement declaration:
                writer.WriteLine($"using var {CSharpNameMangler.Mangle(declaration.Local.Name)} = {EmitExpression(writer, declaration.Initializer, function, enumNames, ref tempIndex, diagnostics)};");
                break;
            case MirCSharpBlockStatement block:
                EmitCSharpBlock(writer, block, CurrentSourcePath.Value);
                break;
            case MirExpressionStatement expression:
                writer.WriteLine($"{EmitExpression(writer, expression.Expression, function, enumNames, ref tempIndex, diagnostics)};"); break;
            case MirReturnStatement { Expression: null } when function.IsGenerator:
                writer.WriteLine("yield break;"); break;
            case MirReturnStatement { Expression: null }:
                writer.WriteLine("return;"); break;
            case MirReturnStatement returnStatement:
                if (function.ReturnType.Identifier == "void")
                {
                    writer.WriteLine($"{EmitExpression(writer, returnStatement.Expression!, function, enumNames, ref tempIndex, diagnostics)};");
                    writer.WriteLine("return;");
                }
                else
                {
                    writer.WriteLine($"return {EmitExpression(writer, returnStatement.Expression!, function, enumNames, ref tempIndex, diagnostics)};");
                }
                break;
            case MirIfStatement conditional:
                writer.WriteLine($"if ({EmitExpression(writer, conditional.Condition, function, enumNames, ref tempIndex, diagnostics)})"); writer.WriteLine("{"); writer.Indent(); foreach (var nested in conditional.ThenStatements) EmitStatement(writer, nested, function, enumNames, ref tempIndex, diagnostics); writer.Unindent(); writer.WriteLine("}");
                if (conditional.ElseStatements is not null) { writer.WriteLine("else"); writer.WriteLine("{"); writer.Indent(); foreach (var nested in conditional.ElseStatements) EmitStatement(writer, nested, function, enumNames, ref tempIndex, diagnostics); writer.Unindent(); writer.WriteLine("}"); } break;
            case MirWhileStatement loop:
                EmitWhileStatement(writer, loop, function, enumNames, ref tempIndex, diagnostics);
                break;
            case MirForStatement loop:
                EmitForStatement(writer, loop, function, enumNames, ref tempIndex, diagnostics);
                break;
            case MirForOfStatement loop:
                writer.WriteLine($"foreach ({MapValueStorageType(loop.Local.Type)} {CSharpNameMangler.Mangle(loop.Local.Name)} in {EmitExpression(writer, loop.Iterable, function, enumNames, ref tempIndex, diagnostics)})");
                EmitStatementBlock(writer, loop.BodyStatements, function, enumNames, ref tempIndex, diagnostics);
                break;
            case MirBreakStatement:
                writer.WriteLine("break;");
                break;
            case MirContinueStatement:
                MirExpression? increment = CurrentEmissionState.Value?.ContinueIncrements.TryPeek(out MirExpression? currentIncrement) == true
                    ? currentIncrement
                    : null;
                if (increment is not null)
                {
                    writer.WriteLine($"{EmitExpression(writer, increment, function, enumNames, ref tempIndex, diagnostics)};");
                }
                writer.WriteLine("continue;");
                break;
            case MirYieldStatement { Expression: null }:
                writer.WriteLine("yield break;");
                break;
            case MirYieldStatement yield:
                string yielded = EmitExpression(writer, yield.Expression!, function, enumNames, ref tempIndex, diagnostics);
                writer.WriteLine(yield.IsDelegating ? $"foreach (var __cope_delegated in {yielded}) yield return __cope_delegated;" : $"yield return {yielded};");
                break;
            default: diagnostics.Add(new CSharpDiagnostic("COPE-CS-0001", $"Unsupported MIR statement: {statement.GetType().Name}")); break;
        }
    }

    private static void EmitCSharpBlock(CSharpTextWriter writer, MirCSharpBlockStatement block, string? sourcePath)
    {
        writer.WriteLine("// Copeland inline C# block");
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            writer.WriteLine($"#line {block.SourceLine} \"{sourcePath.Replace("\\", "\\\\", StringComparison.Ordinal)}\"");
        }
        using var reader = new StringReader(block.BodyText.Replace("\r\n", "\n", StringComparison.Ordinal));
        while (reader.ReadLine() is { } line)
        {
            writer.WriteLine(line);
        }
        if (!string.IsNullOrWhiteSpace(sourcePath)) writer.WriteLine("#line default");
    }

    private static void EmitWhileStatement(
        CSharpTextWriter writer,
        MirWhileStatement loop,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        if (!ExpressionRequiresStatements(loop.Condition))
        {
            writer.WriteLine($"while ({EmitExpression(writer, loop.Condition, function, enumNames, ref tempIndex, diagnostics)})");
            EmitStatementBlock(writer, loop.BodyStatements, function, enumNames, ref tempIndex, diagnostics);
            return;
        }

        writer.WriteLine("while (true)");
        writer.WriteLine("{");
        writer.Indent();
        string condition = EmitExpression(writer, loop.Condition, function, enumNames, ref tempIndex, diagnostics);
        writer.WriteLine($"if (!({condition}))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("break;");
        writer.Unindent();
        writer.WriteLine("}");
        EmitLoopBody(writer, loop.BodyStatements, null, function, enumNames, ref tempIndex, diagnostics);
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitForStatement(
        CSharpTextWriter writer,
        MirForStatement loop,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        writer.WriteLine("{");
        writer.Indent();
        if (loop.Initializer is not null)
        {
            EmitStatement(writer, loop.Initializer, function, enumNames, ref tempIndex, diagnostics);
        }

        bool requiresStaging = (loop.Condition is not null && ExpressionRequiresStatements(loop.Condition))
            || (loop.Increment is not null && ExpressionRequiresStatements(loop.Increment));
        if (!requiresStaging)
        {
            string condition = loop.Condition is null
                ? string.Empty
                : EmitExpression(writer, loop.Condition, function, enumNames, ref tempIndex, diagnostics);
            string increment = loop.Increment is null
                ? string.Empty
                : EmitExpression(writer, loop.Increment, function, enumNames, ref tempIndex, diagnostics);
            writer.WriteLine($"for (; {condition}; {increment})");
            EmitLoopBodyBlock(writer, loop.BodyStatements, null, function, enumNames, ref tempIndex, diagnostics);
        }
        else
        {
            writer.WriteLine("while (true)");
            writer.WriteLine("{");
            writer.Indent();
            if (loop.Condition is not null)
            {
                string condition = EmitExpression(writer, loop.Condition, function, enumNames, ref tempIndex, diagnostics);
                writer.WriteLine($"if (!({condition}))");
                writer.WriteLine("{");
                writer.Indent();
                writer.WriteLine("break;");
                writer.Unindent();
                writer.WriteLine("}");
            }
            EmitLoopBody(writer, loop.BodyStatements, loop.Increment, function, enumNames, ref tempIndex, diagnostics);
            if (loop.Increment is not null)
            {
                writer.WriteLine($"{EmitExpression(writer, loop.Increment, function, enumNames, ref tempIndex, diagnostics)};");
            }
            writer.Unindent();
            writer.WriteLine("}");
        }
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitStatementBlock(
        CSharpTextWriter writer,
        IReadOnlyList<MirStatement> statements,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        writer.WriteLine("{");
        writer.Indent();
        foreach (MirStatement nested in statements)
        {
            EmitStatement(writer, nested, function, enumNames, ref tempIndex, diagnostics);
        }
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitLoopBodyBlock(
        CSharpTextWriter writer,
        IReadOnlyList<MirStatement> statements,
        MirExpression? continueIncrement,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        writer.WriteLine("{");
        writer.Indent();
        EmitLoopBody(writer, statements, continueIncrement, function, enumNames, ref tempIndex, diagnostics);
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitLoopBody(
        CSharpTextWriter writer,
        IReadOnlyList<MirStatement> statements,
        MirExpression? continueIncrement,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        CSharpEmissionState state = CurrentEmissionState.Value
            ?? throw new InvalidOperationException("C# loop emission requires function emission state.");
        state.ContinueIncrements.Push(continueIncrement);
        try
        {
            foreach (MirStatement nested in statements)
            {
                EmitStatement(writer, nested, function, enumNames, ref tempIndex, diagnostics);
            }
        }
        finally
        {
            state.ContinueIncrements.Pop();
        }
    }

    private static string EmitExpression(CSharpTextWriter writer, MirExpression expression, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
        => expression switch
        {
            MirLiteralExpression literal => CSharpLiteralWriter.Write(literal.Value),
            MirUnitExpression => "CopeUnit.Value",
            MirVariableExpression variable => CSharpNameMangler.Mangle(variable.Name),
            MirAssignmentExpression assignment => $"{CSharpNameMangler.Mangle(assignment.Name)} = {EmitExpression(writer, assignment.Expression, function, enumNames, ref tempIndex, diagnostics)}",
            MirUnaryExpression unary => unary.Operator + ParenthesizeAssignmentOperand(unary.Operand, EmitExpression(writer, unary.Operand, function, enumNames, ref tempIndex, diagnostics)),
            MirBinaryExpression binary => EmitBinary(writer, binary, function, enumNames, ref tempIndex, diagnostics),
            MirCallExpression call => $"{CSharpNameMangler.Mangle(call.FunctionName)}({string.Join(", ", EmitArguments(call.Arguments, writer, function, enumNames, ref tempIndex, diagnostics))})",
            MirClrInvocationExpression invocation => EmitClrInvocation(writer, invocation, function, enumNames, ref tempIndex, diagnostics),
            MirClrPropertyAccessExpression property => EmitClrProperty(writer, property, function, enumNames, ref tempIndex, diagnostics),
            MirFunctionReferenceExpression reference => $"({MapType(reference.CallableType)}){CSharpNameMangler.Mangle(reference.FunctionName)}",
            MirCallableConstructionExpression construction => EmitCallableConstruction(writer, construction, function, enumNames, ref tempIndex, diagnostics),
            MirInvokeExpression invoke => $"{ParenthesizeAssignmentOperand(invoke.Callee, EmitExpression(writer, invoke.Callee, function, enumNames, ref tempIndex, diagnostics))}({string.Join(", ", EmitArguments(invoke.Arguments, writer, function, enumNames, ref tempIndex, diagnostics))})",
            MirArrayExpression array => $"new {MapType(array.Type)} {{ {string.Join(", ", EmitArguments(array.Elements, writer, function, enumNames, ref tempIndex, diagnostics))} }}",
            MirBatchExpression batch => EmitBatchExpression(writer, batch, function, enumNames, ref tempIndex, diagnostics),
            MirRecordConstructionExpression construction => EmitRecordConstruction(writer, construction, function, enumNames, ref tempIndex, diagnostics),
            MirRecordFieldAccessExpression access => EmitRecordFieldAccess(writer, access, function, enumNames, ref tempIndex, diagnostics),
            MirRecordWithExpression withExpression => EmitRecordWith(writer, withExpression, function, enumNames, ref tempIndex, diagnostics),
            MirTableReferenceExpression reference => TableSingletonName(reference.TableId),
            MirTableColumnAccessExpression access => $"{EmitExpression(writer, access.Receiver, function, enumNames, ref tempIndex, diagnostics)}.{TableColumnPropertyName(access.ColumnId)}",
            MirTableRowAccessExpression access => $"{EmitExpression(writer, access.Receiver, function, enumNames, ref tempIndex, diagnostics)}.GetRow({EmitExpression(writer, access.Index, function, enumNames, ref tempIndex, diagnostics)})",
            MirColumnElementAccessExpression access => $"{EmitExpression(writer, access.Receiver, function, enumNames, ref tempIndex, diagnostics)}.Get({EmitExpression(writer, access.Index, function, enumNames, ref tempIndex, diagnostics)})",
            MirTableRowFieldAccessExpression access => $"{EmitExpression(writer, access.Receiver, function, enumNames, ref tempIndex, diagnostics)}.{TableRowFieldName(access.FieldId)}",
            MirEnumValueExpression value => $"new {CSharpNameMangler.Mangle(value.EnumName)}.{CSharpNameMangler.Mangle(value.CaseName)}({string.Join(", ", EmitArguments(value.Arguments, writer, function, enumNames, ref tempIndex, diagnostics))})",
            MirMatchExpression match => EmitEnumMatch(writer, match, function, enumNames, ref tempIndex, diagnostics),
            MirIfExpression conditional => EmitIfExpression(writer, conditional, function, enumNames, ref tempIndex, diagnostics),
            MirTsonEncodeExpression encode => $"{TsonEncodeMethodName(encode.PlanId)}({EmitExpression(writer, encode.Operand, function, enumNames, ref tempIndex, diagnostics)})",
            MirOkExpression ok => $"CopeResult<{MapResultComponentType(((MirResultType)ok.Type).SuccessType)}, {MapType(((MirResultType)ok.Type).ErrorType)}>.Ok({EmitExpression(writer, ok.Payload, function, enumNames, ref tempIndex, diagnostics)})",
            MirErrExpression err => $"CopeResult<{MapResultComponentType(((MirResultType)err.Type).SuccessType)}, {MapType(((MirResultType)err.Type).ErrorType)}>.Err({EmitExpression(writer, err.Payload, function, enumNames, ref tempIndex, diagnostics)})",
            MirResultMatchExpression match => EmitResultMatch(writer, match, function, enumNames, ref tempIndex, diagnostics),
            MirPropagateExpression propagate => EmitPropagation(writer, propagate, function, enumNames, ref tempIndex, diagnostics),
            MirUnwrapExpression unwrap => EmitUnwrap(writer, unwrap, function, enumNames, ref tempIndex, diagnostics),
            MirTryExpression tryExpression => EmitTryExcept(writer, tryExpression, function, enumNames, ref tempIndex, diagnostics),
            _ => UnsupportedExpression(expression, diagnostics)
        };

    private static string EmitBatchExpression(
        CSharpTextWriter writer,
        MirBatchExpression batch,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        int batchId = tempIndex++;
        string input = "__cope_batch_input_" + batchId;
        string output = "__cope_batch_output_" + batchId;
        string failures = "__cope_batch_failures_" + batchId;
        string options = "__cope_batch_options_" + batchId;
        string index = "__cope_batch_index_" + batchId;
        string itemName = CSharpNameMangler.Mangle(batch.Item.Name);
        string inputType = MapType(batch.Input.Type);
        string outputType = MapType(batch.ArrayType.ElementType);

        writer.WriteLine($"{inputType} {input} = {EmitExpression(writer, batch.Input, function, enumNames, ref tempIndex, diagnostics)};");
        writer.WriteLine($"{outputType}[] {output} = new {outputType}[{input}.Length];");
        writer.WriteLine($"var {failures} = new global::System.Collections.Concurrent.ConcurrentDictionary<int, global::System.Exception>();");
        writer.WriteLine($"var {options} = new global::System.Threading.Tasks.ParallelOptions();");
        writer.WriteLine("if (__cope_batch_max_degree_for_testing > 0)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"{options}.MaxDegreeOfParallelism = __cope_batch_max_degree_for_testing;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"global::System.Threading.Tasks.Parallel.For(0, {input}.Length, {options}, {index} =>");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("__cope_batch_item_entered_for_testing?.Invoke();");
        writer.WriteLine($"{MapValueStorageType(batch.Item.Type)} {itemName} = {input}[{index}];");
        foreach (MirStatement statement in batch.Body.PrefixStatements)
        {
            EmitBatchBodyStatement(writer, statement, function, enumNames, ref tempIndex, diagnostics);
        }
        writer.WriteLine($"{output}[{index}] = {EmitExpression(writer, batch.Body.ValueExpression, function, enumNames, ref tempIndex, diagnostics)};");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("catch (global::System.Exception exception)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"{failures}.TryAdd({index}, exception);");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("});");
        writer.WriteLine($"if (!{failures}.IsEmpty)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("var __cope_batch_failure_index = int.MaxValue;");
        writer.WriteLine($"foreach (int __cope_batch_candidate in {failures}.Keys)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (__cope_batch_candidate < __cope_batch_failure_index) __cope_batch_failure_index = __cope_batch_candidate;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"throw new global::System.InvalidOperationException($\"COPE-BATCH-FAILURE index {{__cope_batch_failure_index}}\", {failures}[__cope_batch_failure_index]);");
        writer.Unindent();
        writer.WriteLine("}");
        return output;
    }

    private static void EmitBatchBodyStatement(
        CSharpTextWriter writer,
        MirStatement statement,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                writer.WriteLine($"{MapValueStorageType(declaration.Local.Type)} {CSharpNameMangler.Mangle(declaration.Local.Name)} = {EmitExpression(writer, declaration.Initializer, function, enumNames, ref tempIndex, diagnostics)};");
                break;
            case MirExpressionStatement expression:
                writer.WriteLine($"{EmitExpression(writer, expression.Expression, function, enumNames, ref tempIndex, diagnostics)};");
                break;
            default:
                diagnostics.Add(new CSharpDiagnostic("COPE-CS-BATCH-0001", $"Unsupported batch body statement: {statement.GetType().Name}"));
                break;
        }
    }

    private static string EmitCallableConstruction(
        CSharpTextWriter writer,
        MirCallableConstructionExpression construction,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        string environmentName = CapturedCallableEnvironmentName(construction.CodeFunctionName);
        string captures = string.Join(", ", EmitArguments(construction.Captures, writer, function, enumNames, ref tempIndex, diagnostics));
        return $"({MapType(construction.CallableType)})new {environmentName}({captures}).Invoke";
    }

    private static string CapturedCallableEnvironmentName(string codeFunctionName)
        => "__CopeCapturedCallableEnvironment_" + CSharpNameMangler.Mangle(codeFunctionName);

    private static IEnumerable<MirCallableConstructionExpression> EnumerateCallableConstructions(MirProgram program)
    {
        foreach (MirFunction function in program.Functions)
        {
            foreach (MirStatement statement in function.Body)
            {
                foreach (MirCallableConstructionExpression construction in EnumerateCallableConstructions(statement))
                {
                    yield return construction;
                }
            }
        }
    }

    private static IEnumerable<MirCallableConstructionExpression> EnumerateCallableConstructions(MirStatement statement)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                return EnumerateCallableConstructions(declaration.Initializer);
            case MirExpressionStatement expression:
                return EnumerateCallableConstructions(expression.Expression);
            case MirReturnStatement { Expression: not null } returned:
                return EnumerateCallableConstructions(returned.Expression);
            case MirIfStatement conditional:
                return EnumerateCallableConstructions(conditional.Condition)
                    .Concat(conditional.ThenStatements.SelectMany(EnumerateCallableConstructions))
                    .Concat(conditional.ElseStatements?.SelectMany(EnumerateCallableConstructions) ?? []);
            case MirWhileStatement loop:
                return EnumerateCallableConstructions(loop.Condition).Concat(loop.BodyStatements.SelectMany(EnumerateCallableConstructions));
            case MirForStatement loop:
                return (loop.Initializer is null ? [] : EnumerateCallableConstructions(loop.Initializer))
                    .Concat(loop.Condition is null ? [] : EnumerateCallableConstructions(loop.Condition))
                    .Concat(loop.Increment is null ? [] : EnumerateCallableConstructions(loop.Increment))
                    .Concat(loop.BodyStatements.SelectMany(EnumerateCallableConstructions));
            default:
                return [];
        }
    }

    private static IEnumerable<MirCallableConstructionExpression> EnumerateCallableConstructions(MirExpression expression)
    {
        IEnumerable<MirCallableConstructionExpression> Children(IEnumerable<MirExpression> expressions)
            => expressions.SelectMany(EnumerateCallableConstructions);

        return expression switch
        {
            MirCallableConstructionExpression construction => [construction, .. Children(construction.Captures)],
            MirInvokeExpression invoke => Children([invoke.Callee, .. invoke.Arguments]),
            MirAssignmentExpression assignment => EnumerateCallableConstructions(assignment.Expression),
            MirUnaryExpression unary => EnumerateCallableConstructions(unary.Operand),
            MirBinaryExpression binary => Children([binary.Left, binary.Right]),
            MirCallExpression call => Children(call.Arguments),
            MirArrayExpression array => Children(array.Elements),
            MirRecordConstructionExpression record => Children(record.Initializers.Select(initializer => initializer.Value)),
            MirRecordFieldAccessExpression access => EnumerateCallableConstructions(access.Receiver),
            MirRecordWithExpression update => Children([update.Source, .. update.Replacements.Select(replacement => replacement.Value)]),
            MirEnumValueExpression value => Children(value.Arguments),
            MirMatchExpression match => Children([match.Scrutinee, .. match.Arms.Select(arm => arm.Expression)]),
            MirResultMatchExpression match => Children([match.Scrutinee, match.OkExpression, match.ErrExpression]),
            MirIfExpression conditional => Children([conditional.Condition, conditional.ThenExpression, conditional.ElseExpression]),
            MirOkExpression ok => EnumerateCallableConstructions(ok.Payload),
            MirErrExpression err => EnumerateCallableConstructions(err.Payload),
            MirPropagateExpression propagate => EnumerateCallableConstructions(propagate.Operand),
            MirUnwrapExpression unwrap => EnumerateCallableConstructions(unwrap.Operand),
            MirTryExpression attempt => attempt.Protected.PrefixStatements.SelectMany(EnumerateCallableConstructions)
                .Concat(EnumerateCallableConstructions(attempt.Protected.ValueExpression))
                .Concat(attempt.Handler.PrefixStatements.SelectMany(EnumerateCallableConstructions))
                .Concat(EnumerateCallableConstructions(attempt.Handler.ValueExpression)),
            _ => [],
        };
    }

    private static string EmitBinary(
        CSharpTextWriter writer,
        MirBinaryExpression binary,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        string binaryOperator = binary.Operator switch
        {
            "===" => "==",
            "!==" => "!=",
            _ => binary.Operator,
        };

        if (binaryOperator is "&&" or "||"
            && (ExpressionRequiresStatements(binary.Left) || ExpressionRequiresStatements(binary.Right)))
        {
            return EmitStatementfulLogicalBinary(
                writer,
                binary,
                function,
                enumNames,
                ref tempIndex,
                diagnostics,
                binaryOperator);
        }

        if (!ExpressionRequiresStatements(binary.Left) && !ExpressionRequiresStatements(binary.Right))
        {
            string simpleLeft = ParenthesizeAssignmentOperand(binary.Left, EmitExpression(writer, binary.Left, function, enumNames, ref tempIndex, diagnostics));
            string simpleRight = ParenthesizeAssignmentOperand(binary.Right, EmitExpression(writer, binary.Right, function, enumNames, ref tempIndex, diagnostics));
            return $"({simpleLeft} {binaryOperator} {simpleRight})";
        }

        string left = ParenthesizeAssignmentOperand(binary.Left, EmitExpression(writer, binary.Left, function, enumNames, ref tempIndex, diagnostics));
        string leftTemporary = $"__cope_operand_{tempIndex++}";
        writer.WriteLine($"var {leftTemporary} = {left};");
        string right = ParenthesizeAssignmentOperand(binary.Right, EmitExpression(writer, binary.Right, function, enumNames, ref tempIndex, diagnostics));
        string rightTemporary = $"__cope_operand_{tempIndex++}";
        writer.WriteLine($"var {rightTemporary} = {right};");
        return $"({leftTemporary} {binaryOperator} {rightTemporary})";
    }

    private static string EmitStatementfulLogicalBinary(
        CSharpTextWriter writer,
        MirBinaryExpression binary,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics,
        string binaryOperator)
    {
        string left = ParenthesizeAssignmentOperand(binary.Left, EmitExpression(writer, binary.Left, function, enumNames, ref tempIndex, diagnostics));
        string resultTemporary = $"__cope_logical_result_{tempIndex++}";
        writer.WriteLine($"bool {resultTemporary};");
        string branchCondition = binaryOperator == "&&" ? left : $"!({left})";
        writer.WriteLine($"if ({branchCondition})");
        writer.WriteLine("{");
        writer.Indent();
        string right = ParenthesizeAssignmentOperand(binary.Right, EmitExpression(writer, binary.Right, function, enumNames, ref tempIndex, diagnostics));
        writer.WriteLine($"{resultTemporary} = {right};");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("else");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"{resultTemporary} = {(binaryOperator == "&&" ? "false" : "true")};");
        writer.Unindent();
        writer.WriteLine("}");
        return resultTemporary;
    }

    private static string ParenthesizeAssignmentOperand(MirExpression expression, string emitted)
        => expression is MirAssignmentExpression ? $"({emitted})" : emitted;

    private static string EmitIfExpression(
        CSharpTextWriter writer,
        MirIfExpression conditional,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        if (!ExpressionRequiresStatements(conditional.ThenExpression)
            && !ExpressionRequiresStatements(conditional.ElseExpression))
        {
            string condition = EmitExpression(writer, conditional.Condition, function, enumNames, ref tempIndex, diagnostics);
            string thenExpression = EmitExpression(writer, conditional.ThenExpression, function, enumNames, ref tempIndex, diagnostics);
            string elseExpression = EmitExpression(writer, conditional.ElseExpression, function, enumNames, ref tempIndex, diagnostics);
            return $"({condition} ? {thenExpression} : {elseExpression})";
        }

        string conditionValue = EmitExpression(writer, conditional.Condition, function, enumNames, ref tempIndex, diagnostics);
        string resultTemporary = $"__cope_if_result_{tempIndex++}";
        writer.WriteLine($"{MapValueStorageType(conditional.Type)} {resultTemporary};");
        writer.WriteLine($"if ({conditionValue})");
        writer.WriteLine("{");
        writer.Indent();
        string thenValue = EmitExpression(writer, conditional.ThenExpression, function, enumNames, ref tempIndex, diagnostics);
        writer.WriteLine($"{resultTemporary} = {thenValue};");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("else");
        writer.WriteLine("{");
        writer.Indent();
        string elseValue = EmitExpression(writer, conditional.ElseExpression, function, enumNames, ref tempIndex, diagnostics);
        writer.WriteLine($"{resultTemporary} = {elseValue};");
        writer.Unindent();
        writer.WriteLine("}");
        return resultTemporary;
    }

    private static string EmitRecordConstruction(
        CSharpTextWriter writer,
        MirRecordConstructionExpression construction,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        MirRecordDefinition record = GetRecordDefinition(construction.RecordTypeId);
        var valuesByField = new Dictionary<MirRecordFieldId, string>();

        foreach (var initializer in construction.Initializers)
        {
            string value = EmitExpression(writer, initializer.Value, function, enumNames, ref tempIndex, diagnostics);
            string temporary = $"__cope_record_init_{tempIndex++}";
            writer.WriteLine($"var {temporary} = {value};");
            valuesByField.Add(initializer.FieldId, temporary);
        }

        string arguments = string.Join(", ", record.Fields.Select(field => valuesByField[field.Id]));
        return $"new {RecordTypeName(record.Id)}({arguments})";
    }

    private static string EmitRecordFieldAccess(
        CSharpTextWriter writer,
        MirRecordFieldAccessExpression access,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        string receiver = EmitExpression(writer, access.Receiver, function, enumNames, ref tempIndex, diagnostics);
        return $"({receiver}).{RecordFieldName(access.FieldId)}";
    }

    private static string EmitRecordWith(
        CSharpTextWriter writer,
        MirRecordWithExpression withExpression,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        MirRecordDefinition record = GetRecordDefinition(withExpression.RecordTypeId);
        string sourceValue = EmitExpression(writer, withExpression.Source, function, enumNames, ref tempIndex, diagnostics);
        string sourceTemporary = $"__cope_record_source_{tempIndex++}";
        writer.WriteLine($"var {sourceTemporary} = {sourceValue};");

        var replacementsByField = new Dictionary<MirRecordFieldId, string>();
        foreach (var replacement in withExpression.Replacements)
        {
            string replacementValue = EmitExpression(writer, replacement.Value, function, enumNames, ref tempIndex, diagnostics);
            string replacementTemporary = $"__cope_record_replacement_{tempIndex++}";
            writer.WriteLine($"var {replacementTemporary} = {replacementValue};");
            replacementsByField.Add(replacement.FieldId, replacementTemporary);
        }

        var arguments = new List<string>(record.Fields.Count);
        foreach (var field in record.Fields)
        {
            if (replacementsByField.TryGetValue(field.Id, out string? replacement))
            {
                arguments.Add(replacement);
            }
            else
            {
                arguments.Add($"{sourceTemporary}.{RecordFieldName(field.Id)}");
            }
        }

        return $"new {RecordTypeName(record.Id)}({string.Join(", ", arguments)})";
    }

    private static MirRecordDefinition GetRecordDefinition(MirRecordTypeId id)
    {
        CSharpEmissionState state = CurrentEmissionState.Value
            ?? throw new InvalidOperationException("Record emission requires function-local state.");
        return state.Records[id];
    }

    private static string EmitPropagation(CSharpTextWriter writer, MirPropagateExpression propagation, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        if (propagation.Target is MirPropagationTarget.LexicalExcept lexical)
        {
            var state = CurrentEmissionState.Value;
            if (state is null || !state.Handlers.TryGetValue(lexical.HandlerId, out var handler))
            {
                diagnostics.Add(new CSharpDiagnostic("COPE-CS-0002", $"Lexical except propagation target '{lexical.HandlerId}' is not active."));
                return "default!";
            }

            var temporary = $"__cope_tmp{tempIndex++}";
            writer.WriteLine($"var {temporary} = {EmitExpression(writer, propagation.Operand, function, enumNames, ref tempIndex, diagnostics)};");
            writer.WriteLine($"if (!{temporary}.IsOk)");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"{handler.ErrorTemporary} = {temporary}.Error;");
            writer.WriteLine($"goto {handler.Label};");
            writer.Unindent();
            writer.WriteLine("}");
            return $"{temporary}.Value";
        }

        if (function.ReturnType is not MirResultType functionResult || propagation.Operand.Type is not MirResultType operandResult || !MirTypeFacts.AreEquivalent(functionResult.ErrorType, operandResult.ErrorType))
        {
            diagnostics.Add(new CSharpDiagnostic("COPE-CS-0002", "Function-return propagation requires compatible Result error types.")); return "default!";
        }
        var functionTemporary = $"__cope_tmp{tempIndex++}"; writer.WriteLine($"var {functionTemporary} = {EmitExpression(writer, propagation.Operand, function, enumNames, ref tempIndex, diagnostics)};"); writer.WriteLine($"if (!{functionTemporary}.IsOk)"); writer.WriteLine("{"); writer.Indent(); writer.WriteLine($"return CopeResult<{MapResultComponentType(functionResult.SuccessType)}, {MapType(functionResult.ErrorType)}>.Err({functionTemporary}.Error);"); writer.Unindent(); writer.WriteLine("}"); return $"{functionTemporary}.Value";
    }

    private static string EmitUnwrap(CSharpTextWriter writer, MirUnwrapExpression unwrap, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        if (unwrap.Operand.Type is not MirResultType resultType || !MirTypeFacts.AreEquivalent(unwrap.Type, resultType.SuccessType))
        {
            diagnostics.Add(new CSharpDiagnostic("COPE-CS-0002", "Result unwrap requires a Result operand and its success type."));
            return "default!";
        }

        string temporary = $"__cope_tmp{tempIndex++}";
        writer.WriteLine($"var {temporary} = {EmitExpression(writer, unwrap.Operand, function, enumNames, ref tempIndex, diagnostics)};");
        writer.WriteLine($"if (!{temporary}.IsOk)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"throw new CopeUnwrapPanicException({temporary}.Error);");
        writer.Unindent();
        writer.WriteLine("}");
        return $"{temporary}.Value";
    }

    private static string EmitTryExcept(CSharpTextWriter writer, MirTryExpression tryExpression, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        var state = CurrentEmissionState.Value;
        if (state is null)
        {
            diagnostics.Add(new CSharpDiagnostic("COPE-CS-0002", "Try/except emission requires a function-local emission state."));
            return "default!";
        }

        if (state.Handlers.ContainsKey(tryExpression.HandlerId))
        {
            diagnostics.Add(new CSharpDiagnostic("COPE-CS-0002", $"Duplicate active try handler identity '{tryExpression.HandlerId}'."));
            return "default!";
        }

        var index = state.TryIndex++;
        var suffix = $"h{tryExpression.HandlerId.Value}_{index}";
        var resultTemporary = $"__cope_try_result_{suffix}";
        var errorTemporary = $"__cope_try_error_{suffix}";
        var handlerLabel = $"__cope_try_handler_{suffix}";
        var joinLabel = $"__cope_try_join_{suffix}";

        writer.WriteLine($"{MapValueStorageType(tryExpression.Type)} {resultTemporary};");
        writer.WriteLine($"{MapType(tryExpression.HandledErrorType)} {errorTemporary} = default!;");
        state.Handlers.Add(tryExpression.HandlerId, new HandlerTransfer(errorTemporary, handlerLabel));
        EmitTryValueBlock(writer, tryExpression.Protected, resultTemporary, joinLabel, function, enumNames, ref tempIndex, diagnostics);
        state.Handlers.Remove(tryExpression.HandlerId);
        writer.WriteLine($"{handlerLabel}:");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"var {CSharpNameMangler.Mangle(tryExpression.HandlerBinding.Name)} = {errorTemporary};");
        EmitTryValueBlock(writer, tryExpression.Handler, resultTemporary, joinLabel, function, enumNames, ref tempIndex, diagnostics);
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"{joinLabel}:");
        return resultTemporary;
    }

    private static void EmitTryValueBlock(CSharpTextWriter writer, MirValueBlock block, string resultTemporary, string joinLabel, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        writer.WriteLine("{");
        writer.Indent();
        foreach (var statement in block.PrefixStatements)
        {
            if (statement is MirExpressionStatement expressionStatement)
            {
                writer.WriteLine($"_ = {EmitExpression(writer, expressionStatement.Expression, function, enumNames, ref tempIndex, diagnostics)};");
            }
            else
            {
                EmitStatement(writer, statement, function, enumNames, ref tempIndex, diagnostics);
            }
        }

        writer.WriteLine($"{resultTemporary} = {EmitExpression(writer, block.ValueExpression, function, enumNames, ref tempIndex, diagnostics)};");
        writer.WriteLine($"goto {joinLabel};");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static string EmitResultMatch(CSharpTextWriter writer, MirResultMatchExpression match, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        var resultTemporary = $"__cope_tmp{tempIndex++}"; var valueTemporary = $"__cope_tmp{tempIndex++}";
        writer.WriteLine($"var {resultTemporary} = {EmitExpression(writer, match.Scrutinee, function, enumNames, ref tempIndex, diagnostics)};"); writer.WriteLine($"{MapType(match.Type)} {valueTemporary};"); writer.WriteLine($"if ({resultTemporary}.IsOk)"); writer.WriteLine("{"); writer.Indent(); writer.WriteLine($"var {CSharpNameMangler.Mangle(match.OkBinding.Name)} = {resultTemporary}.Value;"); writer.WriteLine($"{valueTemporary} = {EmitExpression(writer, match.OkExpression, function, enumNames, ref tempIndex, diagnostics)};"); writer.Unindent(); writer.WriteLine("}"); writer.WriteLine("else"); writer.WriteLine("{"); writer.Indent(); writer.WriteLine($"var {CSharpNameMangler.Mangle(match.ErrBinding.Name)} = {resultTemporary}.Error;"); writer.WriteLine($"{valueTemporary} = {EmitExpression(writer, match.ErrExpression, function, enumNames, ref tempIndex, diagnostics)};"); writer.Unindent(); writer.WriteLine("}"); return valueTemporary;
    }

    private static string EmitEnumMatch(CSharpTextWriter writer, MirMatchExpression match, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        if (match.Scrutinee.Type is not MirType enumType || match.Scrutinee.Type is MirArrayType or MirResultType || !enumNames.Contains(enumType.Identifier)) return UnsupportedExpression(match, diagnostics);
        if (match.Arms.Any(arm => ExpressionRequiresStatements(arm.Expression)))
        {
            return EmitStatementfulEnumMatch(writer, match, function, enumNames, ref tempIndex, diagnostics, enumType);
        }
        var scrutinee = EmitExpression(writer, match.Scrutinee, function, enumNames, ref tempIndex, diagnostics);
        var arms = new List<string>();
        foreach (var arm in match.Arms)
        {
            var pattern = arm.PayloadBindings.Count == 0 ? $"{CSharpNameMangler.Mangle(enumType.Identifier)}.{CSharpNameMangler.Mangle(arm.CaseName)} _" : $"{CSharpNameMangler.Mangle(enumType.Identifier)}.{CSharpNameMangler.Mangle(arm.CaseName)}({string.Join(", ", arm.PayloadBindings.Select(binding => $"var {CSharpNameMangler.Mangle(binding.Name)}"))})";
            arms.Add($"{pattern} => {EmitExpression(writer, arm.Expression, function, enumNames, ref tempIndex, diagnostics)}");
        }
        arms.Add("_ => throw new global::System.InvalidOperationException(\"Non-exhaustive match.\")");
        return $"{scrutinee} switch {{ {string.Join(", ", arms)} }}";
    }

    private static string EmitStatementfulEnumMatch(
        CSharpTextWriter writer,
        MirMatchExpression match,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics,
        MirType enumType)
    {
        string scrutineeValue = EmitExpression(writer, match.Scrutinee, function, enumNames, ref tempIndex, diagnostics);
        string scrutineeTemporary = $"__cope_match_scrutinee_{tempIndex++}";
        string resultTemporary = $"__cope_match_result_{tempIndex++}";
        writer.WriteLine($"{MapType(enumType)} {scrutineeTemporary} = {scrutineeValue};");
        writer.WriteLine($"{MapValueStorageType(match.Type)} {resultTemporary};");
        writer.WriteLine($"switch ({scrutineeTemporary})");
        writer.WriteLine("{");
        writer.Indent();

        foreach (var arm in match.Arms)
        {
            string enumName = CSharpNameMangler.Mangle(enumType.Identifier);
            string caseName = CSharpNameMangler.Mangle(arm.CaseName);
            string pattern = arm.PayloadBindings.Count == 0
                ? $"{enumName}.{caseName} _"
                : $"{enumName}.{caseName}({string.Join(", ", arm.PayloadBindings.Select(binding => $"var {CSharpNameMangler.Mangle(binding.Name)}"))})";
            writer.WriteLine($"case {pattern}:");
            writer.WriteLine("{");
            writer.Indent();
            string armValue = EmitExpression(writer, arm.Expression, function, enumNames, ref tempIndex, diagnostics);
            writer.WriteLine($"{resultTemporary} = {armValue};");
            writer.WriteLine("break;");
            writer.Unindent();
            writer.WriteLine("}");
        }

        writer.WriteLine("default:");
        writer.Indent();
        writer.WriteLine("throw new global::System.InvalidOperationException(\"Non-exhaustive match.\");");
        writer.Unindent();
        writer.Unindent();
        writer.WriteLine("}");
        return resultTemporary;
    }

    private static List<string> EmitArguments(IReadOnlyList<MirExpression> expressions, CSharpTextWriter writer, MirFunction function, IReadOnlySet<string> enumNames, ref int tempIndex, List<CSharpDiagnostic> diagnostics)
    {
        var values = new List<string>(expressions.Count);
        bool requiresOrderedStaging = expressions.Any(ExpressionRequiresStatements);
        foreach (var expression in expressions)
        {
            string value = EmitExpression(writer, expression, function, enumNames, ref tempIndex, diagnostics);
            if (!requiresOrderedStaging)
            {
                values.Add(value);
                continue;
            }

            string temporary = $"__cope_argument_{tempIndex++}";
            writer.WriteLine($"var {temporary} = {value};");
            values.Add(temporary);
        }
        return values;
    }

    private static string UnsupportedExpression(MirExpression expression, List<CSharpDiagnostic> diagnostics) { diagnostics.Add(new CSharpDiagnostic("COPE-CS-0001", $"Unsupported MIR expression: {expression.GetType().Name}")); return "default!"; }

    private static void EmitTsonEncodingRuntime(
        CSharpTextWriter writer,
        IReadOnlyList<MirTsonEncodingPlan> plans,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records,
        bool emitTransportDecoders)
    {
        bool needsArrayWriter = plans.Any(plan => CollectTsonArrayPlans(plan).Count > 0);
        EmitTsonWriter(writer, needsArrayWriter);
        foreach (MirTsonEncodingPlan plan in plans)
        {
            EmitTsonEncodingPlan(writer, plan, records);
        }
        if (emitTransportDecoders)
        {
            EmitTsonReader(writer);
            foreach (MirTsonEncodingPlan plan in plans.Where(plan => plan.RootType is MirRecordType && plan.TablePlan is null))
            {
                EmitTsonFlatRecordDecoder(writer, plan, records);
            }
        }
    }

    private static void EmitTsonReader(CSharpTextWriter writer)
    {
        writer.WriteLine("""
            private sealed class __TsonReader
            {
                private readonly string text;
                private int position;
                internal __TsonReader(string value) { text = value; }
                internal bool End => position == text.Length;
                internal bool Expect(string value)
                {
                    if (position > text.Length - value.Length || string.CompareOrdinal(text, position, value, 0, value.Length) != 0) return false;
                    position += value.Length;
                    return true;
                }
                internal void SkipWhitespace()
                {
                    while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                }
                internal bool TryBoolean(out bool value)
                {
                    if (Expect("true")) { value = true; return true; }
                    if (Expect("false")) { value = false; return true; }
                    value = false;
                    return false;
                }
                internal bool TryNumber(out double value)
                {
                    value = 0;
                    if (!Expect("$number(\"")) return false;
                    int start = position;
                    while (position < text.Length && text[position] != '"') position++;
                    if (position - start != 16 || !Expect("\")")) return false;
                    string hexadecimal = text.Substring(start, 16);
                    if (!ulong.TryParse(hexadecimal, global::System.Globalization.NumberStyles.AllowHexSpecifier, global::System.Globalization.CultureInfo.InvariantCulture, out ulong bits)) return false;
                    value = global::System.BitConverter.Int64BitsToDouble(unchecked((long)bits));
                    return true;
                }
                internal bool TryString(out string value)
                {
                    value = string.Empty;
                    if (position >= text.Length || text[position++] != '"') return false;
                    var builder = new global::System.Text.StringBuilder();
                    while (position < text.Length)
                    {
                        char current = text[position++];
                        if (current == '"') { value = builder.ToString(); return true; }
                        if (current < ' ') return false;
                        if (current != '\\') { builder.Append(current); continue; }
                        if (position >= text.Length) return false;
                        char escape = text[position++];
                        if (escape == '"' || escape == '\\') builder.Append(escape);
                        else if (escape == 'n') builder.Append('\n');
                        else if (escape == 'r') builder.Append('\r');
                        else if (escape == 't') builder.Append('\t');
                        else return false;
                    }
                    return false;
                }
            }
            """);
        writer.WriteLine();
    }

    private static void EmitTsonFlatRecordDecoder(
        CSharpTextWriter writer,
        MirTsonEncodingPlan plan,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records)
    {
        if (plan.RootValuePlan is not MirTsonRecordValuePlan root
            || !records.TryGetValue(root.RecordTypeId, out MirRecordDefinition? record)
            || plan.Definitions.OfType<MirTsonRecordPlan>().SingleOrDefault(candidate => candidate.RecordTypeId == root.RecordTypeId) is not MirTsonRecordPlan recordPlan)
        {
            return;
        }

        writer.WriteLine($"private static {RecordTypeName(record.Id)} {TsonDecodeMethodName(plan.Id)}(string text)");
        writer.WriteLine("{");
        writer.Indent();
        string rootPrefix = "$record." + record.Name + "({\n";
        writer.WriteLine("if (!text.StartsWith(\"const $schema: string = \\\"\", global::System.StringComparison.Ordinal)) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at schema.\");");
        writer.WriteLine($"int rootPosition = text.IndexOf({CSharpLiteralWriter.Write(rootPrefix)}, global::System.StringComparison.Ordinal);");
        writer.WriteLine("if (rootPosition < 0) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at record prefix.\");");
        writer.WriteLine("var reader = new __TsonReader(text.Substring(rootPosition));");
        writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write(rootPrefix)})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at record prefix.\");");
        var valuesByFieldId = new Dictionary<MirRecordFieldId, string>();
        int temporaryIndex = 0;
        for (int index = 0; index < recordPlan.Fields.Count; index++)
        {
            MirTsonRecordFieldPlan fieldPlan = recordPlan.Fields[index];
            MirRecordFieldDefinition field = record.Fields.Single(candidate => candidate.Id == fieldPlan.FieldId);
            writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write("    \"" + fieldPlan.Name + "\": ")})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at field label {fieldPlan.Name}.\");");
            string variable = EmitTsonDecoderValue(writer, plan, fieldPlan.ValuePlan, field.Type, records, indentation: 1, ref temporaryIndex);
            writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write(",\n")})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at field separator.\");");
            valuesByFieldId.Add(field.Id, variable);
        }
        writer.WriteLine("if (!reader.Expect(\"})\") || !reader.Expect(\";\\n\") || !reader.End) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at suffix.\");");
        writer.WriteLine($"return new {RecordTypeName(record.Id)}({string.Join(", ", record.Fields.Select(field => valuesByFieldId[field.Id]))});");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static string EmitTsonDecoderValue(
        CSharpTextWriter writer,
        MirTsonEncodingPlan plan,
        MirTsonValuePlan valuePlan,
        MirType type,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records,
        int indentation,
        ref int temporaryIndex)
    {
        string variable = "field" + temporaryIndex++;
        switch (valuePlan)
        {
            case MirTsonBooleanPlan:
                writer.WriteLine($"if (!reader.TryBoolean(out bool {variable})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at boolean value.\");");
                return variable;
            case MirTsonNumberPlan:
                writer.WriteLine($"if (!reader.TryNumber(out double {variable})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at number value.\");");
                return variable;
            case MirTsonStringPlan:
                writer.WriteLine($"if (!reader.TryString(out string {variable})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at string value.\");");
                return variable;
            case MirTsonArrayPlan arrayPlan when type is MirArrayType arrayType:
                string elementType = MapType(arrayType.ElementType);
                string items = "items" + temporaryIndex++;
                writer.WriteLine($"var {items} = new global::System.Collections.Generic.List<{elementType}>();");
                writer.WriteLine("reader.SkipWhitespace();");
                writer.WriteLine("if (!reader.Expect(\"[\")) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at array prefix.\");");
                writer.WriteLine("while (true)");
                writer.WriteLine("{");
                writer.Indent();
                writer.WriteLine("reader.SkipWhitespace();");
                writer.WriteLine("if (reader.Expect(\"]\")) break;");
                string element = EmitTsonDecoderValue(writer, plan, arrayPlan.ElementPlan, arrayType.ElementType, records, indentation + 1, ref temporaryIndex);
                writer.WriteLine($"{items}.Add({element});");
                writer.WriteLine("reader.SkipWhitespace();");
                writer.WriteLine("if (!reader.Expect(\",\")) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at array separator.\");");
                writer.Unindent();
                writer.WriteLine("}");
                writer.WriteLine($"var {variable} = {items}.ToArray();");
                return variable;
            case MirTsonRecordValuePlan recordValue when type is MirRecordType recordType
                && records.TryGetValue(recordValue.RecordTypeId, out MirRecordDefinition? record)
                && plan.Definitions.OfType<MirTsonRecordPlan>().SingleOrDefault(candidate => candidate.RecordTypeId == recordValue.RecordTypeId) is MirTsonRecordPlan recordPlan:
                writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write("$record." + record.Name + "({\n")})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at nested record prefix.\");");
                var values = new Dictionary<MirRecordFieldId, string>();
                for (int index = 0; index < recordPlan.Fields.Count; index++)
                {
                    MirTsonRecordFieldPlan fieldPlan = recordPlan.Fields[index];
                    MirRecordFieldDefinition field = record.Fields.Single(candidate => candidate.Id == fieldPlan.FieldId);
                    string fieldPrefix = new string(' ', 4 * (indentation + 1)) + "\"" + fieldPlan.Name + "\": ";
                    writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write(fieldPrefix)})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at nested field label.\");");
                    values.Add(field.Id, EmitTsonDecoderValue(writer, plan, fieldPlan.ValuePlan, field.Type, records, indentation + 1, ref temporaryIndex));
                    writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write(",\n")})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at nested field separator.\");");
                }
                writer.WriteLine($"if (!reader.Expect({CSharpLiteralWriter.Write(new string(' ', 4 * indentation) + "})")})) throw new global::System.InvalidOperationException(\"Malformed TSON transport payload at nested record suffix.\");");
                writer.WriteLine($"var {variable} = new {RecordTypeName(record.Id)}({string.Join(", ", record.Fields.Select(field => values[field.Id]))});");
                return variable;
            default:
                throw new InvalidOperationException("Transport decoder received an unsupported nested TSON value plan.");
        }
    }

    private static void EmitTsonWriter(CSharpTextWriter writer, bool needsArrayWriter)
    {
        writer.WriteLine("private sealed class __TsonWriter");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("private readonly global::System.Text.StringBuilder _builder = new();");
        writer.WriteLine("private readonly int _maximumBytes;");
        writer.WriteLine("private readonly int _maximumStringCodeUnits;");
        writer.WriteLine("private int _bytes;");
        writer.WriteLine("internal TsonEncodeError? Error { get; private set; }");
        writer.WriteLine();
        writer.WriteLine("internal __TsonWriter(int maximumBytes, int maximumStringCodeUnits)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("_maximumBytes = maximumBytes;");
        writer.WriteLine("_maximumStringCodeUnits = maximumStringCodeUnits;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("internal bool Static(string value) => AppendRaw(value, false);");
        writer.WriteLine();
        writer.WriteLine("internal bool Indent(int level)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("return Static(new string(' ', level * 4));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("internal bool String(string value)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (value.Length > _maximumStringCodeUnits)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("return FailOutputLimit();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("for (int index = 0; index < value.Length; index++)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("char character = value[index];");
        writer.WriteLine("if (char.IsHighSurrogate(character))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return FailInvalidUnicode();");
        writer.WriteLine("index++;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("else if (char.IsLowSurrogate(character)) return FailInvalidUnicode();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("if (!Static(\"\\\"\")) return false;");
        writer.WriteLine("for (int index = 0; index < value.Length; index++)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("char character = value[index];");
        writer.WriteLine("switch (character)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("case '\"': if (!Static(\"\\\\\\\"\")) return false; break;");
        writer.WriteLine("case '\\\\': if (!Static(\"\\\\\\\\\")) return false; break;");
        writer.WriteLine("case '\\b': if (!Static(\"\\\\b\")) return false; break;");
        writer.WriteLine("case '\\f': if (!Static(\"\\\\f\")) return false; break;");
        writer.WriteLine("case '\\n': if (!Static(\"\\\\n\")) return false; break;");
        writer.WriteLine("case '\\r': if (!Static(\"\\\\r\")) return false; break;");
        writer.WriteLine("case '\\t': if (!Static(\"\\\\t\")) return false; break;");
        writer.WriteLine("case '\\u2028': if (!UnicodeEscape(character)) return false; break;");
        writer.WriteLine("case '\\u2029': if (!UnicodeEscape(character)) return false; break;");
        writer.WriteLine("default:");
        writer.Indent();
        writer.WriteLine("if (character < ' ') { if (!UnicodeEscape(character)) return false; }");
        writer.WriteLine("else if (char.IsHighSurrogate(character))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return FailInvalidUnicode();");
        writer.WriteLine("if (!AppendScalar(character, value[++index])) return false;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("else if (char.IsLowSurrogate(character)) return FailInvalidUnicode();");
        writer.WriteLine("else if (!AppendRaw(character.ToString(), false)) return false;");
        writer.WriteLine("break;");
        writer.Unindent();
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("return Static(\"\\\"\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("internal bool Number(double value)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("ulong bits = global::System.BitConverter.DoubleToUInt64Bits(value);");
        writer.WriteLine("if ((bits & 0x7FF0000000000000UL) == 0x7FF0000000000000UL && (bits & 0x000FFFFFFFFFFFFFUL) != 0) bits = 0x7FF8000000000000UL;");
        writer.WriteLine("return Static(\"$number(\\\"\" + bits.ToString(\"X16\", global::System.Globalization.CultureInfo.InvariantCulture) + \"\\\")\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("internal string Finish() => _builder.ToString();");
        writer.WriteLine();
        writer.WriteLine("private bool UnicodeEscape(char value)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("return Static(\"\\\\u\" + ((int)value).ToString(\"X4\", global::System.Globalization.CultureInfo.InvariantCulture));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("private bool AppendScalar(char high, char low)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (_bytes > _maximumBytes - 4) return FailOutputLimit();");
        writer.WriteLine("_bytes += 4;");
        writer.WriteLine("_builder.Append(high);");
        writer.WriteLine("_builder.Append(low);");
        writer.WriteLine("return true;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("private bool AppendRaw(string value, bool enforceStringLimit)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (enforceStringLimit && value.Length > _maximumStringCodeUnits) return FailOutputLimit();");
        writer.WriteLine("int byteCount = 0;");
        writer.WriteLine("for (int index = 0; index < value.Length; index++)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("char character = value[index];");
        writer.WriteLine("if (character <= 0x7F) byteCount += 1;");
        writer.WriteLine("else if (character <= 0x7FF) byteCount += 2;");
        writer.WriteLine("else if (char.IsHighSurrogate(character))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return FailInvalidUnicode();");
        writer.WriteLine("byteCount += 4;");
        writer.WriteLine("index++;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("else if (char.IsLowSurrogate(character)) return FailInvalidUnicode();");
        writer.WriteLine("else byteCount += 3;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("if (_bytes > _maximumBytes - byteCount) return FailOutputLimit();");
        writer.WriteLine("_bytes += byteCount;");
        writer.WriteLine("_builder.Append(value);");
        writer.WriteLine("return true;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("private bool FailInvalidUnicode()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("Error = new TsonEncodeError.InvalidUnicode();");
        writer.WriteLine("return false;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("private bool FailOutputLimit()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("Error = new TsonEncodeError.OutputLimitExceeded();");
        writer.WriteLine("return false;");
        writer.Unindent();
        writer.WriteLine("}");
        if (needsArrayWriter)
        {
            writer.WriteLine();
            writer.WriteLine("internal bool OutputLimit() => FailOutputLimit();");
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("private static bool __tson_write_boolean(__TsonWriter writer, bool value, int indentation) => writer.Static(value ? \"true\" : \"false\");");
        writer.WriteLine("private static bool __tson_write_number(__TsonWriter writer, double value, int indentation) => writer.Number(value);");
        writer.WriteLine("private static bool __tson_write_string(__TsonWriter writer, string value, int indentation) => writer.String(value);");
        writer.WriteLine();
    }

    private static void EmitTsonEncodingPlan(
        CSharpTextWriter writer,
        MirTsonEncodingPlan plan,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records)
    {
        IReadOnlyList<MirTsonArrayPlan> arrayPlans = CollectTsonArrayPlans(plan);
        string resultType = $"CopeResult<string, TsonEncodeError>";
        if (plan.TablePlan is not null)
        {
            EmitTsonTableEncodingPlan(writer, plan, arrayPlans, resultType);
        }
        else
        {
        writer.WriteLine($"private static {resultType} {TsonEncodeMethodName(plan.Id)}({MapType(plan.RootType)} value)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"var writer = new __TsonWriter({plan.Limits.MaximumUtf8Bytes}, {plan.Limits.MaximumStringCodeUnits});");
        string prefix = MirTsonCanonicalText.BuildDocumentPrefix(plan);
        writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write(prefix)})");
        writer.Indent();
        writer.WriteLine($"|| !{TsonValueWriterName(plan.Id, plan.RootValuePlan, arrayPlans)}(writer, value, 0)");
        writer.WriteLine("|| !writer.Static(\";\\n\"))");
        writer.Unindent();
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"return {resultType}.Err(writer.Error ?? new TsonEncodeError.OutputLimitExceeded());");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"return {resultType}.Ok(writer.Finish());");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        }

        foreach (MirTsonNominalPlan definition in plan.Definitions)
        {
            switch (definition)
            {
                case MirTsonRecordPlan record:
                    EmitTsonRecordWriter(writer, plan, record, records, arrayPlans);
                    break;
                case MirTsonEnumPlan @enum:
                    EmitTsonEnumWriter(writer, plan, @enum, arrayPlans);
                    break;
            }
        }

        foreach (MirTsonArrayPlan arrayPlan in arrayPlans)
        {
            EmitTsonArrayWriter(writer, plan, arrayPlan, arrayPlans);
        }
    }

    private static void EmitTsonTableEncodingPlan(
        CSharpTextWriter writer,
        MirTsonEncodingPlan plan,
        IReadOnlyList<MirTsonArrayPlan> arrayPlans,
        string resultType)
    {
        MirTsonTablePlan table = plan.TablePlan!;
        writer.WriteLine($"private static {resultType} {TsonEncodeMethodName(plan.Id)}({TableTypeName(table.TableId)} value)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"if (!object.ReferenceEquals(value, {TableSingletonName(table.TableId)})) throw new global::System.InvalidOperationException(\"Copeland C# backend invariant failure.\");");
        for (int index = 0; index < table.Columns.Count; index++)
        {
            MirTsonTableColumnPlan column = table.Columns[index];
            writer.WriteLine($"var column{index} = value.{TableTsonStorageAccessName(column.ColumnId)}();");
            writer.WriteLine($"var length{index} = column{index} is null ? -1 : column{index}.Length;");
            writer.WriteLine($"if (length{index} != {column.ExpectedElementCount}) throw new global::System.InvalidOperationException(\"Copeland C# backend invariant failure.\");");
        }
        writer.WriteLine($"var writer = new __TsonWriter({plan.Limits.MaximumUtf8Bytes}, {plan.Limits.MaximumStringCodeUnits});");
        writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write(MirTsonCanonicalText.BuildDocumentPrefix(plan))}))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"return {resultType}.Err(writer.Error ?? new TsonEncodeError.OutputLimitExceeded());");
        writer.Unindent();
        writer.WriteLine("}");
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            MirTsonTableColumnPlan column = table.Columns[columnIndex];
            writer.WriteLine($"if (length{columnIndex} == 0)");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write(MirTsonCanonicalText.BuildTableColumnPrefix(plan, column))}) || !writer.Static(\"[];\\n\"))");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"return {resultType}.Err(writer.Error ?? new TsonEncodeError.OutputLimitExceeded());");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine("goto " + $"__tson_table_column_done_{columnIndex};");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write(MirTsonCanonicalText.BuildTableColumnPrefix(plan, column))}) || !writer.Static(\"[\\n\"))");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"return {resultType}.Err(writer.Error ?? new TsonEncodeError.OutputLimitExceeded());");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine($"for (var index = 0; index < length{columnIndex}; index++)");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"var cell = column{columnIndex}[index];");
            writer.WriteLine("if (!writer.Indent(2)");
            writer.Indent();
            writer.WriteLine($"|| !{TsonValueWriterName(plan.Id, column.ElementPlan, arrayPlans)}(writer, cell, 2)");
            writer.WriteLine("|| !writer.Static(\",\\n\"))");
            writer.Unindent();
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"return {resultType}.Err(writer.Error ?? new TsonEncodeError.OutputLimitExceeded());");
            writer.Unindent();
            writer.WriteLine("}");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine("if (!writer.Indent(1) || !writer.Static(\"];\\n\"))");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine($"return {resultType}.Err(writer.Error ?? new TsonEncodeError.OutputLimitExceeded());");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine($"__tson_table_column_done_{columnIndex}: ;");
        }
        writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write(MirTsonCanonicalText.BuildTableDocumentSuffix(plan))}))");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"return {resultType}.Err(writer.Error ?? new TsonEncodeError.OutputLimitExceeded());");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"return {resultType}.Ok(writer.Finish());");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitTsonRecordWriter(CSharpTextWriter writer, MirTsonEncodingPlan plan, MirTsonRecordPlan record, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, IReadOnlyList<MirTsonArrayPlan> arrayPlans)
    {
        writer.WriteLine($"private static bool {TsonValueWriterName(plan.Id, new MirTsonRecordValuePlan(record.RecordTypeId))}(__TsonWriter writer, {RecordTypeName(record.RecordTypeId)} value, int indentation)");
        writer.WriteLine("{");
        writer.Indent();
        if (record.Fields.Count == 0)
        {
            writer.WriteLine($"return writer.Static({CSharpLiteralWriter.Write($"$record.{record.Name}({{}})")});");
        }
        else
        {
            writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write($"$record.{record.Name}({{\n")}) ) return false;");
            MirRecordDefinition carrier = records[record.RecordTypeId];
            for (int index = 0; index < record.Fields.Count; index++)
            {
                MirTsonRecordFieldPlan field = record.Fields[index];
                MirRecordFieldDefinition carrierField = carrier.Fields[index];
                writer.WriteLine("if (!writer.Indent(indentation + 1)) return false;");
                writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write($"\"{field.Name}\": ")})) return false;");
                writer.WriteLine($"if (!{TsonValueWriterName(plan.Id, field.ValuePlan, arrayPlans)}(writer, value.{RecordFieldName(carrierField.Id)}, indentation + 1)) return false;");
                writer.WriteLine("if (!writer.Static(\",\\n\")) return false;");
            }
            writer.WriteLine("if (!writer.Indent(indentation)) return false;");
            writer.WriteLine("return writer.Static(\"})\");");
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitTsonEnumWriter(CSharpTextWriter writer, MirTsonEncodingPlan plan, MirTsonEnumPlan @enum, IReadOnlyList<MirTsonArrayPlan> arrayPlans)
    {
        string methodName = TsonValueWriterName(plan.Id, new MirTsonEnumValuePlan(@enum.Name));
        string typeName = CSharpNameMangler.Mangle(@enum.Name);
        writer.WriteLine($"private static bool {methodName}(__TsonWriter writer, {typeName} value, int indentation)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("switch (value)");
        writer.WriteLine("{");
        writer.Indent();
        foreach (MirTsonEnumCasePlan @case in @enum.Cases)
        {
            string caseType = $"{typeName}.{CSharpNameMangler.Mangle(@case.Name)}";
            string variable = @case.Payloads.Count == 0 ? "" : " item";
            writer.WriteLine($"case {caseType}{variable}:");
            writer.Indent();
            if (@case.Payloads.Count == 0)
            {
                writer.WriteLine($"return writer.Static({CSharpLiteralWriter.Write($"{@enum.Name}.{@case.Name}")});");
            }
            else
            {
                writer.WriteLine($"if (!writer.Static({CSharpLiteralWriter.Write($"{@enum.Name}.{@case.Name}(\n")})) return false;");
                for (int index = 0; index < @case.Payloads.Count; index++)
                {
                    MirTsonEnumPayloadPlan payload = @case.Payloads[index];
                    writer.WriteLine("if (!writer.Indent(indentation + 1)) return false;");
                    writer.WriteLine($"if (!{TsonValueWriterName(plan.Id, payload.ValuePlan, arrayPlans)}(writer, item.{CSharpNameMangler.Mangle(payload.Name)}, indentation + 1)) return false;");
                    writer.WriteLine(index + 1 < @case.Payloads.Count
                        ? "if (!writer.Static(\",\\n\")) return false;"
                        : "if (!writer.Static(\"\\n\")) return false;");
                }
                writer.WriteLine("if (!writer.Indent(indentation)) return false;");
                writer.WriteLine("return writer.Static(\")\");");
            }
            writer.Unindent();
        }
        writer.WriteLine("default:");
        writer.Indent();
        writer.WriteLine("throw new global::System.InvalidOperationException(\"Copeland C# backend invariant failure.\");");
        writer.Unindent();
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void EmitTsonArrayWriter(
        CSharpTextWriter writer,
        MirTsonEncodingPlan plan,
        MirTsonArrayPlan arrayPlan,
        IReadOnlyList<MirTsonArrayPlan> arrayPlans)
    {
        string elementType = TsonValuePlanType(arrayPlan.ElementPlan);
        writer.WriteLine($"private static bool {TsonArrayWriterName(plan.Id, arrayPlan, arrayPlans)}(__TsonWriter writer, {elementType}[] value, int indentation)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("if (value is null) throw new global::System.InvalidOperationException(\"Copeland C# backend invariant failure.\");");
        writer.WriteLine("var array = value;");
        writer.WriteLine("var length = array.Length;");
        writer.WriteLine($"if (length > {plan.Limits.MaximumArrayLength}) return writer.OutputLimit();");
        writer.WriteLine("if (length == 0) return writer.Static(\"[]\");");
        writer.WriteLine("if (!writer.Static(\"[\\n\")) return false;");
        writer.WriteLine("for (var index = 0; index < length; index++)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("var element = array[index];");
        writer.WriteLine("if (!writer.Indent(indentation + 1)) return false;");
        writer.WriteLine($"if (!{TsonValueWriterName(plan.Id, arrayPlan.ElementPlan, arrayPlans)}(writer, element, indentation + 1)) return false;");
        writer.WriteLine("if (!writer.Static(\",\\n\")) return false;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("if (!writer.Indent(indentation)) return false;");
        writer.WriteLine("return writer.Static(\"]\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static string TsonEncodeMethodName(MirTsonEncodingPlanId id)
        => "__tson_encode_" + EncodeStableIdentity(id.Value);

    private static string TsonDecodeMethodName(MirTsonEncodingPlanId id)
        => "__tson_decode_" + EncodeStableIdentity(id.Value);

    private static string TsonValueWriterName(MirTsonEncodingPlanId id, MirTsonValuePlan valuePlan)
        => valuePlan switch
        {
            MirTsonBooleanPlan => "__tson_write_boolean",
            MirTsonNumberPlan => "__tson_write_number",
            MirTsonStringPlan => "__tson_write_string",
            MirTsonRecordValuePlan record => "__tson_write_" + EncodeStableIdentity(id.Value + "_record_" + record.RecordTypeId.Value),
            MirTsonEnumValuePlan @enum => "__tson_write_" + EncodeStableIdentity(id.Value + "_enum_" + @enum.EnumName),
            _ => throw new InvalidOperationException("Unsupported validated TSON value plan."),
        };

    private static string TsonValueWriterName(MirTsonEncodingPlanId id, MirTsonValuePlan valuePlan, IReadOnlyList<MirTsonArrayPlan> arrayPlans)
        => valuePlan is MirTsonArrayPlan array
            ? TsonArrayWriterName(id, array, arrayPlans)
            : TsonValueWriterName(id, valuePlan);

    private static string TsonArrayWriterName(MirTsonEncodingPlanId id, MirTsonArrayPlan arrayPlan, IReadOnlyList<MirTsonArrayPlan> arrayPlans)
        => "__tson_write_" + EncodeStableIdentity(id.Value + "_array_" + TsonArrayPlanIndex(arrayPlan, arrayPlans));

    private static int TsonArrayPlanIndex(MirTsonArrayPlan arrayPlan, IReadOnlyList<MirTsonArrayPlan> arrayPlans)
    {
        for (int index = 0; index < arrayPlans.Count; index++)
        {
            if (arrayPlans[index].Equals(arrayPlan))
            {
                return index;
            }
        }

        throw new InvalidOperationException("Validated TSON array plan was not collected.");
    }

    private static string TsonValuePlanType(MirTsonValuePlan valuePlan)
        => valuePlan switch
        {
            MirTsonBooleanPlan => "bool",
            MirTsonNumberPlan => "double",
            MirTsonStringPlan => "string",
            MirTsonRecordValuePlan record => RecordTypeName(record.RecordTypeId),
            MirTsonEnumValuePlan @enum => CSharpNameMangler.Mangle(@enum.EnumName),
            MirTsonArrayPlan array => TsonValuePlanType(array.ElementPlan) + "[]",
            _ => throw new InvalidOperationException("Unsupported validated TSON value plan."),
        };

    private static IReadOnlyList<MirTsonArrayPlan> CollectTsonArrayPlans(MirTsonEncodingPlan plan)
    {
        var arrays = new List<MirTsonArrayPlan>();

        void Visit(MirTsonValuePlan valuePlan)
        {
            if (valuePlan is not MirTsonArrayPlan array || arrays.Contains(array))
            {
                return;
            }

            arrays.Add(array);
            Visit(array.ElementPlan);
        }

        Visit(plan.RootValuePlan);
        if (plan.TablePlan is not null)
        {
            foreach (MirTsonTableColumnPlan column in plan.TablePlan.Columns)
            {
                Visit(column.ElementPlan);
            }
        }
        foreach (MirTsonNominalPlan definition in plan.Definitions)
        {
            IEnumerable<MirTsonValuePlan> values = definition switch
            {
                MirTsonRecordPlan record => record.Fields.Select(field => field.ValuePlan),
                MirTsonEnumPlan @enum => @enum.Cases.SelectMany(@case => @case.Payloads.Select(payload => payload.ValuePlan)),
                _ => [],
            };
            foreach (MirTsonValuePlan value in values)
            {
                Visit(value);
            }
        }

        return arrays;
    }
    private static string MapType(MirType type) => type switch { MirType { Identifier: "number" } => "double", MirType { Identifier: "string" } => "string", MirType { Identifier: "boolean" } => "bool", MirType { Identifier: "void" } => "void", MirClrType clr => "global::" + clr.MetadataName, MirArrayType array => MapType(array.ElementType) + "[]", MirResultType result => $"CopeResult<{MapResultComponentType(result.SuccessType)}, {MapType(result.ErrorType)}>", MirAsyncType async => $"CopeAsync<{MapValueStorageType(async.EventualType)}>", MirIterableType iterable => $"global::System.Collections.Generic.IEnumerable<{MapValueStorageType(iterable.ElementType)}>", MirCallableType callable => CallableDelegateName(callable), MirRecordType record => RecordTypeName(record.RecordTypeId), MirTableType table => TableTypeName(table.TableId), MirTableRowType row => TableRowTypeName(row.RowTypeId), MirColumnType column => $"CopeColumn<{MapType(column.ElementType)}>", MirType named => CSharpNameMangler.Mangle(named.Identifier), _ => throw new InvalidOperationException("Unknown structured MIR type.") };
    private static string MapValueStorageType(MirType type) => type is MirNamedType { Identifier: "void" } ? "CopeUnit" : MapType(type);
    private static string MapResultComponentType(MirType type) => type is MirNamedType { Identifier: "void" } ? "CopeUnit" : MapType(type);

    private static bool ProgramUsesResult(MirProgram program) => EnumerateTypes(program).Any(MirTypeFacts.ContainsResult);

    private static bool ProgramUsesUnwrap(MirProgram program)
        => program.Functions.Any(function => function.Body.Any(StatementUsesUnwrap));

    private static bool ProgramUsesBatch(MirProgram program)
        => program.Functions.Any(function => function.Body.Any(StatementUsesBatch));

    private static bool StatementUsesBatch(MirStatement statement)
        => statement switch
        {
            MirVariableDeclarationStatement declaration => ExpressionUsesBatch(declaration.Initializer),
            MirResourceUsingDeclarationStatement declaration => ExpressionUsesBatch(declaration.Initializer),
            MirExpressionStatement expression => ExpressionUsesBatch(expression.Expression),
            MirReturnStatement { Expression: not null } returned => ExpressionUsesBatch(returned.Expression),
            MirIfStatement conditional => ExpressionUsesBatch(conditional.Condition)
                || conditional.ThenStatements.Any(StatementUsesBatch)
                || (conditional.ElseStatements?.Any(StatementUsesBatch) ?? false),
            MirWhileStatement loop => ExpressionUsesBatch(loop.Condition) || loop.BodyStatements.Any(StatementUsesBatch),
            MirForStatement loop => (loop.Initializer is not null && StatementUsesBatch(loop.Initializer))
                || (loop.Condition is not null && ExpressionUsesBatch(loop.Condition))
                || (loop.Increment is not null && ExpressionUsesBatch(loop.Increment))
                || loop.BodyStatements.Any(StatementUsesBatch),
            _ => false,
        };

    private static bool ExpressionUsesBatch(MirExpression expression)
        => expression switch
        {
            MirBatchExpression => true,
            MirAssignmentExpression assignment => ExpressionUsesBatch(assignment.Expression),
            MirUnaryExpression unary => ExpressionUsesBatch(unary.Operand),
            MirBinaryExpression binary => ExpressionUsesBatch(binary.Left) || ExpressionUsesBatch(binary.Right),
            MirCallExpression call => call.Arguments.Any(ExpressionUsesBatch),
            MirInvokeExpression invoke => ExpressionUsesBatch(invoke.Callee) || invoke.Arguments.Any(ExpressionUsesBatch),
            MirArrayExpression array => array.Elements.Any(ExpressionUsesBatch),
            MirRecordConstructionExpression construction => construction.Initializers.Any(initializer => ExpressionUsesBatch(initializer.Value)),
            MirRecordFieldAccessExpression access => ExpressionUsesBatch(access.Receiver),
            MirRecordWithExpression update => ExpressionUsesBatch(update.Source) || update.Replacements.Any(replacement => ExpressionUsesBatch(replacement.Value)),
            MirEnumValueExpression value => value.Arguments.Any(ExpressionUsesBatch),
            MirMatchExpression match => ExpressionUsesBatch(match.Scrutinee) || match.Arms.Any(arm => ExpressionUsesBatch(arm.Expression)),
            MirResultMatchExpression match => ExpressionUsesBatch(match.Scrutinee) || ExpressionUsesBatch(match.OkExpression) || ExpressionUsesBatch(match.ErrExpression),
            MirIfExpression conditional => ExpressionUsesBatch(conditional.Condition) || ExpressionUsesBatch(conditional.ThenExpression) || ExpressionUsesBatch(conditional.ElseExpression),
            MirOkExpression ok => ExpressionUsesBatch(ok.Payload),
            MirErrExpression err => ExpressionUsesBatch(err.Payload),
            MirUnwrapExpression unwrap => ExpressionUsesBatch(unwrap.Operand),
            MirTryExpression attempt => attempt.Protected.PrefixStatements.Any(StatementUsesBatch)
                || ExpressionUsesBatch(attempt.Protected.ValueExpression)
                || attempt.Handler.PrefixStatements.Any(StatementUsesBatch)
                || ExpressionUsesBatch(attempt.Handler.ValueExpression),
            _ => false,
        };

    private static bool ProgramUsesTsonTransport(MirProgram program)
        => program.Functions.Any(function => function.Body.Any(StatementUsesTsonTransport));

    private static bool ProgramUsesSystemTextJson(MirProgram program)
        => program.Functions.Any(function => function.Body.Any(StatementUsesSystemTextJson));

    private static bool StatementUsesSystemTextJson(MirStatement statement)
        => statement switch
        {
            MirVariableDeclarationStatement declaration => ExpressionUsesSystemTextJson(declaration.Initializer),
            MirResourceUsingDeclarationStatement declaration => ExpressionUsesSystemTextJson(declaration.Initializer),
            MirExpressionStatement expression => ExpressionUsesSystemTextJson(expression.Expression),
            MirReturnStatement { Expression: not null } returned => ExpressionUsesSystemTextJson(returned.Expression),
            MirIfStatement conditional => ExpressionUsesSystemTextJson(conditional.Condition) || conditional.ThenStatements.Any(StatementUsesSystemTextJson) || (conditional.ElseStatements?.Any(StatementUsesSystemTextJson) ?? false),
            MirWhileStatement loop => ExpressionUsesSystemTextJson(loop.Condition) || loop.BodyStatements.Any(StatementUsesSystemTextJson),
            MirForStatement loop => (loop.Initializer is not null && StatementUsesSystemTextJson(loop.Initializer)) || (loop.Condition is not null && ExpressionUsesSystemTextJson(loop.Condition)) || (loop.Increment is not null && ExpressionUsesSystemTextJson(loop.Increment)) || loop.BodyStatements.Any(StatementUsesSystemTextJson),
            _ => false,
        };

    private static bool ExpressionUsesSystemTextJson(MirExpression expression)
        => expression switch
        {
            MirClrInvocationExpression { Member.DeclaringType: "System.Text.Json.JsonSerializer" } => true,
            MirClrInvocationExpression invocation => invocation.Arguments.Any(ExpressionUsesSystemTextJson) || (invocation.Receiver is not null && ExpressionUsesSystemTextJson(invocation.Receiver)),
            MirClrPropertyAccessExpression property => property.Receiver is not null && ExpressionUsesSystemTextJson(property.Receiver),
            MirBinaryExpression binary => ExpressionUsesSystemTextJson(binary.Left) || ExpressionUsesSystemTextJson(binary.Right),
            MirUnaryExpression unary => ExpressionUsesSystemTextJson(unary.Operand),
            MirAssignmentExpression assignment => ExpressionUsesSystemTextJson(assignment.Expression),
            MirCallExpression call => call.Arguments.Any(ExpressionUsesSystemTextJson),
            MirArrayExpression array => array.Elements.Any(ExpressionUsesSystemTextJson),
            MirRecordConstructionExpression construction => construction.Initializers.Any(initializer => ExpressionUsesSystemTextJson(initializer.Value)),
            _ => false,
        };

    private static bool StatementUsesTsonTransport(MirStatement statement)
        => statement switch
        {
            MirVariableDeclarationStatement declaration => ExpressionUsesTsonTransport(declaration.Initializer),
            MirExpressionStatement expression => ExpressionUsesTsonTransport(expression.Expression),
            MirReturnStatement { Expression: not null } returned => ExpressionUsesTsonTransport(returned.Expression),
            MirIfStatement conditional => ExpressionUsesTsonTransport(conditional.Condition)
                || conditional.ThenStatements.Any(StatementUsesTsonTransport)
                || (conditional.ElseStatements?.Any(StatementUsesTsonTransport) ?? false),
            MirWhileStatement loop => ExpressionUsesTsonTransport(loop.Condition) || loop.BodyStatements.Any(StatementUsesTsonTransport),
            MirForStatement loop => (loop.Initializer is not null && StatementUsesTsonTransport(loop.Initializer))
                || (loop.Condition is not null && ExpressionUsesTsonTransport(loop.Condition))
                || (loop.Increment is not null && ExpressionUsesTsonTransport(loop.Increment))
                || loop.BodyStatements.Any(StatementUsesTsonTransport),
            _ => false,
        };

    private static bool ExpressionUsesTsonTransport(MirExpression expression)
        => expression switch
        {
            MirTsonTransportExpression or MirNpmCallExpression => true,
            MirAwaitExpression awaited => ExpressionUsesTsonTransport(awaited.Operand),
            MirAssignmentExpression assignment => ExpressionUsesTsonTransport(assignment.Expression),
            MirUnaryExpression unary => ExpressionUsesTsonTransport(unary.Operand),
            MirBinaryExpression binary => ExpressionUsesTsonTransport(binary.Left) || ExpressionUsesTsonTransport(binary.Right),
            MirCallExpression call => call.Arguments.Any(ExpressionUsesTsonTransport),
            MirRecordConstructionExpression record => record.Initializers.Any(value => ExpressionUsesTsonTransport(value.Value)),
            MirRecordFieldAccessExpression access => ExpressionUsesTsonTransport(access.Receiver),
            MirTsonEncodeExpression encode => ExpressionUsesTsonTransport(encode.Operand),
            _ => false,
        };

    private static bool StatementUsesUnwrap(MirStatement statement)
    {
        return statement switch
        {
            MirVariableDeclarationStatement declaration => ExpressionUsesUnwrap(declaration.Initializer),
            MirExpressionStatement expression => ExpressionUsesUnwrap(expression.Expression),
            MirReturnStatement { Expression: not null } returnStatement => ExpressionUsesUnwrap(returnStatement.Expression),
            MirIfStatement conditional =>
                ExpressionUsesUnwrap(conditional.Condition)
                || conditional.ThenStatements.Any(StatementUsesUnwrap)
                || (conditional.ElseStatements?.Any(StatementUsesUnwrap) ?? false),
            MirWhileStatement loop =>
                ExpressionUsesUnwrap(loop.Condition)
                || loop.BodyStatements.Any(StatementUsesUnwrap),
            MirForStatement loop =>
                (loop.Initializer is not null && StatementUsesUnwrap(loop.Initializer))
                || (loop.Condition is not null && ExpressionUsesUnwrap(loop.Condition))
                || (loop.Increment is not null && ExpressionUsesUnwrap(loop.Increment))
                || loop.BodyStatements.Any(StatementUsesUnwrap),
            _ => false,
        };
    }

    private static bool ExpressionUsesUnwrap(MirExpression expression)
    {
        return expression switch
        {
            MirUnwrapExpression => true,
            MirAssignmentExpression assignment => ExpressionUsesUnwrap(assignment.Expression),
            MirUnaryExpression unary => ExpressionUsesUnwrap(unary.Operand),
            MirBinaryExpression binary => ExpressionUsesUnwrap(binary.Left) || ExpressionUsesUnwrap(binary.Right),
            MirCallExpression call => call.Arguments.Any(ExpressionUsesUnwrap),
            MirInvokeExpression invoke => ExpressionUsesUnwrap(invoke.Callee) || invoke.Arguments.Any(ExpressionUsesUnwrap),
            MirArrayExpression array => array.Elements.Any(ExpressionUsesUnwrap),
            MirRecordConstructionExpression construction => construction.Initializers.Any(initializer => ExpressionUsesUnwrap(initializer.Value)),
            MirRecordFieldAccessExpression access => ExpressionUsesUnwrap(access.Receiver),
            MirRecordWithExpression withExpression => ExpressionUsesUnwrap(withExpression.Source) || withExpression.Replacements.Any(replacement => ExpressionUsesUnwrap(replacement.Value)),
            MirTableColumnAccessExpression access => ExpressionUsesUnwrap(access.Receiver),
            MirTableRowAccessExpression access => ExpressionUsesUnwrap(access.Receiver) || ExpressionUsesUnwrap(access.Index),
            MirColumnElementAccessExpression access => ExpressionUsesUnwrap(access.Receiver) || ExpressionUsesUnwrap(access.Index),
            MirTableRowFieldAccessExpression access => ExpressionUsesUnwrap(access.Receiver),
            MirEnumValueExpression value => value.Arguments.Any(ExpressionUsesUnwrap),
            MirMatchExpression match => ExpressionUsesUnwrap(match.Scrutinee) || match.Arms.Any(arm => ExpressionUsesUnwrap(arm.Expression)),
            MirResultMatchExpression match => ExpressionUsesUnwrap(match.Scrutinee) || ExpressionUsesUnwrap(match.OkExpression) || ExpressionUsesUnwrap(match.ErrExpression),
            MirIfExpression conditional => ExpressionUsesUnwrap(conditional.Condition) || ExpressionUsesUnwrap(conditional.ThenExpression) || ExpressionUsesUnwrap(conditional.ElseExpression),
            MirOkExpression ok => ExpressionUsesUnwrap(ok.Payload),
            MirErrExpression err => ExpressionUsesUnwrap(err.Payload),
            MirPropagateExpression propagation => ExpressionUsesUnwrap(propagation.Operand),
            MirTryExpression tryExpression => ValueBlockUsesUnwrap(tryExpression.Protected) || ValueBlockUsesUnwrap(tryExpression.Handler),
            MirTsonEncodeExpression encode => ExpressionUsesUnwrap(encode.Operand),
            _ => false,
        };
    }

    private static bool ValueBlockUsesUnwrap(MirValueBlock block)
        => block.PrefixStatements.Any(StatementUsesUnwrap)
            || ExpressionUsesUnwrap(block.ValueExpression);
    private static IEnumerable<MirType> EnumerateTypes(MirProgram program)
    {
        foreach (var function in program.Functions) { yield return function.ReturnType; foreach (var parameter in function.Parameters) yield return parameter.Type; foreach (var local in function.Locals) yield return local.Type; }
        foreach (var @enum in program.Enums) foreach (var @case in @enum.Cases) foreach (var field in @case.PayloadFields) yield return field.Type;
        foreach (var record in program.Records) foreach (var field in record.Fields) yield return field.Type;
        foreach (var table in program.Tables)
        {
            foreach (var column in table.Columns)
            {
                yield return column.ElementType;
                foreach (var constant in column.Constants)
                {
                    foreach (var type in EnumerateTableConstantTypes(constant))
                    {
                        yield return type;
                    }
                }
            }
        }
    }

    private static bool ExpressionRequiresStatements(MirExpression expression)
    {
        return expression switch
        {
            MirRecordConstructionExpression => true,
            MirRecordWithExpression => true,
            MirResultMatchExpression => true,
            MirPropagateExpression => true,
            MirUnwrapExpression => true,
            MirTryExpression => true,
            MirAssignmentExpression assignment => ExpressionRequiresStatements(assignment.Expression),
            MirUnaryExpression unary => ExpressionRequiresStatements(unary.Operand),
            MirBinaryExpression binary => ExpressionRequiresStatements(binary.Left) || ExpressionRequiresStatements(binary.Right),
            MirCallExpression call => call.Arguments.Any(ExpressionRequiresStatements),
            MirInvokeExpression invoke => ExpressionRequiresStatements(invoke.Callee) || invoke.Arguments.Any(ExpressionRequiresStatements),
            MirArrayExpression array => array.Elements.Any(ExpressionRequiresStatements),
            MirRecordFieldAccessExpression access => ExpressionRequiresStatements(access.Receiver),
            MirTableColumnAccessExpression access => ExpressionRequiresStatements(access.Receiver),
            MirTableRowAccessExpression access => ExpressionRequiresStatements(access.Receiver) || ExpressionRequiresStatements(access.Index),
            MirColumnElementAccessExpression access => ExpressionRequiresStatements(access.Receiver) || ExpressionRequiresStatements(access.Index),
            MirTableRowFieldAccessExpression access => ExpressionRequiresStatements(access.Receiver),
            MirEnumValueExpression value => value.Arguments.Any(ExpressionRequiresStatements),
            MirMatchExpression match => ExpressionRequiresStatements(match.Scrutinee) || match.Arms.Any(arm => ExpressionRequiresStatements(arm.Expression)),
            MirIfExpression conditional => ExpressionRequiresStatements(conditional.Condition) || ExpressionRequiresStatements(conditional.ThenExpression) || ExpressionRequiresStatements(conditional.ElseExpression),
            MirOkExpression ok => ExpressionRequiresStatements(ok.Payload),
            MirErrExpression err => ExpressionRequiresStatements(err.Payload),
            MirTsonEncodeExpression encode => ExpressionRequiresStatements(encode.Operand),
            _ => false,
        };
    }

    private static string RecordTypeName(MirRecordTypeId id)
        => "__CopeRecord_" + EncodeStableIdentity(id.Value);

    private static string RecordFieldName(MirRecordFieldId id)
        => "__field_" + EncodeStableIdentity(id.Value);

    private static string TableTypeName(MirTableId id)
        => "__CopeTable_" + EncodeStableIdentity(id.Value);

    private static string TableRowTypeName(string rowTypeId)
        => "__CopeTableRow_" + EncodeStableIdentity(rowTypeId);

    private static string TableColumnTypeName(MirTableColumnId id)
        => "__CopeTableColumn_" + EncodeStableIdentity(id.Value);

    private static IEnumerable<MirCallableType> EnumerateCallableTypes(MirType root)
    {
        var pending = new Stack<MirType>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            MirType type = pending.Pop();
            switch (type)
            {
                case MirCallableType callable:
                    yield return callable;
                    foreach (var parameter in callable.Parameters) pending.Push(parameter.Type);
                    pending.Push(callable.ReturnType);
                    break;
                case MirArrayType array:
                    pending.Push(array.ElementType);
                    break;
                case MirResultType result:
                    pending.Push(result.SuccessType);
                    pending.Push(result.ErrorType);
                    break;
                case MirColumnType column:
                    pending.Push(column.ElementType);
                    break;
            }
        }
    }

    private static string CallableDelegateName(MirCallableType callable)
        => "__CopeCallable_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CallableTypeIdentity(callable))));

    private static string CallableTypeIdentity(MirCallableType callable)
        => "(" + string.Join(",", callable.Parameters.Select(parameter => MirTypeIdentity(parameter.Type))) + ")->" + MirTypeIdentity(callable.ReturnType);

    private static string MirTypeIdentity(MirType type) => type switch
    {
        MirArrayType array => "array(" + MirTypeIdentity(array.ElementType) + ")",
        MirResultType result => "result(" + MirTypeIdentity(result.SuccessType) + "," + MirTypeIdentity(result.ErrorType) + ")",
        MirCallableType callable => CallableTypeIdentity(callable),
        MirRecordType record => "record:" + record.RecordTypeId.Value,
        MirTableType table => "table:" + table.TableId.Value,
        MirTableRowType row => "row:" + row.RowTypeId,
        MirColumnType column => "column(" + MirTypeIdentity(column.ElementType) + ")",
        _ => "named:" + type.Identifier,
    };

    private static string TableSingletonName(MirTableId id)
        => "__cope_table_" + EncodeStableIdentity(id.Value);

    private static string TableStorageName(MirTableColumnId id)
        => "_column_" + EncodeStableIdentity(id.Value);

    private static string TableColumnPropertyName(MirTableColumnId id)
        => "__column_" + EncodeStableIdentity(id.Value);

    private static string TableReadMethodName(MirTableColumnId id)
        => "__read_" + EncodeStableIdentity(id.Value);

    private static string TableTsonStorageAccessName(MirTableColumnId id)
        => "__tson_values_" + EncodeStableIdentity(id.Value);

    private static string TableRowFieldName(MirTableColumnId id)
        => "__row_field_" + EncodeStableIdentity(id.Value);

    private static string TableRowFieldName(string fieldId)
        => "__row_field_" + EncodeStableIdentity(fieldId.Replace(".f", string.Empty, StringComparison.Ordinal));

    private static IEnumerable<MirType> EnumerateTableConstantTypes(MirTableConstant constant)
    {
        yield return constant.Type;
        switch (constant)
        {
            case MirTableArrayConstant array:
                foreach (var element in array.Elements)
                {
                    foreach (var type in EnumerateTableConstantTypes(element))
                    {
                        yield return type;
                    }
                }
                break;
            case MirTableRecordConstant record:
                foreach (var field in record.Fields)
                {
                    foreach (var type in EnumerateTableConstantTypes(field.Value))
                    {
                        yield return type;
                    }
                }
                break;
            case MirTableEnumConstant value:
                foreach (var payload in value.Payloads)
                {
                    foreach (var type in EnumerateTableConstantTypes(payload))
                    {
                        yield return type;
                    }
                }
                break;
            case MirTableResultConstant result:
                foreach (var type in EnumerateTableConstantTypes(result.Payload))
                {
                    yield return type;
                }
                break;
        }
    }

    private static string EncodeStableIdentity(string identity)
    {
        var encoded = new global::System.Text.StringBuilder(identity.Length);
        foreach (char character in identity)
        {
            if ((character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9'))
            {
                encoded.Append(character);
            }
            else
            {
                encoded.Append('_');
                encoded.Append(((int)character).ToString("X4", global::System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }
    private static bool ContainsVoidResult(MirType type) => type switch { MirResultType result => result.SuccessType is MirNamedType { Identifier: "void" } || ContainsVoidResult(result.SuccessType) || ContainsVoidResult(result.ErrorType), MirArrayType array => ContainsVoidResult(array.ElementType), _ => false };
    private static List<string> CollectErrorNominalTypes(MirProgram program, IReadOnlySet<string> enumNames)
    {
        var names = new HashSet<string>(StringComparer.Ordinal); foreach (var type in EnumerateTypes(program)) Collect(type, names, enumNames); return names.OrderBy(name => name, StringComparer.Ordinal).ToList();
    }
    private static void Collect(MirType type, ISet<string> names, IReadOnlySet<string> enumNames)
    {
        switch (type) { case MirResultType result: CollectError(result.ErrorType, names, enumNames); Collect(result.SuccessType, names, enumNames); break; case MirArrayType array: Collect(array.ElementType, names, enumNames); break; }
    }
    private static void CollectError(MirType type, ISet<string> names, IReadOnlySet<string> enumNames)
    {
        switch (type) { case MirNamedType named when named.Identifier is not ("number" or "string" or "boolean" or "void" or "error") && !enumNames.Contains(named.Identifier): names.Add(named.Identifier); break; case MirResultType result: CollectError(result.ErrorType, names, enumNames); Collect(result.SuccessType, names, enumNames); break; case MirArrayType array: CollectError(array.ElementType, names, enumNames); break; }
    }
}
