using System.Text;
using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;

namespace Copeland.Cli;

/// <summary>
/// Emits the executable, compiler-owned browser frame registration module.
/// This is intentionally separate from attachments.json: plans remain data
/// transport while transitions remain ordinary generated JavaScript.
/// </summary>
internal static class ComponentFrameArtifactEmitter
{
    public const string ArtifactFileName = "component-frames.js";

    public static string Emit(CopelandProjectCompilation compilation)
    {
        var frames = new List<BrowserFrame>();
        foreach (CopelandProjectModuleCompilation module in compilation.Modules.OrderBy(module => module.LogicalPath, StringComparer.Ordinal))
        {
            BoundProgram program = module.BoundCompilation!.Program;
            foreach (BoundComponentInstance instance in program.ComponentInstances.OrderBy(instance => instance.StableIdentity, StringComparer.Ordinal))
            {
                if (instance.Definition.State is not BoundComponentStateModel state)
                {
                    continue;
                }

                HostAttachmentMir[] attachments = program.HostAttachments
                    .Where(attachment => attachment.ComponentInstanceId == instance.StableIdentity)
                    .OrderBy(attachment => attachment.AttachmentId, StringComparer.Ordinal)
                    .ToArray();
                if (attachments.Length == 0)
                {
                    continue;
                }

                if (instance.Definition.RendererAdapter != RendererAdapterIdentity.CustomElement
                    || !TryGetStateKey(state.Initializer, out string initialState))
                {
                    throw Unsupported(instance, "Browser frames require a string or nullary enum state with a CustomElement presentation.");
                }

                if (attachments.Any(attachment => attachment.Payload is null))
                {
                    throw Unsupported(instance, "M0 browser frames require compiler-emitted CustomElement payload facts.");
                }

                var events = new List<BrowserEvent>();
                foreach (BoundComponentEventTransition transition in state.Transitions.OrderBy(transition => transition.Name, StringComparer.Ordinal))
                {
                    if (transition.Parameters.Count != 0 || !TryGetBrowserTransition(state, transition.NextState, out BrowserEvent @event))
                    {
                        throw Unsupported(instance, $"Event '{transition.Name}' must be a zero-payload string, enum, or match-selected state transition in browser frames.");
                    }

                    events.Add(@event with { Name = transition.Name });
                }

                frames.Add(new BrowserFrame(
                    instance.StableIdentity,
                    instance.Definition.StableIdentity,
                    instance.ParentComponentInstance?.StableIdentity,
                    instance.StateIdentity,
                    initialState,
                    attachments.Select(attachment => attachment.AttachmentId).ToArray(),
                    events,
                    state.PresentationBranches));
            }
        }

        var writer = new StringBuilder();
        writer.AppendLine("import { registerComponentFrames } from \"@copeland/browser-v1\";");
        writer.AppendLine();
        writer.AppendLine("registerComponentFrames([");
        foreach (BrowserFrame frame in frames)
        {
            writer.AppendLine("  {");
            WriteProperty(writer, "componentInstanceId", frame.ComponentInstanceId, trailingComma: true);
            WriteProperty(writer, "componentDefinitionId", frame.ComponentDefinitionId, trailingComma: true);
            WriteNullableProperty(writer, "parentComponentInstanceId", frame.ParentComponentInstanceId, trailingComma: true);
            WriteProperty(writer, "stateIdentity", frame.StateIdentity, trailingComma: true);
            WriteProperty(writer, "initialState", frame.InitialState, trailingComma: true);
            writer.AppendLine("    attachmentIds: [" + string.Join(", ", frame.AttachmentIds.Select(attachmentId => JsonSerializer.Serialize(attachmentId))) + "],");
            writer.AppendLine("    eventContracts: {");
            foreach (BrowserEvent @event in frame.Events)
            {
                WriteEventContract(writer, @event, "      ");
            }
            writer.AppendLine("    },");
            if (frame.Events.Count == 1)
            {
                WriteProperty(writer, "rendererEventName", frame.Events[0].Name, trailingComma: true);
            }
            WriteProjection(writer, frame);
            writer.AppendLine("  },");
        }
        writer.AppendLine("]); ");
        return writer.ToString();
    }

    private static ComponentFrameArtifactException Unsupported(BoundComponentInstance instance, string detail)
        => new("COPE-COMPONENT-STATE-BROWSER-0001", $"Component '{instance.Definition.Function.Name}' cannot emit browser frame '{instance.StateIdentity}'. {detail}");

    private static bool TryGetStateKey(BoundExpression expression, out string value)
    {
        switch (expression)
        {
            case BoundLiteralExpression { Value: string text }:
                value = text;
                return true;
            case BoundEnumValueExpression { IsConstructor: false } enumValue:
                value = enumValue.Case.Name;
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool TryGetBrowserTransition(BoundComponentStateModel state, BoundExpression expression, out BrowserEvent transition)
    {
        if (TryGetStateKey(expression, out string stateKey))
        {
            transition = new BrowserEvent(string.Empty, stateKey, []);
            return true;
        }

        if (expression is BoundMatchExpression match
            && match.Scrutinee is BoundVariableExpression { Variable: var variable }
            && ReferenceEquals(variable, state.State)
            && match.Arms.All(arm => TryGetStateKey(arm.Expression, out _)))
        {
            BrowserTransitionArm[] arms = match.Arms.Select(arm =>
            {
                _ = TryGetStateKey(arm.Expression, out string nextState);
                return new BrowserTransitionArm(arm.Case.Name, nextState);
            }).ToArray();
            transition = new BrowserEvent(string.Empty, null, arms);
            return true;
        }

        transition = default!;
        return false;
    }

    private static void WriteEventContract(StringBuilder writer, BrowserEvent @event, string indent)
    {
        writer.Append(indent).Append(JsonSerializer.Serialize(@event.Name)).Append(": { payload: \"void\", transition: ");
        if (@event.NextState is not null)
        {
            writer.Append("() => ").Append(JsonSerializer.Serialize(@event.NextState));
        }
        else
        {
            writer.Append("(_payload, currentState) => {").AppendLine();
            writer.Append(indent).AppendLine("  switch (currentState) {");
            foreach (BrowserTransitionArm arm in @event.Arms)
            {
                writer.Append(indent).Append("    case ").Append(JsonSerializer.Serialize(arm.CurrentState)).Append(": return ")
                    .Append(JsonSerializer.Serialize(arm.NextState)).AppendLine(";");
            }
            writer.Append(indent).AppendLine("    default: throw new Error(\"state transition has no selected arm\");");
            writer.Append(indent).AppendLine("  }");
            writer.Append(indent).Append("}");
        }
        writer.AppendLine(" },");
    }

    private static void WriteProjection(StringBuilder writer, BrowserFrame frame)
    {
        if (frame.Branches.Count == 0)
        {
            writer.AppendLine("    project: (state, plans) => plans.map(plan => ({ ...plan, payload: { ...plan.payload, label: state } })),");
            return;
        }

        writer.AppendLine("    project: (state, plans) => {");
        writer.AppendLine("      const retainedPlans = plans.map(plan => ({ ...plan, payload: { ...plan.payload, label: state } }));");
        writer.AppendLine("      switch (state) {");
        foreach (BoundPresentationBranch branch in frame.Branches)
        {
            writer.Append("        case ").Append(JsonSerializer.Serialize(branch.StatePattern)).AppendLine(":");
            writer.AppendLine("          return {");
            writer.AppendLine("            plans: retainedPlans,");
            writer.AppendLine("            frames: [");
            foreach (BoundPresentationChildCall child in branch.ChildCalls)
            {
                WriteChildFrame(writer, frame, child);
            }
            writer.AppendLine("            ],");
            writer.AppendLine("          };");
        }
        writer.AppendLine("        default:");
        writer.AppendLine("          return { plans: retainedPlans, frames: [] };");
        writer.AppendLine("      }");
        writer.AppendLine("    },");
    }

    private static void WriteChildFrame(StringBuilder writer, BrowserFrame parent, BoundPresentationChildCall child)
    {
        if (child.Definition.RendererAdapter != RendererAdapterIdentity.CustomElement
            || child.Definition.AttachmentPayload is not AttachmentPlanPayload payload)
        {
            throw new ComponentFrameArtifactException(
                "COPE-COMPONENT-STATE-BROWSER-0002",
                $"State-selected child '{child.Definition.Function.Name}' in '{parent.ComponentInstanceId}' is unavailable for the browser target because it has no CustomElement attachment projection.");
        }

        string childInstanceId = parent.ComponentInstanceId + "::branch-child::" + child.AuthoredIdentity;
        string childAttachmentId = childInstanceId + "::attachment";
        string childState = "initial";
        IReadOnlyList<BrowserEvent> events = [];
        if (child.Definition.State is BoundComponentStateModel state)
        {
            if (!TryGetStateKey(state.Initializer, out childState)
                || state.Transitions.Any(transition => transition.Parameters.Count != 0 || !TryGetStateKey(transition.NextState, out _)))
            {
                throw new ComponentFrameArtifactException(
                    "COPE-COMPONENT-STATE-BROWSER-0002",
                    $"State-selected child '{child.Definition.Function.Name}' has a browser-incompatible state transition.");
            }
            events = state.Transitions
                .Select(transition =>
                {
                    _ = TryGetStateKey(transition.NextState, out string nextState);
                    return new BrowserEvent(transition.Name, nextState, []);
                })
                .OrderBy(@event => @event.Name, StringComparer.Ordinal)
                .ToArray();
        }

        writer.AppendLine("              {");
        WriteProperty(writer, "componentInstanceId", childInstanceId, trailingComma: true, indent: "                ");
        WriteProperty(writer, "componentDefinitionId", child.Definition.StableIdentity, trailingComma: true, indent: "                ");
        WriteProperty(writer, "parentComponentInstanceId", parent.ComponentInstanceId, trailingComma: true, indent: "                ");
        WriteProperty(writer, "stateIdentity", childInstanceId + "::state", trailingComma: true, indent: "                ");
        WriteProperty(writer, "initialState", childState, trailingComma: true, indent: "                ");
        writer.Append("                attachmentIds: [").Append(JsonSerializer.Serialize(childAttachmentId)).AppendLine("],");
        writer.AppendLine("                eventContracts: {");
        foreach (BrowserEvent @event in events)
        {
            WriteEventContract(writer, @event, "                  ");
        }
        writer.AppendLine("                },");
        if (events.Count == 1)
        {
            WriteProperty(writer, "rendererEventName", events[0].Name, trailingComma: true, indent: "                ");
        }
        writer.AppendLine("                project: (state, plans) => plans.map(plan => ({ ...plan, payload: { ...plan.payload, label: state } })),");
        writer.AppendLine("                plans: retainedPlans.slice(0, 1).map(plan => ({");
        writer.AppendLine("                  ...plan,");
        WriteProperty(writer, "attachmentId", childAttachmentId, trailingComma: true, indent: "                  ");
        WriteProperty(writer, "componentDefinitionId", child.Definition.StableIdentity, trailingComma: true, indent: "                  ");
        WriteProperty(writer, "componentInstanceId", childInstanceId, trailingComma: true, indent: "                  ");
        WriteProperty(writer, "parentComponentInstanceId", parent.ComponentInstanceId, trailingComma: true, indent: "                  ");
        writer.Append("                  payload: { tagName: ").Append(JsonSerializer.Serialize(payload.TagName)).Append(", label: ").Append(JsonSerializer.Serialize(payload.Label ?? childState)).AppendLine(" },");
        writer.AppendLine("                })),");
        writer.AppendLine("              },");
    }

    private static void WriteProperty(StringBuilder writer, string name, string value, bool trailingComma, string indent = "    ")
        => writer.Append(indent).Append(name).Append(": ").Append(JsonSerializer.Serialize(value)).AppendLine(trailingComma ? "," : string.Empty);

    private static void WriteNullableProperty(StringBuilder writer, string name, string? value, bool trailingComma)
        => writer.Append("    ").Append(name).Append(": ").Append(value is null ? "null" : JsonSerializer.Serialize(value)).AppendLine(trailingComma ? "," : string.Empty);

    private sealed record BrowserFrame(
        string ComponentInstanceId,
        string ComponentDefinitionId,
        string? ParentComponentInstanceId,
        string StateIdentity,
        string InitialState,
        IReadOnlyList<string> AttachmentIds,
        IReadOnlyList<BrowserEvent> Events,
        IReadOnlyList<BoundPresentationBranch> Branches);

    private sealed record BrowserEvent(string Name, string? NextState, IReadOnlyList<BrowserTransitionArm> Arms);
    private sealed record BrowserTransitionArm(string CurrentState, string NextState);
}

internal sealed class ComponentFrameArtifactException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
