import { createElement, StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
// @ts-expect-error Compiler-generated ESM is the fixture's public application boundary.
import { Cafe } from "@cafe";
// @ts-expect-error Public application exports, never private table carriers.
import { applicationProbe, baselineCents } from "@application";
// @ts-expect-error Public reducer probe compiled from the same source as the UI.
import { rowsProbe, totalLimitProbe } from "@editor";
// @ts-expect-error Generated public state reducer probe.
import { editLimitProbe } from "@row-editor";

const host = document.getElementById("app");
if (host === null) throw new Error("Missing application host");
const root = createRoot(host);
root.render(createElement(StrictMode, null, Cafe() as ReactNode));
Object.defineProperty(globalThis, "cafeProof", {
    value: { applicationProbe, rowsProbe, totalLimitProbe, editLimitProbe, baselineCents, unmount: () => root.unmount() },
});
