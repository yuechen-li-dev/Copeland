using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// React and Vue projections of the canonical browser component-frame registry.
/// The registry owns state and dispatch. This adapter retains only immutable
/// presentation snapshots, subscriptions, and instance-bound typed endpoints.
/// </summary>
internal static class ComponentStateRuntime
{
    internal const string ReactImport = "import { createElement as __cope_component_element, cloneElement as __cope_component_clone, useId as __cope_component_id, useRef as __cope_component_ref, useSyncExternalStore as __cope_component_subscribe, useLayoutEffect as __cope_component_commit, useEffect as __cope_component_effect } from \"react\";";
    internal const string VueImport = "import { h as __cope_component_element, cloneVNode as __cope_component_clone, shallowRef as __cope_component_ref, onMounted as __cope_component_mounted, onUpdated as __cope_component_updated, onBeforeUnmount as __cope_component_unmount } from \"vue\";";

    internal static bool IsVue(MirProgram program)
        => program.PresentationAdapter?.CommitProtocol == MirPresentationCommitProtocol.VueComposition
            || program.Functions.Any(function => function.ReturnType.Identifier == "VueNode");

    internal static string RendererImport(MirProgram program)
        => IsVue(program) ? VueImport : ReactImport;

    internal const string FrameImport = "import { registerComponentFrames as __cope_component_register, dispatchComponentEvent as __cope_component_dispatch, destroyComponentFrame as __cope_component_destroy } from \"@copeland/browser-v1\";";

    internal static bool IsRequired(MirProgram program)
        => program.Functions.Any(function => function.ComponentState is not null)
            || program.JavaScriptHostImports.Any(IsProjectionImport);

    internal static bool IsProjectionImport(MirJavaScriptHostImport import)
        => import.ModuleSpecifier == "@copeland/browser-v1" && import.ExportName is "presentViewport" or "ownChild";

    internal static void EmitViewChildShapes(JavaScriptTextWriter writer)
    {
        const string source = """
            const __cope_view_child_shapes_key = Symbol.for("copeland.ts.view-child-shapes.v1");
            let __cope_view_child_shapes = globalThis[__cope_view_child_shapes_key];
            if (__cope_view_child_shapes === undefined) {
                __cope_view_child_shapes = new WeakMap();
                Object.defineProperty(globalThis, __cope_view_child_shapes_key, { value: __cope_view_child_shapes });
            }
            function __cope_view_mark(node, staticChildren) {
                if (node !== null && typeof node === "object") __cope_view_child_shapes.set(node, staticChildren);
                return node;
            }
            const __cope_view_hosts_key = Symbol.for("copeland.ts.presentation-hosts.v1");
            let __cope_view_hosts = globalThis[__cope_view_hosts_key];
            if (__cope_view_hosts === undefined) {
                __cope_view_hosts = new WeakMap();
                Object.defineProperty(globalThis, __cope_view_hosts_key, { value: __cope_view_hosts });
            }
            """;
        foreach (string line in source.Split('\n')) writer.WriteLine(line.TrimEnd('\r'));
    }

    internal static void Emit(JavaScriptTextWriter writer, bool vue = false, bool copelandView = false)
    {
        string registryKey = copelandView ? "copeland.ts.view-projections.v1"
            : vue ? "copeland.ts.vue-projections.v1" : "copeland.ts.react-projections.v1";
        writer.WriteLine($"const __cope_component_registry_key = Symbol.for({JavaScriptLiteralWriter.WriteString(registryKey)});");
        const string registry = """
            let __cope_component_registry = globalThis[__cope_component_registry_key];
            if (__cope_component_registry === undefined) {
                __cope_component_registry = { instances: new Map(), diagnostics: [], generation: 0 };
                Object.defineProperty(globalThis, __cope_component_registry_key, { value: __cope_component_registry });
            }
            """;
        const string common = """
            function __cope_component_child(slot, key, node) {
                if (typeof slot !== "string" || slot.length === 0 || typeof key !== "string" || key.length === 0) {
                    throw new Error("COPE-COMPONENT-CHILD-0001 Child ownership requires a nonempty slot and domain key.");
                }
                if (node?.props?.__cope_component_definition === undefined) {
                    throw new Error("COPE-COMPONENT-CHILD-0002 ownChild requires a direct stateful Copeland component.");
                }
                return __cope_component_element(__cope_component_child_host, {
                    key: JSON.stringify([slot, key, node.props?.__cope_component_definition]), slot, domainKey: key, node,
                });
            }

            function __cope_component_projection(identity, definitionId, initial, present, createEndpoints, transitions, ownership) {
                const listeners = new Set();
                const batches = new Map();
                let children = new Map();
                let hosts = new Map();
                let outerCommit = -1;
                let acknowledged = -1;
                let presenter = present;
                let evaluating = false;
                let staged;
                let active = false;
                let registered = false;
                let retired = false;
                let leases = 0;
                let snapshot;
                const projection = {
                    identity,
                    stateIdentity: identity + "::state",
                    endpoints: null,
                    trace: [],
                    getSnapshot: () => snapshot,
                    getFrameDefinition: () => ({ ...definition, plans: [] }),
                    subscribe: listener => {
                        listeners.add(listener);
                        return () => listeners.delete(listener);
                    },
                    render: next => evaluate(snapshot.state, next),
                    commitRender: (next, candidate, revision) => {
                        if (retired || revision !== snapshot.revision) return;
                        presenter = next;
                        adopt(candidate);
                    },
                    ensureRegistered: () => {
                        if (retired || ownership?.retired) {
                            throw new Error("COPE-COMPONENT-STATE-0103 Cannot register a retired component owner.");
                        }
                        if (registered) return;
                        if (ownership !== undefined) {
                            ownership.parent.ensureRegistered();
                            ownership.projection = projection;
                        }
                        __cope_component_register([definition]);
                        registered = true;
                        __cope_component_registry.instances.set(identity, projection);
                    },
                    acquire: () => {
                        projection.ensureRegistered();
                        leases += 1;
                        active = true;
                    },
                    release: () => {
                        leases -= 1;
                        active = leases > 0;
                        // In the qualified synchronous React profile, StrictMode reconnects
                        // in this commit. Genuine removal retires at the microtask boundary.
                        queueMicrotask(() => {
                            if (leases === 0) projection.destroy();
                        });
                    },
                    destroy: () => {
                        if (retired) return;
                        retired = true;
                        active = false;
                        for (const child of children.values()) retireChild(child);
                        children.clear();
                        for (const host of hosts.values()) retireHost(host);
                        hosts.clear();
                        for (const batch of batches.values()) {
                            for (const request of batch.requests) {
                                if (request.status === "waiting") request.status = "cancelled";
                                if (request.operation?.cancel) request.operation.cancel();
                            }
                        }
                        batches.clear();
                        if (registered) __cope_component_destroy(identity);
                        registered = false;
                        __cope_component_registry.instances.delete(identity);
                        trace("OwnerRetired", snapshot.revision);
                    },
                    acknowledge: revision => {
                        if (retired || !active || revision !== snapshot.revision) return;
                        outerCommit = revision;
                        acknowledgePresentation();
                    },
                    project: () => evaluate(snapshot.state).presentation,
                    dispatch: (event, payload) => {
                        if (!active || retired || ownership?.retired) {
                            diagnose("COPE-COMPONENT-STATE-0103", event, "Event delivered to a retired or detached component owner.");
                            return;
                        }
                        if (evaluating) {
                            diagnose("COPE-COMPONENT-STATE-0101", event, "Reentrant input during transition or presentation evaluation is unsupported.");
                            return;
                        }
                        try {
                            __cope_component_dispatch(identity, event, payload);
                            flushStateEffects();
                        } catch (error) {
                            const message = String(error);
                            let id = "COPE-COMPONENT-STATE-0101";
                            if (message.includes("COPE-COMPONENT-STATE-0102")) id = "COPE-COMPONENT-STATE-0102";
                            if (message.includes("COPE-COMPONENT-STATE-0103")) id = "COPE-COMPONENT-STATE-0103";
                            diagnose(id, event, message);
                        }
                    },
                };
                function trace(kind, revision, effect, detail) {
                    projection.trace.push(Object.freeze({ kind, revision, effect, detail }));
                    if (projection.trace.length > 128) projection.trace.shift();
                }
                function diagnose(id, event, message) {
                    __cope_component_registry.diagnostics.push(Object.freeze({
                        id, componentInstanceId: identity, stateIdentity: projection.stateIdentity, eventName: event, message,
                    }));
                    if (__cope_component_registry.diagnostics.length > 128) __cope_component_registry.diagnostics.shift();
                }
                function retireChild(child) {
                    child.retired = true;
                    if (child.projection !== undefined) child.projection.destroy();
                }
                function retireHost(host) {
                    if (host.retired) return;
                    host.retired = true;
                    host.connection = null;
                    const release = host.release;
                    host.release = undefined;
                    release?.();
                    trace("PresentationHostRetired", snapshot?.revision ?? 0, host.identity);
                }
                function acknowledgePresentation() {
                    if (retired || !active || outerCommit !== snapshot.revision || acknowledged === outerCommit) return;
                    for (const host of hosts.values()) {
                        if (host.connection === null || host.acknowledged !== outerCommit) return;
                    }
                    acknowledged = outerCommit;
                    trace("PresentationCommitted", outerCommit);
                    const batch = batches.get(outerCommit);
                    if (batch === undefined) return;
                    runPhase(batch, "StateCommitted");
                    runPhase(batch, "PresentationCommitted");
                }
                function hostTicket(host, revision) {
                    return Object.freeze({
                        identity: host.identity,
                        generation: host.generation,
                        revision,
                        connect: release => {
                            if (host.retired) throw new Error("COPE-VIEW-HOST-0103 Cannot connect a retired presentation host.");
                            const connection = {};
                            host.connection = connection;
                            host.release = release;
                            host.acknowledged = -1;
                            const current = () => !retired && !host.retired
                                && host.connection === connection && snapshot.revision === revision;
                            return Object.freeze({
                                current,
                                acknowledge: () => {
                                    if (!current()) return false;
                                    host.acknowledged = revision;
                                    trace("PresentationHostCommitted", revision, host.identity);
                                    acknowledgePresentation();
                                    return true;
                                },
                                release: () => {
                                    if (host.connection !== connection) return;
                                    host.connection = null;
                                    host.release = undefined;
                                    host.acknowledged = -1;
                                },
                            });
                        },
                    });
                }
                function adopt(candidate) {
                    for (const [key, previous] of children) {
                        if (candidate.children.get(key) !== previous) retireChild(previous);
                    }
                    children = candidate.children;
                    for (const [key, previous] of hosts) {
                        if (candidate.hosts.get(key) !== previous) retireHost(previous);
                    }
                    hosts = candidate.hosts;
                }
                function evaluate(state, render = presenter, revision = snapshot?.revision ?? 0) {
                    if (evaluating) throw new Error("Component presentation reentered its owner.");
                    evaluating = true;
                    const desired = new Map();
                    const desiredHosts = new Map();
                    try {
                        const presentation = visit(render(state, projection.endpoints));
                        return { presentation, children: desired, hosts: desiredHosts };
                    } finally {
                        evaluating = false;
                    }
                    function visit(node) {
                        if (Array.isArray(node)) return node.map(visit);
                        if (node === null || typeof node !== "object" || node.props === undefined && node.children === undefined) return node;
                        const hostDefinition = typeof __cope_view_hosts === "undefined" ? undefined : __cope_view_hosts.get(node.type);
                        if (hostDefinition !== undefined) {
                            if (typeof node.props.hostKey !== "string" || node.props.hostKey.length === 0) {
                                throw new Error("COPE-VIEW-HOST-0002 Presentation hosts require a nonempty domain hostKey.");
                            }
                            const key = JSON.stringify([hostDefinition, node.props.hostKey]);
                            if (desiredHosts.has(key)) throw new Error("COPE-VIEW-HOST-0003 Duplicate presentation host key " + key);
                            let host = hosts.get(key);
                            if (host === undefined || host.retired) {
                                const generation = ++__cope_component_registry.generation;
                                host = { identity: identity + "::host::" + key + "::" + generation,
                                    generation, retired: false, connection: null, acknowledged: -1 };
                            }
                            desiredHosts.set(key, host);
                            return __cope_component_clone(node, { key: host.identity, __cope_presentation: hostTicket(host, revision) });
                        }
                        if (node.type?.__cope_owned_child === true) {
                            const slotKey = JSON.stringify([node.props.slot, node.props.domainKey]);
                            if (desired.has(slotKey)) {
                                throw new Error("COPE-COMPONENT-CHILD-0003 Duplicate child key in slot " + slotKey);
                            }
                            const childDefinition = node.props.node.props?.__cope_component_definition;
                            let child = children.get(slotKey);
                            if (child === undefined || child.definition !== childDefinition || child.retired) {
                                child = {
                                    parent: projection, definition: childDefinition, retired: false,
                                    identity: identity + "::child::" + slotKey + "::" + childDefinition + "::" + (++__cope_component_registry.generation),
                                };
                            }
                            desired.set(slotKey, child);
                            return __cope_component_clone(node, { ownership: child, key: child.identity });
                        }
                        const nodeChildren = __cope_component_children(node);
                        if (nodeChildren !== undefined) {
                            return __cope_component_with_children(node, visit(nodeChildren));
                        }
                        if (node.props?.__cope_component_definition !== undefined) {
                            throw new Error("COPE-COMPONENT-CHILD-0004 Stateful children require ownChild(slot, key, Child(...)).");
                        }
                        return node;
                    }
                }
                function settle(batch) {
                    queueCompletions(batch);
                    if (batch.requests.some(request => request.status === "waiting" || request.status === "running")) return;
                    batches.delete(batch.revision);
                }
                function complete(batch, request, result) {
                    if (retired || ownership?.retired) {
                        request.status = "cancelled";
                        trace("EffectCompletionDiscarded", batch.revision, request.identity, "Owner lifetime ended.");
                        return;
                    }
                    request.status = "completed";
                    trace("EffectCompleted", batch.revision, request.identity);
                    request.result = result;
                    settle(batch);
                }
                function queueCompletions(batch) {
                    // A fast state effect must not replace the presentation before
                    // this transition's later phases have started or been superseded.
                    if (batch.requests.some(request => request.status === "waiting")) return;
                    for (const request of batch.requests) {
                        if (request.status !== "completed" || request.complete === undefined || request.completionQueued) continue;
                        request.completionQueued = true;
                        queueMicrotask(() => {
                            if (retired || ownership?.retired) {
                                trace("EffectCompletionDiscarded", batch.revision, request.identity, "Owner lifetime ended.");
                                return;
                            }
                            try {
                                const event = request.complete(request.result);
                                projection.dispatch(event.event, event.payload);
                            } catch (error) {
                                diagnose("COPE-COMPONENT-EFFECT-0002", batch.event, "Effect completion failed: " + String(error));
                            }
                        });
                    }
                }
                function failed(batch, request, message) {
                    request.status = "failed";
                    diagnose("COPE-COMPONENT-EFFECT-0002", batch.event, message);
                    trace("EffectFailed", batch.revision, request.identity, message);
                    settle(batch);
                }
                function runPhase(batch, phase) {
                    if (retired || ownership?.retired) return;
                    for (const request of batch.requests) {
                        if (request.phase !== phase || request.status !== "waiting") continue;
                        request.status = "running";
                        trace("EffectStarted", batch.revision, request.identity, phase);
                        try {
                            const operation = request.start();
                            request.operation = operation;
                            if (operation !== null && typeof operation === "object" && typeof operation.subscribe === "function") {
                                const success = () => complete(batch, request, operation.value);
                                const cancelled = () => {
                                    request.status = "cancelled";
                                    trace("EffectCompletionDiscarded", batch.revision, request.identity, "Operation cancelled.");
                                    settle(batch);
                                };
                                const failure = () => failed(batch, request, "Effect computation failed.");
                                if (operation.subscribe(success, cancelled, failure, failure)) {
                                    if (operation.cancelled) cancelled();
                                    else if (operation.panicked || operation.transportFailed) failure();
                                    else success();
                                }
                            } else {
                                complete(batch, request, operation);
                            }
                        } catch (error) {
                            failed(batch, request, "Effect failed to start: " + String(error));
                        }
                    }
                    settle(batch);
                }
                function flushStateEffects() {
                    for (const batch of [...batches.values()]) runPhase(batch, "StateCommitted");
                }
                const eventContracts = {};
                for (const [name, handler] of Object.entries(transitions)) {
                    eventContracts[name] = {
                        payload: "typed-copeland-arguments",
                        transition: (payload, currentState) => {
                            if (!Array.isArray(payload) || payload.length !== handler.transition.length - 1) {
                                throw new Error("Typed component input requires an argument tuple of the declared arity.");
                            }
                            let next;
                            let effects;
                            evaluating = true;
                            try {
                                next = handler.transition(currentState, ...payload);
                                effects = handler.prepare(currentState, ...payload);
                            } finally {
                                evaluating = false;
                            }
                            let candidate;
                            try {
                                candidate = evaluate(next, presenter, snapshot.revision + 1);
                            } catch (error) {
                                throw new Error("COPE-COMPONENT-STATE-0102 " + String(error));
                            }
                            staged = { candidate, effects, event: name };
                            return next;
                        },
                    };
                }
                projection.endpoints = Object.freeze(createEndpoints(projection));
                const state = initial();
                const candidate = evaluate(state);
                adopt(candidate);
                snapshot = Object.freeze({ state, presentation: candidate.presentation, revision: 0 });
                const definition = {
                    componentInstanceId: identity,
                    componentDefinitionId: definitionId,
                    parentComponentInstanceId: ownership?.parent.identity ?? null,
                    stateIdentity: projection.stateIdentity,
                    attachmentIds: [],
                    eventContracts,
                    get initialState() { return snapshot.state; },
                    project: state => {
                        if (staged !== undefined) {
                            const transaction = staged;
                            staged = undefined;
                            adopt(transaction.candidate);
                            const revision = snapshot.revision + 1;
                            snapshot = Object.freeze({ state, presentation: transaction.candidate.presentation, revision });
                            for (const batch of [...batches.values()]) {
                                for (const request of batch.requests) {
                                    if (request.phase === "PresentationCommitted" && request.status === "waiting") {
                                        request.status = "cancelled";
                                        trace("EffectSuperseded", batch.revision, request.identity);
                                    }
                                }
                                settle(batch);
                            }
                            const requests = transaction.effects.map(effect => ({ ...effect, status: "waiting" }));
                            if (requests.length > 0) batches.set(revision, { revision, event: transaction.event, requests });
                            trace("StateCommitted", revision);
                            for (const listener of listeners) listener();
                            queueMicrotask(flushStateEffects);
                        }
                        return {
                            plans: [],
                            frames: [...children.values()]
                                .filter(child => child.projection !== undefined && !child.retired)
                                .map(child => child.projection.getFrameDefinition()),
                        };
                    },
                };
                return projection;
            }
            """;
        const string react = """
            function __cope_component_child_host(props) {
                if (props.ownership === undefined) {
                    throw new Error("COPE-COMPONENT-CHILD-0005 ownChild requires a canonical stateful parent presentation.");
                }
                return __cope_component_clone(props.node, { ownership: props.ownership });
            }
            __cope_component_child_host.__cope_owned_child = true;


            function __cope_component_children(node) {
                return node.props.children;
            }

            function __cope_component_with_children(node, children) {
                if (typeof __cope_view_child_shapes !== "undefined" && __cope_view_child_shapes.get(node) === true) {
                    const values = Array.isArray(children) ? children : [children];
                    return __cope_view_mark(__cope_component_clone(node, null, ...values), true);
                }
                return __cope_component_clone(node, { children });
            }
            function __cope_component_project(definition, initial, present, createEndpoints, transitions, ownership) {
                const id = __cope_component_id();
                const owner = __cope_component_ref(null);
                if (owner.current === null) {
                    const identity = ownership?.identity ?? definition + "::react::" + id + "::" + (++__cope_component_registry.generation);
                    owner.current = __cope_component_projection(identity, definition, initial, present, createEndpoints, transitions, ownership);
                }
                const projection = owner.current;
                const snapshot = __cope_component_subscribe(projection.subscribe, projection.getSnapshot, projection.getSnapshot);
                const candidate = projection.render(present);
                __cope_component_commit(() => {
                    projection.acquire();
                    return () => projection.release();
                }, [projection]);
                __cope_component_commit(() => {
                    projection.commitRender(present, candidate, snapshot.revision);
                    projection.acknowledge(snapshot.revision);
                });
                return candidate.presentation;
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
        const string vueAdapter = """
            const __cope_component_vue_hosts = new WeakMap();
            function __cope_component_vue_host(setup) {
                let component = __cope_component_vue_hosts.get(setup);
                if (component === undefined) {
                    component = {
                        props: ["inputs", "ownership", "__cope_component_definition"],
                        setup,
                    };
                    __cope_component_vue_hosts.set(setup, component);
                }
                return component;
            }

            const __cope_component_child_host = {
                props: ["node", "ownership", "slot", "domainKey"],
                __cope_owned_child: true,
                setup(props) {
                    return () => {
                        if (props.ownership === undefined) {
                            throw new Error("COPE-COMPONENT-CHILD-0005 ownChild requires a canonical stateful parent presentation.");
                        }
                        return __cope_component_clone(props.node, { ownership: props.ownership });
                    };
                },
            };

            function __cope_component_children(node) {
                if (node.type === __cope_component_viewport_host) return node.props.children;
                if (Array.isArray(node.children)) return node.children;
                return node.children?.default?.__cope_component_slot_children;
            }

            function __cope_component_with_children(node, children) {
                if (node.type === __cope_component_viewport_host) {
                    return __cope_component_clone(node, { children });
                }
                const result = __cope_component_clone(node);
                if (Array.isArray(node.children)) {
                    result.children = children;
                } else {
                    const slot = () => children;
                    slot.__cope_component_slot_children = children;
                    result.children = { ...node.children, default: slot };
                }
                return result;
            }

            function __cope_component_project(definition, initial, present, createEndpoints, transitions, ownership) {
                const identity = ownership?.identity ?? definition + "::vue::" + (++__cope_component_registry.generation);
                const projection = __cope_component_projection(identity, definition, initial, present, createEndpoints, transitions, ownership);
                const current = __cope_component_ref(projection.getSnapshot());
                const unsubscribe = projection.subscribe(() => {
                    current.value = projection.getSnapshot();
                });
                let rendered;
                const acknowledge = () => {
                    if (rendered === undefined) return;
                    projection.commitRender(present, rendered.candidate, rendered.revision);
                    projection.acknowledge(rendered.revision);
                };
                __cope_component_mounted(() => {
                    projection.acquire();
                    acknowledge();
                });
                __cope_component_updated(acknowledge);
                __cope_component_unmount(() => {
                    unsubscribe();
                    projection.release();
                });
                return () => {
                    const snapshot = current.value;
                    const candidate = projection.render(present);
                    rendered = { candidate, revision: snapshot.revision };
                    return candidate.presentation;
                };
            }

            const __cope_component_viewport_host = {
                props: ["children", "onResize"],
                setup(props) {
                    const changed = () => props.onResize(window.innerWidth);
                    __cope_component_mounted(() => {
                        window.addEventListener("resize", changed, { passive: true });
                        window.addEventListener("orientationchange", changed, { passive: true });
                    });
                    __cope_component_unmount(() => {
                        window.removeEventListener("resize", changed);
                        window.removeEventListener("orientationchange", changed);
                    });
                    return () => props.children;
                },
            };

            function __cope_component_viewport(children, onResize) {
                return __cope_component_element(__cope_component_viewport_host, { children, onResize });
            }
            """;
        string source = registry + "\n" + (vue ? vueAdapter : react) + "\n" + common;
        foreach (string line in source.Split('\n'))
        {
            writer.WriteLine(line.TrimEnd('\r'));
        }
    }
}
