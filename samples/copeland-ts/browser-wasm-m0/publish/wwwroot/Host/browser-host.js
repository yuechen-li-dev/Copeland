import { dotnet } from "../_framework/dotnet.js";

const incrementEvent = 0;
const resetEvent = 1;
const workloadIterations = 100000;
const boundaryIterations = 10000;

const countElement = requireElement("count");
const incrementButton = requireButton("increment");
const resetButton = requireButton("reset");
const workloadElement = requireElement("workload");
const boundaryElement = requireElement("boundary");

const start = performance.now();
const runtime = await dotnet.create();
const exports = await runtime.getAssemblyExports("Copeland.Browser.Wasm.M0.dll");
const bridge = exports.Copeland.Browser.Wasm.M0.BrowserBridge;

function render(snapshot) {
    countElement.textContent = snapshot;
}

function dispatch(eventDiscriminant) {
    render(bridge.Dispatch(eventDiscriminant));
}

render(bridge.Initialize());
incrementButton.addEventListener("click", () => dispatch(incrementEvent));
resetButton.addEventListener("click", () => dispatch(resetEvent));

const workloadStart = performance.now();
const wasmChecksum = bridge.RunWorkload(workloadIterations);
const workloadElapsedMs = performance.now() - workloadStart;
const nativeWorkloadStart = performance.now();
const nativeChecksum = runNativeWorkload(workloadIterations);
const nativeWorkloadElapsedMs = performance.now() - nativeWorkloadStart;
const chattyBoundaryStart = performance.now();
for (let index = 0; index < boundaryIterations; index += 1) {
    bridge.Dispatch(incrementEvent);
}
const chattyBoundaryElapsedMs = performance.now() - chattyBoundaryStart;
bridge.Dispatch(resetEvent);
const coarseBoundary = bridge.MeasureBoundary(boundaryIterations).split(":");
const startupElapsedMs = performance.now() - start;
if (wasmChecksum !== nativeChecksum) {
    throw new Error(`Copeland workload mismatch: WASM=${wasmChecksum}, JS=${nativeChecksum}.`);
}
workloadElement.textContent = `workload: ${wasmChecksum}; wasm ${workloadElapsedMs.toFixed(3)} ms; js ${nativeWorkloadElapsedMs.toFixed(3)} ms; startup ${startupElapsedMs.toFixed(3)} ms`;
boundaryElement.textContent = `boundary: ${boundaryIterations} calls ${chattyBoundaryElapsedMs.toFixed(3)} ms; one coarse call ${coarseBoundary[1]} ms; checksum ${coarseBoundary[0]}`;

window.copelandWasmM0 = {
    bridge,
    workloadIterations,
    wasmChecksum,
    workloadElapsedMs,
    nativeChecksum,
    nativeWorkloadElapsedMs,
    boundaryIterations,
    chattyBoundaryElapsedMs,
    coarseBoundaryChecksum: Number(coarseBoundary[0]),
    coarseBoundaryElapsedMs: Number(coarseBoundary[1]),
    startupElapsedMs,
    dispatch,
};

function requireElement(id) {
    const element = document.getElementById(id);
    if (element === null) {
        throw new Error(`Copeland browser host could not find '${id}'.`);
    }

    return element;
}

function runNativeWorkload(iterations) {
    let checksum = 0;

    for (let index = 0; index < iterations; index += 1) {
        checksum = (checksum * 31 + index * 17 + 7) % 1000003;
    }

    return checksum;
}

function requireButton(id) {
    const button = document.getElementById(id);
    if (!(button instanceof HTMLButtonElement)) {
        throw new Error(`Copeland browser host expected button '${id}'.`);
    }

    return button;
}
