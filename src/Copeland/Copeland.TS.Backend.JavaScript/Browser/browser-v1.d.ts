declare module "@copeland/browser-v1" {
    export function registerRendererAdapter(
        identity: string,
        adapter: object,
    ): void;
    export function attachRenderer(
        attachment: string,
        owner: string,
        selector: string,
        adapter: string,
        tag: string,
        payload: string,
    ): void;
    export function updateRenderer(
        attachment: string,
        owner: string,
        selector: string,
        payload: string,
    ): void;
    export function detachRenderer(attachment: string, owner: string): void;
}
