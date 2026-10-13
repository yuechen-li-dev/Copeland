using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// React projection of the existing canonical browser component-frame registry.
/// The registry owns state and dispatch. This adapter retains only immutable
/// presentation snapshots, subscriptions, and instance-bound typed endpoints.
/// </summary>
internal static class ComponentStateRuntime
{
    internal const string ReactImport = "import { createElement as __cope_component_element, useId as __cope_component_id, useRef as __cope_component_ref, useSyncExternalStore as __cope_component_subscribe, useLayoutEffect as __cope_component_commit, useEffect as __cope_component_effect } from \"react\";";
    internal const string FrameImport = "import { registerComponentFrames as __cope_component_register, dispatchComponentEvent as __cope_component_dispatch, destroyComponentFrame as __cope_component_destroy } from \"@copeland/browser-v1\";";

    internal static bool IsRequired(MirProgram program)
        => program.Functions.Any(function => function.ComponentState is not null)
            || program.JavaScriptHostImports.Any(IsProjectionImport);

    internal static bool IsProjectionImport(MirJavaScriptHostImport import)
        => import.ModuleSpecifier == "@copeland/browser-v1" && import.ExportName == "presentViewport";

    internal static void Emit(JavaScriptTextWriter writer)
    {
        const string source = """
            const __cope_component_registry_key = Symbol.for("copeland.ts.react-projections.v1");
            let __cope_component_registry = globalThis[__cope_component_registry_key];
            if (__cope_component_registry === undefined) {
                __cope_component_registry = { instances: new Map(), diagnostics: [] };
                Object.defineProperty(globalThis, __cope_component_registry_key, {
                    value: __cope_component_registry,
                    writable: false,
                    configurable: false,
                });
            }

            function __cope_component_projection(identity, initial, present, createEndpoints, transitions) {
                const listeners = new Set();
                let presenter = present;
                let evaluating = false;
                let pendingPresentation;
                let pending = false;
                let active = false;
                let snapshot;
                const projection = {
                    identity,
                    stateIdentity: identity + "::state",
                    endpoints: null,
                    trace: [],
                    getSnapshot: () => snapshot,
                    subscribe: (listener) => {
                        listeners.add(listener);
                        return () => listeners.delete(listener);
                    },
                    setPresenter: (next) => { presenter = next; },
                    mount: () => {
                        __cope_component_register([definition]);
                        active = true;
                        __cope_component_registry.instances.set(identity, projection);
                    },
                    destroy: () => {
                        active = false;
                        __cope_component_destroy(identity);
                        __cope_component_registry.instances.delete(identity);
                    },
                    acknowledge: () => { trace("PresentationCommitted", snapshot.revision); },
                    project: () => evaluate(snapshot.state),
                    dispatch: (event, payload) => {
                        if (!active) {
                            diagnose("COPE-COMPONENT-STATE-0103", event, "Event delivered to an unmounted component owner.");
                            return;
                        }
                        if (evaluating) {
                            diagnose("COPE-COMPONENT-STATE-0101", event, "Reentrant input during transition or presentation evaluation is unsupported.");
                            return;
                        }
                        try {
                            __cope_component_dispatch(identity, event, payload);
                        } catch (error) {
                            const message = String(error);
                            let id = "COPE-COMPONENT-STATE-0101";
                            if (message.includes("COPE-COMPONENT-STATE-0102")) {
                                id = "COPE-COMPONENT-STATE-0102";
                            } else if (message.includes("COPE-COMPONENT-STATE-0103")) {
                                id = "COPE-COMPONENT-STATE-0103";
                            }
                            diagnose(id, event, message);
                        }
                    },
                };
                function trace(kind, revision) {
                    projection.trace.push(Object.freeze({ kind, revision }));
                    if (projection.trace.length > 64) projection.trace.shift();
                }
                function diagnose(id, event, message) {
                    __cope_component_registry.diagnostics.push(Object.freeze({
                        id, componentInstanceId: identity, stateIdentity: projection.stateIdentity, eventName: event, message,
                    }));
                    if (__cope_component_registry.diagnostics.length > 128) __cope_component_registry.diagnostics.shift();
                }
                function evaluate(state) {
                    if (evaluating) throw new Error("Component presentation reentered its owner.");
                    evaluating = true;
                    try {
                        return presenter(state, projection.endpoints);
                    } finally {
                        evaluating = false;
                    }
                }
                const eventContracts = {};
                for (const [name, transition] of Object.entries(transitions)) {
                    eventContracts[name] = {
                        payload: "typed-copeland-arguments",
                        transition: (payload, currentState) => {
                            if (!Array.isArray(payload) || payload.length !== transition.length - 1) {
                                throw new Error("Typed component input requires an argument tuple of the declared arity.");
                            }
                            let next;
                            evaluating = true;
                            try {
                                next = transition(currentState, ...payload);
                            } finally {
                                evaluating = false;
                            }
                            try {
                                pendingPresentation = evaluate(next);
                            } catch (error) {
                                throw new Error("COPE-COMPONENT-STATE-0102 " + String(error));
                            }
                            pending = true;
                            return next;
                        },
                    };
                }
                projection.endpoints = Object.freeze(createEndpoints(projection));
                const state = initial();
                snapshot = Object.freeze({ state, presentation: evaluate(state), revision: 0 });
                const definition = {
                    componentInstanceId: identity,
                    componentDefinitionId: identity.split("::react::")[0],
                    parentComponentInstanceId: null,
                    stateIdentity: projection.stateIdentity,
                    attachmentIds: [],
                    eventContracts,
                    get initialState() { return snapshot.state; },
                    project: (state) => {
                        if (pending) {
                            snapshot = Object.freeze({ state, presentation: pendingPresentation, revision: snapshot.revision + 1 });
                            pending = false;
                            pendingPresentation = undefined;
                            trace("StateCommitted", snapshot.revision);
                            for (const listener of listeners) listener();
                        }
                        return [];
                    },
                };
                return projection;
            }

            function __cope_component_project(definition, initial, present, createEndpoints, transitions) {
                const id = __cope_component_id();
                const owner = __cope_component_ref(null);
                if (owner.current === null) {
                    owner.current = __cope_component_projection(definition + "::react::" + id, initial, present, createEndpoints, transitions);
                }
                const projection = owner.current;
                projection.setPresenter(present);
                const snapshot = __cope_component_subscribe(projection.subscribe, projection.getSnapshot, projection.getSnapshot);
                __cope_component_commit(() => {
                    projection.mount();
                    return () => projection.destroy();
                }, [projection]);
                __cope_component_commit(() => { projection.acknowledge(); }, [projection, snapshot]);
                return projection.project();
            }

            function __cope_component_viewport_host(props) {
                __cope_component_effect(() => {
                    const changed = () => props.onResize(window.innerWidth);
                    window.addEventListener("resize", changed, { passive: true });
                    window.addEventListener("orientationchange", changed, { passive: true });
                    return () => {
                        window.removeEventListener("resize", changed);
                        window.removeEventListener("orientationchange", changed);
                    };
                }, [props.onResize]);
                return props.children;
            }

            function __cope_component_viewport(children, onResize) {
                return __cope_component_element(__cope_component_viewport_host, { onResize }, children);
            }
            """;
        foreach (string line in source.Split('\n'))
        {
            writer.WriteLine(line.TrimEnd('\r'));
        }
    }
}
