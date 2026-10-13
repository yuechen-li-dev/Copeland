interface Root {
    render(node: unknown): void;
    unmount(): void;
}
const load = (specifier: string): Promise<unknown> => import(specifier);
const React = await load("react") as { createElement(type: unknown, props: unknown, child: unknown): unknown; StrictMode: unknown };
const ReactDOM = await load("react-dom/client") as { createRoot(host: Element): Root };
const demo = await load("/lifecycle/Demo.js") as { Directory(): unknown };
await Reflect.get(globalThis, "__copelandFrameArtifactsReady");
const mount = document.getElementById("app");
if (!mount) throw new Error("Missing lifecycle demonstration mount");
export const root = ReactDOM.createRoot(mount);
root.render(React.createElement(React.StrictMode, null, demo.Directory()));
