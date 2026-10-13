import {
    registerRendererAdapter,
    attachRenderer,
    updateRenderer,
    detachRenderer,
} from "@copeland/browser-v1";

export interface PresentationLease {
    current(): boolean;
    acknowledge(): boolean;
    release(): void;
}

// Compiler-owned ticket. Identity includes the canonical owner generation;
// the lease also binds the exact revision and physical connection lifetime.
export interface PresentationTicket {
    readonly identity: string;
    readonly generation: number;
    readonly revision: number;
    connect(release: () => void): PresentationLease;
}

export interface IslandInstance<Props> {
    update(props: Props, committed: () => void): void;
    unmount(): void;
}

export interface IslandExecutor<Props> {
    readonly identity: string;
    mount(
        host: HTMLElement,
        props: Props,
        committed: () => void,
    ): IslandInstance<Props>;
}

interface RendererPlan {
    hostSelector: string;
    payload: string;
}

interface Payload<Props> {
    props: Props;
    committed(): void;
}

// This delegates lifecycle/host accounting to the canonical browser registry.
// Only foreign props and renderer roots are retained here; no application state,
// transition queue, child ownership tree, or effect engine is duplicated.
export function registerIslandExecutor<Props extends object>(
    executor: IslandExecutor<Props>,
) {
    const payloads = new Map<string, Payload<Props>>();
    const instances = new WeakMap<HTMLElement, IslandInstance<Props>>();

    function payload(plan: RendererPlan): Payload<Props> {
        const value = payloads.get(plan.payload);
        if (value === undefined)
            throw new Error(
                "COPE-VIEW-HOST-0101 Missing private island payload.",
            );
        return value;
    }

    registerRendererAdapter(executor.identity, {
        mount(plan: RendererPlan) {
            const host = document.querySelector(plan.hostSelector);
            if (!(host instanceof HTMLElement))
                throw new Error(
                    "COPE-VIEW-HOST-0102 Missing assigned island host.",
                );
            const root = document.createElement("div");
            host.appendChild(root);
            const value = payload(plan);
            try {
                instances.set(
                    root,
                    executor.mount(root, value.props, value.committed),
                );
            } catch (error) {
                root.remove();
                throw error;
            }
            return root;
        },
        update(plan: RendererPlan, root: HTMLElement) {
            const instance = instances.get(root);
            if (instance === undefined)
                throw new Error(
                    "COPE-VIEW-HOST-0101 Missing private island root.",
                );
            const value = payload(plan);
            instance.update(value.props, value.committed);
        },
        unmount(_plan: RendererPlan, root: HTMLElement) {
            const instance = instances.get(root);
            instances.delete(root);
            instance?.unmount();
            root.remove();
        },
    });

    interface Connection {
        leases: number;
        release(): void;
        update(ticket: PresentationTicket, props: Props): void;
    }
    const connections = new WeakMap<HTMLElement, Connection>();

    function createConnection(host: HTMLElement): Connection {
        if (host.id.length === 0)
            throw new Error(
                "COPE-VIEW-HOST-0102 Assigned host requires an ID.",
            );
        const selector = "#" + CSS.escape(host.id);
        let mounted = false;
        let closed = false;
        let identity: string | undefined;
        let lease: PresentationLease | undefined;

        function release() {
            if (closed) return;
            closed = true;
            lease?.release();
            if (mounted && identity !== undefined) {
                detachRenderer(identity, identity);
                payloads.delete(identity);
            }
        }

        return {
            leases: 0,
            release,
            update(ticket: PresentationTicket, props: Props) {
                if (ticket === undefined)
                    throw new Error(
                        "COPE-VIEW-HOST-0004 Presentation hosts require a canonical stateful owner.",
                    );
                if (closed)
                    throw new Error(
                        "COPE-VIEW-HOST-0103 Island connection has retired.",
                    );
                if (identity !== undefined && identity !== ticket.identity) {
                    throw new Error(
                        "COPE-VIEW-HOST-0103 Island owner generation changed without remount.",
                    );
                }
                identity = ticket.identity;
                lease?.release();
                const currentLease = ticket.connect(release);
                lease = currentLease;
                const guarded: Record<string, unknown> = {};
                for (const [name, value] of Object.entries(props)) {
                    if (typeof value === "function") {
                        guarded[name] = (...args: unknown[]) => {
                            if (currentLease.current()) return value(...args);
                        };
                    } else {
                        guarded[name] = value;
                    }
                }
                payloads.set(identity, {
                    props: Object.freeze(guarded) as Props,
                    committed: () => {
                        currentLease.acknowledge();
                    },
                });
                if (mounted) {
                    updateRenderer(identity, identity, selector, identity);
                } else {
                    attachRenderer(
                        identity,
                        identity,
                        selector,
                        executor.identity,
                        "",
                        identity,
                    );
                    mounted = true;
                }
            },
        };
    }

    return function connectIsland(host: HTMLElement) {
        let connection = connections.get(host);
        if (connection === undefined) {
            connection = createConnection(host);
            connections.set(host, connection);
        }
        const current = connection;
        current.leases += 1;
        let released = false;
        return {
            update(ticket: PresentationTicket, props: Props) {
                if (released)
                    throw new Error(
                        "COPE-VIEW-HOST-0103 Physical connection has retired.",
                    );
                current.update(ticket, props);
            },
            release() {
                if (released) return;
                released = true;
                current.leases -= 1;
                // StrictMode can disconnect and reconnect an unchanged host in
                // the same commit. Preserve its foreign root through that replay.
                queueMicrotask(() => {
                    if (current.leases !== 0) return;
                    current.release();
                    if (connections.get(host) === current)
                        connections.delete(host);
                });
            },
        };
    };
}
