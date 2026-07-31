using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ComponentStateM0Tests
{
    [Fact]
    public void Component_state_and_typed_events_are_component_local_compiler_facts()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            record CounterState { count: int; }

            function Counter(): ReactNode {
                state current: CounterState = { count: 0 };
                on Increment() => current with { count: current.count + 1 };
                return <span>{current.count}</span>;
            }

            stream Page<0px, 0px> {
                width: 160px;
                height: 40px;
                content: Counter() { height: fill; }
            }
            """);

        Assert.Empty(project.Diagnostics);
        BoundProgram program = Assert.Single(project.Modules).BoundCompilation!.Program;
        BoundComponentDefinition counter = Assert.Single(program.ComponentDefinitions);
        BoundComponentStateModel state = Assert.IsType<BoundComponentStateModel>(counter.State);
        BoundComponentInstance instance = Assert.Single(program.ComponentInstances);

        Assert.Equal("current", state.State.Name);
        Assert.Equal("CounterState", state.State.Type.Name);
        Assert.Equal("Increment", Assert.Single(state.Transitions).Name);
        Assert.Empty(Assert.Single(state.Transitions).Parameters);
        Assert.Equal(instance.StableIdentity + "::state", instance.StateIdentity);
    }

    [Fact]
    public void Component_transition_effects_bind_with_default_and_attachment_completion_phases()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            function PersistDraft(draft: string): void { }
            function FocusDialog(): void { }

            function Editor(): ReactNode {
                state current: string = "Idle";
                on Saved() => "Saved";
                on Save(draft: string) => "Saving" effect PersistDraft(draft) => Saved();
                on Open() => "Open" after AttachmentsSettled effect FocusDialog();
                return <span>{current}</span>;
            }
            """);

        Assert.Empty(project.Diagnostics);
        BoundComponentDefinition editor = Assert.Single(
            Assert.Single(project.Modules).BoundCompilation!.Program.ComponentDefinitions,
            definition => definition.Function.Name == "Editor");
        BoundComponentEventTransition[] transitions = editor.State!.Transitions.ToArray();
        BoundComponentEventTransition save = Assert.Single(transitions, transition => transition.Name == "Save");
        BoundComponentEventTransition open = Assert.Single(transitions, transition => transition.Name == "Open");

        BoundComponentEffect persist = Assert.Single(save.Effects);
        Assert.Equal(ComponentCompletionPhase.PresentationCommitted, persist.Phase);
        Assert.Equal("PersistDraft", persist.Invocation.Function.Name);
        Assert.Equal("Saved", persist.Completion!.EventName);
        Assert.Equal(ComponentCompletionPhase.AttachmentsSettled, Assert.Single(open.Effects).Phase);
    }

    [Fact]
    public void Invalid_component_effect_phase_return_and_completion_are_diagnosed()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            function ReturnsValue(): int { return 1; }

            function Invalid(): ReactNode {
                state current: string = "Idle";
                on Done(value: int) => "Done";
                on Save() => "Saving" after RendererCommitted effect ReturnsValue() => Missing();
                return <span>{current}</span>;
            }
            """);

        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-EFFECT-0102");
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-EFFECT-0103");
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-EFFECT-0106");
    }

    [Fact]
    public void State_frame_updates_existing_attachment_mounts_and_unmounts_children_and_releases_on_destroy()
    {
        BoundComponentInstance instance = Instance();
        HostAttachmentMir root = HostAttachmentMir.Create(instance);
        HostAttachmentMir child = root with
        {
            AttachmentId = root.AttachmentId + "::details",
            ComponentInstanceId = root.ComponentInstanceId + "::details",
            ParentComponentInstanceId = root.ComponentInstanceId,
            HostBoxId = root.HostBoxId + "::details",
        };
        var adapter = new RecordingAdapter(RendererAdapterIdentity.React);
        var registry = new RendererAttachmentRegistry(adapters: [adapter]);
        var frame = new ComponentStateFrame<bool>(
            instance,
            false,
            expanded => expanded
                ? new ComponentPresentationSnapshot([
                    new ComponentPresentationAttachment(root, "Custom Elements still work"),
                    new ComponentPresentationAttachment(child, "Details mounted")])
                : new ComponentPresentationSnapshot([
                    new ComponentPresentationAttachment(root, "Custom Elements work")]),
            registry);

        Assert.Empty(frame.Start());
        ComponentStateDispatchResult<bool> opened = frame.Dispatch("Toggle", true, (_, value) => value);
        ComponentStateDispatchResult<bool> closed = frame.Dispatch("Toggle", false, (_, value) => value);
        IReadOnlyList<ComponentStateRuntimeDiagnostic> cleanup = frame.Destroy();
        ComponentStateDispatchResult<bool> afterDestroy = new ComponentEventBridge<bool, bool>(frame, "Toggle", (_, value) => value).Deliver(true);

        Assert.True(opened.Applied);
        Assert.True(closed.Applied);
        Assert.Empty(cleanup);
        Assert.False(afterDestroy.Applied);
        Assert.Equal("COPE-COMPONENT-STATE-0103", Assert.Single(afterDestroy.Diagnostics).Id);
        Assert.Equal(instance.StateIdentity, Assert.Single(afterDestroy.Diagnostics).StateIdentity);
        Assert.Equal(
            [
                "mount:" + root.AttachmentId + ":Custom Elements work",
                "update:" + root.AttachmentId + ":Custom Elements still work",
                "mount:" + child.AttachmentId + ":Details mounted",
                "unmount:" + child.AttachmentId,
                "update:" + root.AttachmentId + ":Custom Elements work",
                "unmount:" + root.AttachmentId,
            ],
            adapter.Events);
    }

    [Fact]
    public void Invalid_state_and_event_declarations_report_stable_diagnostics()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            function Invalid(): ReactNode {
                on Click(value: int) => value;
                state current: boolean = 1;
                state again = false;
                return <span>invalid</span>;
            }
            """);

        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-STATE-0006");
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-STATE-0003");
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-STATE-0002");
    }

    [Fact]
    public void Match_return_binds_state_selected_child_presentation_branches_with_branch_qualified_identities()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";

            enum DialogState { Closed, Open }

            function ConfirmDialog(): ReactNode { return <span>confirm</span>; }

            function DialogHost(): ReactNode {
                state current: DialogState = DialogState.Closed;
                on OpenDialog() => DialogState.Open;
                on CloseDialog() => DialogState.Closed;
                return match current {
                    Closed => <span>empty</span>,
                    Open => ConfirmDialog()
                };
            }

            stream Page<0px, 0px> {
                width: 160px;
                height: 40px;
                content: DialogHost() { height: fill; }
            }
            """);

        Assert.Empty(project.Diagnostics);
        BoundProgram program = Assert.Single(project.Modules).BoundCompilation!.Program;
        BoundComponentDefinition host = Assert.Single(program.ComponentDefinitions, definition => definition.Function.Name == "DialogHost");
        BoundPresentationBranch[] branches = host.State!.PresentationBranches.ToArray();

        Assert.Equal(["Closed", "Open"], branches.Select(branch => branch.StatePattern).ToArray());
        Assert.Empty(branches[0].ChildCalls);
        BoundPresentationChildCall child = Assert.Single(branches[1].ChildCalls);
        Assert.Equal("ConfirmDialog", child.Definition.Function.Name);
        Assert.Contains("presentation-branch::Open", child.AuthoredIdentity, StringComparison.Ordinal);
        Assert.StartsWith(branches[1].StableIdentity, child.AuthoredIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordered_effects_run_at_compiler_owned_completion_phases_and_reenter_through_events()
    {
        BoundComponentInstance instance = Instance();
        HostAttachmentMir root = HostAttachmentMir.Create(instance);
        HostAttachmentMir child = root with
        {
            AttachmentId = root.AttachmentId + "::dialog",
            ComponentInstanceId = root.ComponentInstanceId + "::dialog",
            ParentComponentInstanceId = root.ComponentInstanceId,
            HostBoxId = root.HostBoxId + "::dialog",
        };
        var adapter = new RecordingAdapter(RendererAdapterIdentity.React);
        var effects = new List<string>();
        var frame = new ComponentStateFrame<string>(
            instance,
            "Idle",
            state => state switch
            {
                "Open" => new ComponentPresentationSnapshot([
                    new ComponentPresentationAttachment(root, "dialog host"),
                    new ComponentPresentationAttachment(child, "dialog")]),
                _ => new ComponentPresentationSnapshot([
                    new ComponentPresentationAttachment(root, state)]),
            },
            new RendererAttachmentRegistry(adapters: [adapter]));
        var saved = new ComponentEventBridge<string, string>(frame, "Saved", (_, version) => version);
        var save = new ComponentEventBridge<string, string>(
            frame,
            "Save",
            (_, draft) => new ComponentTransition<string>(
                "Saving",
                [
                    new ComponentEffectRequest<string>(
                        new ComponentEffectDescriptor("persist", ComponentCompletionPhase.PresentationCommitted, 0, "Saved"),
                        _ =>
                        {
                            effects.Add("persist");
                            return ValueTask.FromResult<ComponentEffectCompletion<string>?>(
                                ComponentEffectCompletion<string>.ToEvent("Saved " + draft, saved));
                        }),
                    new ComponentEffectRequest<string>(
                        new ComponentEffectDescriptor("announce", ComponentCompletionPhase.PresentationCommitted, 1),
                        _ =>
                        {
                            effects.Add("announce");
                            return ValueTask.FromResult<ComponentEffectCompletion<string>?>(null);
                        }),
                ]));
        var open = new ComponentEventBridge<string, bool>(
            frame,
            "Open",
            (_, _) => new ComponentTransition<string>(
                "Open",
                [
                    new ComponentEffectRequest<string>(
                        new ComponentEffectDescriptor("focus-dialog", ComponentCompletionPhase.AttachmentsSettled, 0),
                        _ =>
                        {
                            effects.Add("focus-dialog");
                            return ValueTask.FromResult<ComponentEffectCompletion<string>?>(null);
                        }),
                ]));

        frame.Start();
        save.Deliver("v1");

        Assert.Equal("Saved v1", frame.State);
        Assert.Equal(["persist", "announce"], effects);
        List<ComponentRuntimeTrace> trace = frame.Trace.ToList();
        Assert.True(trace.FindIndex(entry => entry.Kind == "StateCommitted" && entry.EventName == "Save")
            < trace.FindIndex(entry => entry.Kind == "EffectStarted" && entry.EffectIdentity == "persist"));
        Assert.True(trace.FindIndex(entry => entry.Kind == "PresentationCommitted" && entry.EventName == "Save")
            < trace.FindIndex(entry => entry.Kind == "EffectStarted" && entry.EffectIdentity == "persist"));
        Assert.Contains(frame.Trace, trace => trace.Kind == "CompletionEventDispatched" && trace.EventName == "Saved");

        open.Deliver(true);

        Assert.Equal("Open", frame.State);
        Assert.Equal(["persist", "announce", "focus-dialog"], effects);
        Assert.Contains("mount:" + child.AttachmentId + ":dialog", adapter.Events);
        trace = frame.Trace.ToList();
        Assert.True(trace.FindIndex(entry => entry.Kind == "AttachmentsSettled" && entry.EventName == "Open")
            < trace.FindIndex(entry => entry.Kind == "EffectStarted" && entry.EffectIdentity == "focus-dialog"));
    }

    [Fact]
    public async Task Async_effect_completion_after_frame_destruction_is_discarded_without_resurrection()
    {
        BoundComponentInstance instance = Instance();
        HostAttachmentMir root = HostAttachmentMir.Create(instance);
        var completionSource = new TaskCompletionSource<ComponentEffectCompletion<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new RecordingAdapter(RendererAdapterIdentity.React);
        var frame = new ComponentStateFrame<string>(
            instance,
            "Idle",
            state => new ComponentPresentationSnapshot([new ComponentPresentationAttachment(root, state)]),
            new RendererAttachmentRegistry(adapters: [adapter]));
        var completed = new ComponentEventBridge<string, string>(frame, "Completed", (_, value) => value);
        var begin = new ComponentEventBridge<string, bool>(
            frame,
            "Begin",
            (_, _) => new ComponentTransition<string>(
                "Working",
                [
                    new ComponentEffectRequest<string>(
                        new ComponentEffectDescriptor("delayed", ComponentCompletionPhase.PresentationCommitted, 0, "Completed"),
                        _ => new ValueTask<ComponentEffectCompletion<string>?>(completionSource.Task)),
                ]));

        frame.Start();
        begin.Deliver(true);
        frame.Destroy();
        completionSource.SetResult(ComponentEffectCompletion<string>.ToEvent("Completed", completed));
        await completionSource.Task;
        Assert.True(SpinWait.SpinUntil(
            () => frame.Trace.Any(trace => trace.Kind == "EffectCompletionDiscarded"),
            TimeSpan.FromSeconds(2)));

        Assert.True(frame.IsDestroyed);
        Assert.Equal("Working", frame.State);
        Assert.Contains(frame.CompletionDiagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-EFFECT-0003");
        Assert.DoesNotContain(adapter.Events, entry => entry.Contains("Completed", StringComparison.Ordinal));
    }

    private static BoundComponentInstance Instance()
    {
        CopelandProjectCompilation project = Compile("""
            import { createElement } from "react";
            function Badge(): ReactNode { return <span>badge</span>; }
            stream Page<0px, 0px> { width: 160px; height: 40px; content: Badge() { height: fill; } }
            """);
        Assert.Empty(project.Diagnostics);
        return Assert.Single(Assert.Single(project.Modules).BoundCompilation!.Program.ComponentInstances);
    }

    private static CopelandProjectCompilation Compile(string source)
        => CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Page.tsx", "Page.tsx", source)],
            new CopelandCompilationOptions
            {
                SourcePath = "Page.tsx",
                TsXmlProfile = CopelandTsXmlProfile.ReactM0,
                NpmPackages =
                [
                    new CopelandNpmPackageContract(
                        "react",
                        "19.2.7",
                        [new CopelandNpmFunctionContract("createElement", [], "ReactNode")]),
                ],
            });

    private sealed class RecordingAdapter(RendererAdapterIdentity identity) : IRendererAttachmentAdapter
    {
        public RendererAdapterIdentity Identity { get; } = identity;
        public List<string> Events { get; } = [];

        public object? Mount(HostAttachmentMir attachment, object? payload)
        {
            Events.Add("mount:" + attachment.AttachmentId + ":" + payload);
            return attachment.AttachmentId;
        }

        public void Update(HostAttachmentMir attachment, object? rendererRoot, object? payload)
            => Events.Add("update:" + attachment.AttachmentId + ":" + payload);

        public void Unmount(HostAttachmentMir attachment, object? rendererRoot)
            => Events.Add("unmount:" + attachment.AttachmentId);
    }
}
