import { createElement, StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import * as demo from "@demo";

await Reflect.get(globalThis, "__copelandFrameArtifactsReady");
const lifecycle = new URL(location.href).pathname.includes("lifecycle");
const present = Reflect.get(demo, lifecycle ? "Directory" : "Application") as (
    ...inputs: number[]
) => unknown;
if (typeof present !== "function")
    throw new Error("Missing generated presentation export");
const node = lifecycle ? present() : present(window.innerWidth);
const element = document.getElementById("app");
if (element === null) throw new Error("Missing application host");
const root = createRoot(element);
root.render(createElement(StrictMode, null, node as ReactNode));
Object.defineProperty(globalThis, "__presentationDemo", {
    value: { unmount: () => root.unmount(), demo },
});
