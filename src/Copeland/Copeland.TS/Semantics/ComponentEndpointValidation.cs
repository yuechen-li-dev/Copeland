using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics.Bound;

namespace Copeland.TS.Semantics;

/// <summary>Checks the bounded component endpoint slice against the existing effect authority.</summary>
public static class ComponentEndpointValidation
{
    public static IReadOnlyList<Diagnostic> Validate(
        BoundProgram program,
        IReadOnlyList<BoundFunctionDeclaration> projectFunctions,
        IReadOnlyList<BoundLayoutBinding>? projectLayouts = null,
        IReadOnlySet<string>? immutablePresentationBindings = null)
    {
        var diagnostics = new List<Diagnostic>();
        BoundFunctionDeclaration[] layouts = (projectLayouts ?? program.LayoutBindings).Select(binding =>
            new BoundFunctionDeclaration(binding.RuntimeFunction, new BoundBlockStatement(
                binding.Entries.Select(entry => entry.Component).Concat(binding.Collections.SelectMany(collection => collection.Items))
                    .Select(expression => (BoundStatement)new BoundExpressionStatement(expression)).ToArray()))).ToArray();
        foreach (BoundFunctionDeclaration component in program.Functions.Where(function => function.Symbol.UsesComponentEndpoints))
        {
            BoundComponentStateDeclaration[] states = component.Body.Statements.OfType<BoundComponentStateDeclaration>().ToArray();
            if (states.Length != 1 || component.Symbol.IsGeneric || component.Symbol.IsAsync || component.Symbol.IsGenerator)
            {
                diagnostics.Add(new Diagnostic("COPE-COMPONENT-ON-0003",
                    "Component endpoints require one top-level state declaration in an ordinary closed component function.", 0, 1));
                continue;
            }
            BoundComponentStateDeclaration state = states[0];
            if (!IsImmutableState(state.State.Type, []))
            {
                diagnostics.Add(new Diagnostic("COPE-COMPONENT-ON-0009",
                    "Component state must be an immutable data snapshot. Freeze local construction arrays and store records, enums, or immutable arrays; keep mutable storage and host handles outside state.", 0, 1));
            }
            BoundComponentEventHandler[] handlers = component.Body.Statements.OfType<BoundComponentEventHandler>().ToArray();
            var initializer = new BoundFunctionDeclaration(
                new FunctionSymbol(component.Symbol.Name + " initializer", component.Symbol.Parameters, state.State.Type,
                    stableIdentity: component.Symbol.StableIdentity + "::initial"),
                new BoundBlockStatement([new BoundReturnStatement(state.Initializer)]));
            string? initialFailure = FunctionEffectClassifier.QualifyTransition(initializer, projectFunctions);
            if (initialFailure is not null)
            {
                diagnostics.Add(new Diagnostic("COPE-COMPONENT-ON-0004",
                    "Initialize component state from inputs and qualified pure helpers. " + initialFailure, 0, 1));
            }
            foreach (BoundComponentEventHandler handler in handlers)
            {
                foreach (BoundComponentEffect effect in handler.Effects)
                {
                    if (effect.Phase == ComponentCompletionPhase.AttachmentsSettled)
                    {
                        diagnostics.Add(new Diagnostic("COPE-COMPONENT-ON-0005",
                            "AttachmentsSettled requires declared attachment acknowledgements. Use StateCommitted or PresentationCommitted for this adapter.", 0, 1));
                    }
                    IEnumerable<BoundExpression> arguments = effect.Invocation.Arguments;
                    if (effect.Completion is not null)
                    {
                        arguments = arguments.Concat(effect.Completion.Arguments);
                    }
                    foreach (BoundExpression argument in arguments)
                    {
                        var captureParameters = new[] { new ParameterSymbol(state.State.Name, state.State.Type) }
                            .Concat(handler.Parameters).ToList();
                        if (effect.Completion?.ResultParameter is { } resultParameter)
                        {
                            captureParameters.Add(resultParameter);
                        }
                        var capture = new BoundFunctionDeclaration(
                            new FunctionSymbol(component.Symbol.Name + " effect capture", captureParameters, argument.Type,
                                stableIdentity: effect.StableIdentity + "::capture"),
                            new BoundBlockStatement([new BoundReturnStatement(argument)]));
                        string? captureFailure = FunctionEffectClassifier.QualifyTransition(capture, projectFunctions);
                        if (captureFailure is not null)
                        {
                            diagnostics.Add(new Diagnostic("COPE-COMPONENT-EFFECT-0107",
                                "Effect arguments and completion arguments must be pure snapshots of state, event payload, and result. " + captureFailure, 0, 1));
                        }
                    }
                }
                if (handler.Parameters.Any(parameter => parameter.Name == state.State.Name))
                {
                    diagnostics.Add(new Diagnostic("COPE-COMPONENT-ON-0006",
                        "Rename the event payload parameter so it does not shadow the component state binding.", 0, 1));
                    continue;
                }
                var transition = new BoundFunctionDeclaration(
                    new FunctionSymbol(component.Symbol.Name + ".on." + handler.Name,
                        new[] { new ParameterSymbol(state.State.Name, state.State.Type) }.Concat(handler.Parameters).ToArray(),
                        state.State.Type, stableIdentity: component.Symbol.StableIdentity + "::on::" + handler.Name),
                    new BoundBlockStatement([new BoundReturnStatement(handler.NextState)]));
                string? failure = FunctionEffectClassifier.QualifyTransition(transition, projectFunctions);
                if (failure is not null)
                {
                    diagnostics.Add(new Diagnostic("COPE-COMPONENT-ON-0007",
                        "Handler '" + handler.Name + "' must use current state, typed payload, and qualified pure helpers. Pass render-local values as event payload; put external work in declared effects. " + failure, 0, 1));
                }
            }
            var endpointCallbacks = new Dictionary<string, BoundExpression>(StringComparer.Ordinal);
            var endpointDeclarations = new List<BoundFunctionDeclaration>();
            foreach (BoundComponentEventHandler handler in handlers)
            {
                var endpoint = new FunctionSymbol(component.Symbol.Name + " endpoint " + handler.Name, handler.Parameters, PrimitiveTypeSymbol.Void,
                    stableIdentity: component.Symbol.StableIdentity + "::endpoint::" + handler.Name);
                // Invoking an endpoint during presentation is an external state write.
                // Passing or explicitly capturing the capability remains pure construction.
                endpointDeclarations.Add(new BoundFunctionDeclaration(endpoint,
                    new BoundBlockStatement([state])));
                endpointCallbacks.Add(handler.Endpoint!.Name, new BoundFunctionReferenceExpression(endpoint));
            }
            var present = new BoundFunctionDeclaration(
                new FunctionSymbol(component.Symbol.Name + " presentation",
                    component.Symbol.Parameters.Concat([new ParameterSymbol(state.State.Name, state.State.Type)])
                        .Concat(handlers.Select(handler => new ParameterSymbol(handler.Endpoint!.Name, handler.Endpoint.Type))).ToArray(),
                    ReactNodeTypeSymbol.Instance, stableIdentity: component.Symbol.StableIdentity + "::present"),
                new BoundBlockStatement(component.Body.Statements.Where(statement => statement is not BoundComponentStateDeclaration and not BoundComponentEventHandler).ToArray()));
            string? presentFailure = FunctionEffectClassifier.QualifyTransition(present,
                projectFunctions.Concat(layouts).Concat(endpointDeclarations).ToArray(), presentation: true, initialCallbacks: endpointCallbacks,
                immutablePresentationBindings: immutablePresentationBindings ?? program.ModuleStreams.Select(stream => stream.Storage.Name).ToHashSet(StringComparer.Ordinal));
            if (presentFailure is not null)
            {
                diagnostics.Add(new Diagnostic("COPE-COMPONENT-ON-0008",
                    "Presentation must construct nodes without dispatching inputs or performing external work. Move the action to an explicit callback. " + presentFailure, 0, 1));
            }
        }
        return diagnostics;
    }

    private static bool IsImmutableState(TypeSymbol type, HashSet<TypeSymbol> visiting)
    {
        if (!visiting.Add(type))
        {
            return true;
        }
        return type switch
        {
            PrimitiveTypeSymbol primitive => primitive != PrimitiveTypeSymbol.Void && primitive != PrimitiveTypeSymbol.Error,
            ArrayTypeSymbol array => IsImmutableState(array.ElementType, visiting),
            TableTypeSymbol table => table.Columns.All(column => IsImmutableState(column.Type, visiting)),
            RecordTypeSymbol record => record.Fields.All(field => IsImmutableState(field.Type, visiting)),
            EnumTypeSymbol enumeration => enumeration.Cases.All(@case => @case.PayloadFields.All(field => IsImmutableState(field.Type, visiting))),
            ResultTypeSymbol result => IsImmutableState(result.SuccessType, visiting) && IsImmutableState(result.ErrorType, visiting),
            _ => false,
        };
    }
}
