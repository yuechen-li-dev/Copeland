using System.Text;
using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;

namespace Copeland.Cli;

/// <summary>
/// Emits the versioned, inert browser frame envelope. The browser runtime owns
/// event scheduling, transition execution, and attachment reconciliation.
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
                    state.PresentationBranches,
                    module.LogicalPath));
            }
        }

        var writer = new StringBuilder();
        writer.AppendLine("// Copeland component-frame envelope v1. This module contains data only.");
        writer.AppendLine("export default {");
        writer.AppendLine("  schemaVersion: 1,");
        writer.AppendLine("  projectId: \"copeland\",");
        writer.AppendLine("  frameDefinitions: [");
        foreach (BrowserFrame frame in frames)
        {
            WriteFrameDefinition(writer, frame);
        }
        writer.AppendLine("  ],");
        writer.AppendLine("  frameInstances: [");
        foreach (BrowserFrame frame in frames)
        {
            writer.AppendLine("    {");
            WriteProperty(writer, "componentInstanceId", frame.ComponentInstanceId, trailingComma: true, indent: "      ");
            WriteProperty(writer, "frameDefinitionId", FrameDefinitionId(frame), trailingComma: true, indent: "      ");
            WriteNullableProperty(writer, "parentComponentInstanceId", frame.ParentComponentInstanceId, trailingComma: true, indent: "      ");
            WriteProperty(writer, "initialState", frame.InitialState, trailingComma: true, indent: "      ");
            WriteSource(writer, frame.SourcePath, "      ");
            writer.AppendLine("    },");
        }
        writer.AppendLine("  ],");
        writer.AppendLine("};");
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

    private static void WriteFrameDefinition(StringBuilder writer, BrowserFrame frame)
    {
        writer.AppendLine("    {");
        WriteProperty(writer, "frameDefinitionId", FrameDefinitionId(frame), trailingComma: true, indent: "      ");
        WriteProperty(writer, "componentDefinitionId", frame.ComponentDefinitionId, trailingComma: true, indent: "      ");
        WriteProperty(writer, "stateIdentity", frame.StateIdentity, trailingComma: true, indent: "      ");
        writer.AppendLine("      attachmentIds: [" + string.Join(", ", frame.AttachmentIds.Select(attachmentId => JsonSerializer.Serialize(attachmentId))) + "],");
        writer.AppendLine("      events: [");
        foreach (BrowserEvent @event in frame.Events)
        {
            WriteEvent(writer, frame, @event);
        }
        writer.AppendLine("      ],");
        if (frame.Events.Count == 1)
        {
            WriteProperty(writer, "rendererEventName", frame.Events[0].Name, trailingComma: true, indent: "      ");
        }
        WritePresentationBranches(writer, frame);
        WriteSource(writer, frame.SourcePath, "      ");
        writer.AppendLine("    },");
    }

    private static string FrameDefinitionId(BrowserFrame frame)
        => frame.ComponentInstanceId + "::frame-definition";

    private static void WriteEvent(StringBuilder writer, BrowserFrame frame, BrowserEvent @event)
    {
        writer.AppendLine("        {");
        WriteProperty(writer, "eventId", frame.StateIdentity + "::event::" + @event.Name, trailingComma: true, indent: "          ");
        WriteProperty(writer, "name", @event.Name, trailingComma: true, indent: "          ");
        writer.AppendLine("          payloadContract: \"void\",");
        writer.Append("          transition: ");
        if (@event.NextState is not null)
        {
            writer.Append("{ kind: \"constant\", nextState: ").Append(JsonSerializer.Serialize(@event.NextState)).AppendLine(" },");
        }
        else
        {
            writer.AppendLine("{ kind: \"match\", arms: [");
            foreach (BrowserTransitionArm arm in @event.Arms)
            {
                writer.Append("            { statePattern: ").Append(JsonSerializer.Serialize(arm.CurrentState))
                    .Append(", nextState: ").Append(JsonSerializer.Serialize(arm.NextState)).AppendLine(" },");
            }
            writer.AppendLine("          ] },");
        }
        writer.AppendLine("        },");
    }

    private static void WritePresentationBranches(StringBuilder writer, BrowserFrame frame)
    {
        writer.AppendLine("      presentationBranches: [");
        foreach (BoundPresentationBranch branch in frame.Branches)
        {
            writer.AppendLine("        {");
            WriteProperty(writer, "branchId", frame.StateIdentity + "::branch::" + branch.StatePattern, trailingComma: true, indent: "          ");
            WriteProperty(writer, "statePattern", branch.StatePattern, trailingComma: true, indent: "          ");
            writer.AppendLine("          childFrames: [");
            foreach (BoundPresentationChildCall child in branch.ChildCalls)
            {
                WriteChildFrame(writer, frame, child);
            }
            writer.AppendLine("          ],");
            writer.AppendLine("        },");
        }
        writer.AppendLine("      ],");
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

        writer.AppendLine("            {");
        WriteProperty(writer, "componentInstanceId", childInstanceId, trailingComma: true, indent: "              ");
        WriteProperty(writer, "componentDefinitionId", child.Definition.StableIdentity, trailingComma: true, indent: "              ");
        WriteProperty(writer, "parentComponentInstanceId", parent.ComponentInstanceId, trailingComma: true, indent: "              ");
        WriteProperty(writer, "stateIdentity", childInstanceId + "::state", trailingComma: true, indent: "              ");
        WriteProperty(writer, "initialState", childState, trailingComma: true, indent: "              ");
        writer.Append("              attachmentIds: [").Append(JsonSerializer.Serialize(childAttachmentId)).AppendLine("],");
        writer.AppendLine("              events: [");
        foreach (BrowserEvent @event in events)
        {
            WriteEvent(writer, new BrowserFrame(childInstanceId, child.Definition.StableIdentity, parent.ComponentInstanceId, childInstanceId + "::state", childState, [childAttachmentId], events, [], parent.SourcePath), @event);
        }
        writer.AppendLine("              ],");
        if (events.Count == 1)
        {
            WriteProperty(writer, "rendererEventName", events[0].Name, trailingComma: true, indent: "              ");
        }
        writer.AppendLine("              attachment: {");
        WriteProperty(writer, "attachmentId", childAttachmentId, trailingComma: true, indent: "                ");
        WriteProperty(writer, "componentDefinitionId", child.Definition.StableIdentity, trailingComma: true, indent: "                ");
        writer.Append("                payload: { tagName: ").Append(JsonSerializer.Serialize(payload.TagName)).Append(", label: ").Append(JsonSerializer.Serialize(payload.Label ?? childState)).AppendLine(" },");
        writer.AppendLine("              },");
        WriteSource(writer, parent.SourcePath, "              ");
        writer.AppendLine("            },");
    }

    private static void WriteProperty(StringBuilder writer, string name, string value, bool trailingComma, string indent = "    ")
        => writer.Append(indent).Append(name).Append(": ").Append(JsonSerializer.Serialize(value)).AppendLine(trailingComma ? "," : string.Empty);

    private static void WriteNullableProperty(StringBuilder writer, string name, string? value, bool trailingComma, string indent = "    ")
        => writer.Append(indent).Append(name).Append(": ").Append(value is null ? "null" : JsonSerializer.Serialize(value)).AppendLine(trailingComma ? "," : string.Empty);

    private static void WriteSource(StringBuilder writer, string path, string indent)
        => writer.Append(indent).Append("source: { path: ").Append(JsonSerializer.Serialize(path)).AppendLine(", line: 0, column: 0 },");

    private sealed record BrowserFrame(
        string ComponentInstanceId,
        string ComponentDefinitionId,
        string? ParentComponentInstanceId,
        string StateIdentity,
        string InitialState,
        IReadOnlyList<string> AttachmentIds,
        IReadOnlyList<BrowserEvent> Events,
        IReadOnlyList<BoundPresentationBranch> Branches,
        string SourcePath);

    private sealed record BrowserEvent(string Name, string? NextState, IReadOnlyList<BrowserTransitionArm> Arms);
    private sealed record BrowserTransitionArm(string CurrentState, string NextState);
}

internal sealed class ComponentFrameArtifactException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
