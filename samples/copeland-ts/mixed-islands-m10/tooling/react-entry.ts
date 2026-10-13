import { createElement, StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import * as demo from "@demo";
import * as runtime from "@copeland/browser-v1";

await Reflect.get(globalThis, "__copelandFrameArtifactsReady");
const present = Reflect.get(demo, "Application") as () => ReactNode;
const host = document.getElementById("app");
if (host === null) throw new Error("Missing application host.");
const root = createRoot(host);
root.render(createElement(StrictMode, null, present()));
Object.defineProperty(globalThis, "__mixedDemo", {
    value: { runtime, unmount: () => root.unmount() },
});
