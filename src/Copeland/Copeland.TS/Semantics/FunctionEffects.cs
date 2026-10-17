using Copeland.TS.Semantics.Bound;

namespace Copeland.TS.Semantics;

public enum FunctionEffect
{
    LocalMutation,
    ReadsRuntimeState,
    WritesRuntimeState,
    IO,
    HostInterop,
    Suspension,
    UnknownCall,
}

public enum FunctionStaticSafety
{
    StaticSafe,
    RuntimeOnly,
}

public sealed record FunctionEffectSummary(
    FunctionSymbol Function,
    FunctionStaticSafety StaticSafety,
    IReadOnlyList<FunctionEffect> SafeEffects,
    FunctionEffect? RuntimeEffect,
    string? Reason,
    IReadOnlyList<string> Provenance)
{
    public bool IsStaticSafe => StaticSafety == FunctionStaticSafety.StaticSafe;
}

/// <summary>
/// Backend-neutral, fail-closed ordinary-function effect analysis. Recursive
/// call components converge by fixed point; recursion alone is not an effect.
/// The bound-tree walk is explicit so this compiler path remains NativeAOT-friendly.
/// </summary>
public static class FunctionEffectClassifier
{
    /// <summary>
    /// Qualifies one closed transition, resolving higher-order helper arguments
    /// at their actual call sites. Unresolved callbacks remain runtime-only.
    /// This uses the same bound-tree inspector as ordinary effect classification.
    /// </summary>
    public static string? QualifyTransition(
        BoundFunctionDeclaration transition,
        IReadOnlyList<BoundFunctionDeclaration> functions,
        bool presentation = false,
        IReadOnlyDictionary<string, BoundExpression>? initialCallbacks = null,
        IReadOnlySet<string>? immutablePresentationBindings = null)
    {
        var declarations = functions.Append(transition).ToDictionary(function => function.Symbol);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Qualify(transition, initialCallbacks ?? new Dictionary<string, BoundExpression>(StringComparer.Ordinal));

        string? Qualify(BoundFunctionDeclaration declaration, IReadOnlyDictionary<string, BoundExpression> callbacks)
        {
            string key = declaration.Symbol.StableIdentity + "|" + string.Join("|", callbacks.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + CallbackIdentity(pair.Value)));
            if (!visited.Add(key))
            {
                return null;
            }
            if (visited.Count > 512)
            {
                return "transition helper qualification exceeded 512 closed call contexts";
            }
            DirectEffectInfo info = new BoundEffectInspector(declaration, declarations, callbacks, presentation, immutablePresentationBindings).Inspect();
            if (info.RuntimeEffect is not null)
            {
                return declaration.Symbol.Name + ": " + info.Reason;
            }
            foreach (BoundCallExpression call in info.CallSites)
            {
                if (!declarations.TryGetValue(call.Function, out BoundFunctionDeclaration? callee))
                {
                    return "unclassified helper '" + call.Function.Name + "'";
                }
                var arguments = new Dictionary<string, BoundExpression>(StringComparer.Ordinal);
                for (int index = 0; index < callee.Symbol.Parameters.Count; index += 1)
                {
                    BoundExpression argument = ResolveCallback(call.Arguments[index], callbacks);
                    if (callee.Symbol.Parameters[index].Type is CallableTypeSymbol)
                    {
                        if (argument is not BoundFunctionReferenceExpression and not BoundCallableConstructionExpression)
                        {
                            return "helper '" + callee.Symbol.Name + "' needs a known pure callable for '" + callee.Symbol.Parameters[index].Name + "'";
                        }
                    }
                    arguments.Add(callee.Symbol.Parameters[index].Name, argument);
                }
                string? failure = Qualify(callee, arguments);
                if (failure is not null)
                {
                    return declaration.Symbol.Name + " -> " + failure;
                }
            }
            return null;
        }
    }

    private static string CallbackIdentity(BoundExpression expression)
        => expression switch
        {
            BoundFunctionReferenceExpression reference => reference.Function.StableIdentity,
            BoundCallableConstructionExpression closure => closure.Code.StableIdentity + "(" + string.Join(",", closure.Captures.Select(CallbackIdentity)) + ")",
            BoundVariableExpression variable => variable.Variable.Name,
            BoundRecordConstructionExpression record => record.RecordType.Name + "{" + string.Join(",", record.Initializers.Select(field => field.Field.Name + "=" + CallbackIdentity(field.Value))) + "}",
            BoundRecordFieldAccessExpression field => CallbackIdentity(field.Receiver) + "." + field.Field.Name,
            BoundRecordWithExpression update => CallbackIdentity(update.Source) + " with {" + string.Join(",", update.Replacements.Select(field => field.Field.Name + "=" + CallbackIdentity(field.Value))) + "}",
            _ => expression.Type.Name,
        };

    private static BoundExpression ResolveCallback(
        BoundExpression expression,
        IReadOnlyDictionary<string, BoundExpression> callbacks,
        IReadOnlyDictionary<VariableSymbol, BoundExpression>? locals = null)
    {
        if (expression is BoundVariableExpression variable)
        {
            if (locals is not null && locals.TryGetValue(variable.Variable, out BoundExpression? local))
            {
                return local;
            }
            if (callbacks.TryGetValue(variable.Variable.Name, out BoundExpression? bound))
            {
                return bound;
            }
        }
        if (expression is BoundCallableConstructionExpression closure)
        {
            return new BoundCallableConstructionExpression(closure.Code,
                closure.Captures.Select(capture => ResolveCallback(capture, callbacks, locals)).ToArray(), closure.CallableType);
        }
        if (expression is BoundRecordConstructionExpression record)
        {
            return new BoundRecordConstructionExpression(record.RecordType,
                record.Initializers.Select(field => new BoundRecordFieldInitializer(field.Field, ResolveCallback(field.Value, callbacks, locals))).ToArray());
        }
        if (expression is BoundRecordFieldAccessExpression access)
        {
            BoundExpression receiver = ResolveCallback(access.Receiver, callbacks, locals);
            if (receiver is BoundRecordConstructionExpression construction)
            {
                BoundRecordFieldInitializer? field = construction.Initializers.FirstOrDefault(field => field.Field.Id == access.Field.Id);
                if (field is not null)
                {
                    return field.Value;
                }
            }
            return new BoundRecordFieldAccessExpression(receiver, access.RecordType, access.Field);
        }
        return expression;
    }

    public static IReadOnlyDictionary<FunctionSymbol, FunctionEffectSummary> Classify(IReadOnlyList<BoundFunctionDeclaration> functions)
    {
        var declarations = functions.ToDictionary(function => function.Symbol);
        var direct = functions.ToDictionary(
            function => function.Symbol,
            function => new BoundEffectInspector(function, declarations).Inspect());
        var summaries = new Dictionary<FunctionSymbol, FunctionEffectSummary>();

        foreach (BoundFunctionDeclaration declaration in functions.OrderBy(function => function.Symbol.StableIdentity, StringComparer.Ordinal))
        {
            DirectEffectInfo info = direct[declaration.Symbol];
            if (info.RuntimeEffect is not null)
            {
                summaries[declaration.Symbol] = CreateRuntimeSummary(
                    declaration.Symbol,
                    info,
                    [declaration.Symbol.Name, info.Reason!]);
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (BoundFunctionDeclaration declaration in functions.OrderBy(function => function.Symbol.StableIdentity, StringComparer.Ordinal))
            {
                if (summaries.ContainsKey(declaration.Symbol))
                {
                    continue;
                }

                DirectEffectInfo info = direct[declaration.Symbol];
                FunctionSymbol? runtimeCallee = info.Calls
                    .Where(summaries.ContainsKey)
                    .OrderBy(function => function.StableIdentity, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (runtimeCallee is null)
                {
                    continue;
                }

                FunctionEffectSummary calleeSummary = summaries[runtimeCallee];
                summaries[declaration.Symbol] = new FunctionEffectSummary(
                    declaration.Symbol,
                    FunctionStaticSafety.RuntimeOnly,
                    info.SafeEffects.OrderBy(effect => effect).ToArray(),
                    calleeSummary.RuntimeEffect,
                    $"calls runtime-only function '{runtimeCallee.Name}'",
                    [declaration.Symbol.Name, .. calleeSummary.Provenance]);
                changed = true;
            }
        }
        while (changed);

        var transitiveSafeEffects = functions
            .Where(declaration => !summaries.ContainsKey(declaration.Symbol))
            .ToDictionary(
                declaration => declaration.Symbol,
                declaration => new HashSet<FunctionEffect>(direct[declaration.Symbol].SafeEffects));
        do
        {
            changed = false;
            foreach ((FunctionSymbol function, HashSet<FunctionEffect> effects) in transitiveSafeEffects)
            {
                foreach (FunctionSymbol callee in direct[function].Calls)
                {
                    if (transitiveSafeEffects.TryGetValue(callee, out HashSet<FunctionEffect>? calleeEffects)
                        && UnionWithChanged(effects, calleeEffects))
                    {
                        changed = true;
                    }
                }
            }
        }
        while (changed);

        foreach (BoundFunctionDeclaration declaration in functions)
        {
            if (!summaries.ContainsKey(declaration.Symbol))
            {
                summaries[declaration.Symbol] = new FunctionEffectSummary(
                    declaration.Symbol,
                    FunctionStaticSafety.StaticSafe,
                    transitiveSafeEffects[declaration.Symbol].OrderBy(effect => effect).ToArray(),
                    null,
                    null,
                    [declaration.Symbol.Name]);
            }
        }

        return summaries;
    }

    private static bool UnionWithChanged<T>(HashSet<T> target, IEnumerable<T> values)
    {
        int previousCount = target.Count;
        target.UnionWith(values);
        return target.Count != previousCount;
    }

    private static FunctionEffectSummary CreateRuntimeSummary(
        FunctionSymbol function,
        DirectEffectInfo info,
        IReadOnlyList<string> provenance)
        => new(
            function,
            FunctionStaticSafety.RuntimeOnly,
            info.SafeEffects.OrderBy(effect => effect).ToArray(),
            info.RuntimeEffect,
            info.Reason,
            provenance);

    private sealed class BoundEffectInspector
    {
        private readonly BoundFunctionDeclaration _declaration;
        private readonly IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> _declarations;
        private readonly HashSet<string> _locals;
        private readonly HashSet<VariableSymbol> _freshArrays = [];
        private readonly IReadOnlyDictionary<string, BoundExpression> _callbacks;
        private readonly Dictionary<VariableSymbol, BoundExpression> _localBindings = [];
        private readonly bool _presentation;
        private readonly bool _closedCalls;
        private readonly IReadOnlySet<string> _immutablePresentationBindings;
        private readonly DirectEffectInfo _info = new();

        public BoundEffectInspector(
            BoundFunctionDeclaration declaration,
            IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> declarations,
            IReadOnlyDictionary<string, BoundExpression>? callbacks = null,
            bool presentation = false,
            IReadOnlySet<string>? immutablePresentationBindings = null)
        {
            _declaration = declaration;
            _declarations = declarations;
            _callbacks = callbacks is null
                ? new Dictionary<string, BoundExpression>(StringComparer.Ordinal)
                : new Dictionary<string, BoundExpression>(callbacks, StringComparer.Ordinal);
            _presentation = presentation;
            _closedCalls = callbacks is not null;
            _immutablePresentationBindings = immutablePresentationBindings ?? new HashSet<string>(StringComparer.Ordinal);
            _locals = new HashSet<string>(declaration.Symbol.Parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
        }

        public DirectEffectInfo Inspect()
        {
            CollectLocals(_declaration.Body);
            VisitStatement(_declaration.Body);
            if (_declaration.Symbol.IsRemote)
            {
                _info.SetRuntime(FunctionEffect.HostInterop, "remote function execution crosses a host boundary");
            }
            return _info;
        }

        private void CollectLocals(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlockStatement block:
                    foreach (BoundStatement child in block.Statements) CollectLocals(child);
                    break;
                case BoundVariableDeclaration variable:
                    _locals.Add(variable.Variable.Name);
                    if (variable.Variable.IsReadOnly)
                    {
                        _localBindings[variable.Variable] = ResolveCallback(variable.Initializer, _callbacks, _localBindings);
                    }
                    if (variable.Variable.IsReadOnly
                        && (variable.Initializer is BoundMutableArrayConstructionExpression
                            || variable.Initializer is BoundCallExpression { Function.NativeOperation: Copeland.TS.Mir.MirNativeOperation.MutableArrayFilled }))
                    {
                        _freshArrays.Add(variable.Variable);
                    }
                    CollectExpressionLocals(variable.Initializer);
                    break;
                case BoundComponentStateDeclaration state:
                    _locals.Add(state.State.Name);
                    CollectExpressionLocals(state.Initializer);
                    break;
                case BoundResourceUsingDeclaration resource:
                    _locals.Add(resource.Variable.Name);
                    CollectExpressionLocals(resource.Initializer);
                    break;
                case BoundExpressionStatement expression:
                    CollectExpressionLocals(expression.Expression);
                    break;
                case BoundIfStatement conditional:
                    CollectExpressionLocals(conditional.Condition);
                    CollectLocals(conditional.ThenStatement);
                    if (conditional.ElseStatement is not null) CollectLocals(conditional.ElseStatement);
                    break;
                case BoundWhileStatement loop:
                    CollectExpressionLocals(loop.Condition);
                    CollectLocals(loop.Body);
                    break;
                case BoundForStatement loop:
                    if (loop.Initializer is not null) CollectLocals(loop.Initializer);
                    if (loop.Condition is not null) CollectExpressionLocals(loop.Condition);
                    if (loop.Increment is not null) CollectExpressionLocals(loop.Increment);
                    CollectLocals(loop.Body);
                    break;
                case BoundForOfStatement loop:
                    _locals.Add(loop.Variable.Name);
                    CollectExpressionLocals(loop.Iterable);
                    CollectLocals(loop.Body);
                    break;
                case BoundReturnStatement returned when returned.Expression is not null:
                    CollectExpressionLocals(returned.Expression);
                    break;
                case BoundYieldStatement yielded when yielded.Expression is not null:
                    CollectExpressionLocals(yielded.Expression);
                    break;
            }
        }

        private void CollectExpressionLocals(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundBatchExpression batch:
                    _locals.Add(batch.Item.Name);
                    CollectExpressionLocals(batch.Input);
                    foreach (BoundStatement statement in batch.Body.PrefixStatements) CollectLocals(statement);
                    CollectExpressionLocals(batch.Body.ValueExpression);
                    break;
                case BoundMatchExpression match:
                    CollectExpressionLocals(match.Scrutinee);
                    foreach (BoundMatchArm arm in match.Arms)
                    {
                        foreach (VariableSymbol variable in arm.PayloadVariables) _locals.Add(variable.Name);
                        CollectExpressionLocals(arm.Expression);
                    }
                    break;
                case BoundResultMatchExpression match:
                    _locals.Add(match.OkVariable.Name);
                    _locals.Add(match.ErrVariable.Name);
                    CollectExpressionLocals(match.Scrutinee);
                    CollectExpressionLocals(match.OkExpression);
                    CollectExpressionLocals(match.ErrExpression);
                    break;
                case BoundTryExceptExpression attempt:
                    _locals.Add(attempt.HandlerBinding.Name);
                    foreach (BoundStatement statement in attempt.Protected.PrefixStatements) CollectLocals(statement);
                    CollectExpressionLocals(attempt.Protected.ValueExpression);
                    foreach (BoundStatement statement in attempt.Handler.PrefixStatements) CollectLocals(statement);
                    CollectExpressionLocals(attempt.Handler.ValueExpression);
                    break;
            }
        }

        private void VisitStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlockStatement block:
                    foreach (BoundStatement child in block.Statements) VisitStatement(child);
                    return;
                case BoundVariableDeclaration variable:
                    VisitExpression(variable.Initializer);
                    return;
                case BoundComponentStateDeclaration state:
                    _info.SetRuntime(FunctionEffect.WritesRuntimeState, "component state is runtime-owned");
                    VisitExpression(state.Initializer);
                    return;
                case BoundComponentEventHandler:
                    _info.SetRuntime(FunctionEffect.WritesRuntimeState, "component event handling is runtime-owned");
                    return;
                case BoundLocalPresentationDeclaration:
                    if (!_presentation) _info.SetRuntime(FunctionEffect.WritesRuntimeState, "component presentation is runtime-owned");
                    return;
                case BoundResourceUsingDeclaration resource:
                    _info.SetRuntime(FunctionEffect.HostInterop, "resource lifetime depends on host state");
                    VisitExpression(resource.Initializer);
                    return;
                case BoundCSharpBlockStatement:
                    _info.SetRuntime(FunctionEffect.HostInterop, "inline C# block has unclassified host semantics");
                    return;
                case BoundExpressionStatement expression:
                    VisitExpression(expression.Expression);
                    return;
                case BoundIfStatement conditional:
                    VisitExpression(conditional.Condition);
                    VisitStatement(conditional.ThenStatement);
                    if (conditional.ElseStatement is not null) VisitStatement(conditional.ElseStatement);
                    return;
                case BoundWhileStatement loop:
                    VisitExpression(loop.Condition);
                    VisitStatement(loop.Body);
                    return;
                case BoundForStatement loop:
                    if (loop.Initializer is not null) VisitStatement(loop.Initializer);
                    if (loop.Condition is not null) VisitExpression(loop.Condition);
                    if (loop.Increment is not null) VisitExpression(loop.Increment);
                    VisitStatement(loop.Body);
                    return;
                case BoundForOfStatement loop:
                    VisitExpression(loop.Iterable);
                    VisitStatement(loop.Body);
                    return;
                case BoundReturnStatement returned:
                    if (returned.Expression is not null) VisitExpression(returned.Expression);
                    return;
                case BoundYieldStatement yielded:
                    if (yielded.Expression is not null) VisitExpression(yielded.Expression);
                    return;
                case BoundBreakStatement or BoundContinueStatement:
                    return;
                default:
                    _info.SetRuntime(FunctionEffect.UnknownCall, "unclassified bound statement");
                    return;
            }
        }

        private void VisitExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundLiteralExpression or BoundUnitExpression or BoundErrorExpression or BoundTableReferenceExpression:
                    return;
                case BoundStaticExpression:
                    // A valid static expression is gone before MIR and therefore
                    // contributes no runtime effect to its containing function.
                    // Its own eligibility is checked by the post-static pass.
                    return;
                case BoundNpmComponentValueExpression or BoundNpmComponentMemberExpression:
                    if (!_presentation) _info.SetRuntime(FunctionEffect.WritesRuntimeState, "renderer operation observes or changes runtime state");
                    return;
                case BoundVariableExpression variable:
                    if (!_locals.Contains(variable.Variable.Name)
                        && !(_presentation && variable.Variable.IsReadOnly && _immutablePresentationBindings.Contains(variable.Variable.Name)))
                    {
                        _info.SetRuntime(FunctionEffect.ReadsRuntimeState, $"reads runtime binding '{variable.Variable.Name}'");
                    }
                    return;
                case BoundAssignmentExpression assignment:
                    if (_locals.Contains(assignment.Variable.Name)) _info.SafeEffects.Add(FunctionEffect.LocalMutation);
                    else _info.SetRuntime(FunctionEffect.WritesRuntimeState, $"writes runtime binding '{assignment.Variable.Name}'");
                    VisitExpression(assignment.Expression);
                    return;
                case BoundUnaryExpression unary:
                    VisitExpression(unary.Operand);
                    return;
                case BoundAwaitExpression awaited:
                    _info.SetRuntime(FunctionEffect.Suspension, "await depends on runtime suspension");
                    VisitExpression(awaited.Operand);
                    return;
                case BoundBinaryExpression binary:
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    return;
                case BoundNumericConversionExpression conversion:
                    VisitExpression(conversion.Operand);
                    return;
                case BoundCallExpression call:
                    if (_presentation && call.Function.UsesComponentEndpoints)
                    {
                        // Stateful React functions construct inert host elements.
                        // Their initializer/transitions are qualified separately;
                        // calling the constructor does not acquire a live frame.
                        VisitExpressions(call.Arguments);
                        return;
                    }
                    if (call.Function.NativeOperation is not null)
                    {
                        VisitExpressions(call.Arguments);
                        return;
                    }
                    if (_declarations.ContainsKey(call.Function)) _info.Calls.Add(call.Function);
                    else _info.SetRuntime(FunctionEffect.UnknownCall, $"call to unclassified function '{call.Function.Name}'");
                    _info.CallSites.Add(new BoundCallExpression(call.Function, call.Arguments.Select(argument => ResolveCallback(argument, _callbacks, _localBindings)).ToArray()));
                    VisitExpressions(call.Arguments);
                    return;
                case BoundNpmCallExpression npm:
                    _info.SetRuntime(FunctionEffect.IO, "npm call crosses the runtime package boundary");
                    VisitExpressions(npm.Arguments);
                    return;
                case BoundNpmDirectCallExpression npm:
                    _info.SetRuntime(FunctionEffect.IO, "npm call crosses the runtime package boundary");
                    VisitExpressions(npm.Arguments);
                    return;
                case BoundReactElementExpression react:
                    if (!_presentation) _info.SetRuntime(FunctionEffect.WritesRuntimeState, "renderer operation observes or changes runtime state");
                    VisitExpression(react.ElementType);
                    foreach (BoundReactProperty property in react.Properties) VisitExpression(property.Value);
                    VisitExpressions(react.Children);
                    return;
                case BoundTextDocumentExpression document:
                    foreach (BoundTextValueSlot slot in document.Slots) VisitExpression(slot.Expression);
                    return;
                case BoundForeignComponentExpression foreign:
                    if (!_presentation) _info.SetRuntime(FunctionEffect.WritesRuntimeState, "renderer operation observes or changes runtime state");
                    VisitExpression(foreign.Payload);
                    return;
                case BoundReactRootRenderExpression render:
                    _info.SetRuntime(FunctionEffect.WritesRuntimeState, "renderer operation observes or changes runtime state");
                    VisitExpression(render.Root);
                    VisitExpression(render.Node);
                    return;
                case BoundReactRootUnmountExpression unmount:
                    _info.SetRuntime(FunctionEffect.WritesRuntimeState, "renderer operation observes or changes runtime state");
                    VisitExpression(unmount.Root);
                    return;
                case BoundJavaScriptHostCallExpression host:
                    bool viewportProjection = _presentation
                        && host.Function.ModuleSpecifier == "@copeland/browser-v1"
                        && host.Function.ExportName is "presentViewport" or "ownChild";
                    if (!viewportProjection) _info.SetRuntime(FunctionEffect.HostInterop, "JavaScript host call crosses the language boundary");
                    VisitExpressions(host.Arguments);
                    return;
                case BoundClrInvocationExpression clr:
                    _info.SetRuntime(FunctionEffect.HostInterop, "CLR member access crosses the language boundary");
                    if (clr.Receiver is not null) VisitExpression(clr.Receiver);
                    VisitExpressions(clr.Arguments);
                    return;
                case BoundClrPropertyAccessExpression clr:
                    _info.SetRuntime(FunctionEffect.HostInterop, "CLR member access crosses the language boundary");
                    if (clr.Receiver is not null) VisitExpression(clr.Receiver);
                    return;
                case BoundFunctionReferenceExpression:
                    return;
                case BoundCallableConstructionExpression callable:
                    VisitExpressions(callable.Captures);
                    return;
                case BoundInvokeExpression invoke:
                    if (!_closedCalls)
                    {
                        _info.SetRuntime(FunctionEffect.UnknownCall, "indirect callable invocation cannot be classified without a closed call context");
                        VisitExpression(invoke.Callee);
                        VisitExpressions(invoke.Arguments);
                        return;
                    }
                    BoundExpression callee = ResolveCallback(invoke.Callee, _callbacks, _localBindings);
                    if (callee is BoundFunctionReferenceExpression reference)
                    {
                        if (_declarations.ContainsKey(reference.Function)) _info.Calls.Add(reference.Function);
                        else _info.SetRuntime(FunctionEffect.UnknownCall, "call to unclassified callable code");
                        _info.CallSites.Add(new BoundCallExpression(reference.Function, invoke.Arguments.Select(argument => ResolveCallback(argument, _callbacks, _localBindings)).ToArray()));
                    }
                    else if (callee is BoundCallableConstructionExpression closure)
                    {
                        if (_declarations.ContainsKey(closure.Code)) _info.Calls.Add(closure.Code);
                        else _info.SetRuntime(FunctionEffect.UnknownCall, "call to unclassified closure code");
                        _info.CallSites.Add(new BoundCallExpression(closure.Code, closure.Captures.Concat(invoke.Arguments).Select(argument => ResolveCallback(argument, _callbacks, _localBindings)).ToArray()));
                    }
                    else
                    {
                        _info.SetRuntime(FunctionEffect.UnknownCall, "indirect callable invocation cannot be classified");
                    }
                    VisitExpression(invoke.Callee);
                    VisitExpressions(invoke.Arguments);
                    return;
                case BoundEnumValueExpression value:
                    VisitExpressions(value.Arguments);
                    return;
                case BoundPropagateExpression propagation:
                    VisitExpression(propagation.Operand);
                    return;
                case BoundUnwrapExpression unwrap:
                    VisitExpression(unwrap.Operand);
                    return;
                case BoundBatchExpression batch:
                    VisitExpression(batch.Input);
                    _locals.Add(batch.Item.Name);
                    VisitValueBlock(batch.Body);
                    return;
                case BoundTryExceptExpression attempt:
                    _locals.Add(attempt.HandlerBinding.Name);
                    VisitValueBlock(attempt.Protected);
                    VisitValueBlock(attempt.Handler);
                    return;
                case BoundOkExpression ok:
                    VisitExpression(ok.Payload);
                    return;
                case BoundErrExpression err:
                    VisitExpression(err.Payload);
                    return;
                case BoundIfExpression conditional:
                    VisitExpression(conditional.Condition);
                    VisitExpression(conditional.ThenExpression);
                    VisitExpression(conditional.ElseExpression);
                    return;
                case BoundTsonEncodeExpression encode:
                    VisitExpression(encode.Operand);
                    return;
                case BoundTsonTransportExpression transport:
                    _info.SetRuntime(FunctionEffect.IO, "TSON transport performs runtime I/O");
                    VisitExpression(transport.Operation);
                    VisitExpression(transport.Request);
                    return;
                case BoundMatchExpression match:
                    VisitExpression(match.Scrutinee);
                    foreach (BoundMatchArm arm in match.Arms)
                    {
                        foreach (VariableSymbol variable in arm.PayloadVariables)
                        {
                            _locals.Add(variable.Name);
                        }
                        VisitExpression(arm.Expression);
                    }
                    return;
                case BoundResultMatchExpression match:
                    VisitExpression(match.Scrutinee);
                    _locals.Add(match.OkVariable.Name);
                    _locals.Add(match.ErrVariable.Name);
                    VisitExpression(match.OkExpression);
                    VisitExpression(match.ErrExpression);
                    return;
                case BoundArrayExpression array:
                    VisitExpressions(array.Elements);
                    return;
                case BoundArrayLengthExpression length:
                    VisitExpression(length.Receiver);
                    return;
                case BoundArrayElementAccessExpression access:
                    VisitExpression(access.Receiver);
                    VisitExpression(access.Index);
                    return;
                case BoundArrayIterableExpression iterable:
                    VisitExpression(iterable.Receiver);
                    return;
                case BoundMutableArrayConstructionExpression construction:
                    VisitExpression(construction.Length);
                    return;
                case BoundMutableArrayLengthExpression length:
                    VisitExpression(length.Receiver);
                    return;
                case BoundMutableArrayElementAccessExpression access:
                    VisitExpression(access.Receiver);
                    VisitExpression(access.Index);
                    return;
                case BoundMutableArrayElementAssignmentExpression assignment:
                    if (assignment.Receiver is BoundVariableExpression receiver && _freshArrays.Contains(receiver.Variable))
                    {
                        _info.SafeEffects.Add(FunctionEffect.LocalMutation);
                    }
                    else
                    {
                        _info.SetRuntime(FunctionEffect.WritesRuntimeState, "mutable-array write through input or alias; build a fresh local array and return its frozen value");
                    }
                    VisitExpression(assignment.Receiver);
                    VisitExpression(assignment.Index);
                    VisitExpression(assignment.Value);
                    return;
                case BoundMutableArrayIterableExpression iterable:
                    VisitExpression(iterable.Receiver);
                    return;
                case BoundMutableArrayFreezeExpression freeze:
                    VisitExpression(freeze.Receiver);
                    return;
                case BoundRecordConstructionExpression record:
                    foreach (BoundRecordFieldInitializer initializer in record.Initializers) VisitExpression(initializer.Value);
                    return;
                case BoundRecordFieldAccessExpression access:
                    VisitExpression(access.Receiver);
                    return;
                case BoundRequirementFieldAccessExpression access:
                    VisitExpression(access.Receiver);
                    return;
                case BoundTableColumnAccessExpression access:
                    VisitExpression(access.Receiver);
                    return;
                case BoundTableRowAccessExpression access:
                    VisitExpression(access.Receiver);
                    VisitExpression(access.Index);
                    return;
                case BoundColumnElementAccessExpression access:
                    VisitExpression(access.Receiver);
                    VisitExpression(access.Index);
                    return;
                case BoundTableRowFieldAccessExpression access:
                    VisitExpression(access.Receiver);
                    return;
                case BoundTableRowsExpression rows:
                    VisitExpression(rows.Table);
                    return;
                case BoundTableWhereExpression where:
                    VisitExpression(where.Source);
                    VisitExpressions(where.Predicates);
                    return;
                case BoundTableSelectExpression select:
                    VisitExpression(select.Source);
                    VisitExpression(select.Projector);
                    return;
                case BoundTableSummaryExpression summary:
                    VisitExpression(summary.Source);
                    return;
                case BoundTableRecalculationExpression recalculation:
                    VisitExpressions(recalculation.Inputs.Select(input => input.Value));
                    return;
                case BoundTableAggregateExpression aggregate:
                    VisitExpression(aggregate.Receiver);
                    return;
                case BoundTableWithRowExpression rowUpdate:
                    foreach (BoundExpression child in rowUpdate.Children) VisitExpression(child);
                    return;
                case BoundTableWithExpression update:
                    VisitExpression(update.Source);
                    foreach (BoundTableColumnReplacement replacement in update.Replacements) VisitExpression(replacement.Value);
                    return;
                case BoundRecordWithExpression update:
                    VisitExpression(update.Source);
                    foreach (BoundRecordFieldInitializer replacement in update.Replacements) VisitExpression(replacement.Value);
                    return;
                default:
                    _info.SetRuntime(FunctionEffect.UnknownCall, "unclassified bound operation");
                    return;
            }
        }

        private void VisitValueBlock(BoundValueBlock block)
        {
            foreach (BoundStatement statement in block.PrefixStatements) VisitStatement(statement);
            VisitExpression(block.ValueExpression);
        }

        private void VisitExpressions(IEnumerable<BoundExpression> expressions)
        {
            foreach (BoundExpression expression in expressions) VisitExpression(expression);
        }
    }

    private sealed class DirectEffectInfo
    {
        public HashSet<FunctionSymbol> Calls { get; } = [];
        public List<BoundCallExpression> CallSites { get; } = [];
        public HashSet<FunctionEffect> SafeEffects { get; } = [];
        public FunctionEffect? RuntimeEffect { get; private set; }
        public string? Reason { get; private set; }

        public void SetRuntime(FunctionEffect effect, string reason)
        {
            if (RuntimeEffect is null)
            {
                RuntimeEffect = effect;
                Reason = reason;
            }
        }
    }
}
