# Copeland TS sidecar interop: CTS-SIDECAR-M0a design

**Status:** accepted documentation-only architecture and audit. This milestone specifies no production syntax or runtime behavior.

## 1. Problem statement

Copeland’s C# and JavaScript outputs execute in distinct runtime domains. They do not share ordinary objects, CLR references, JavaScript prototypes, captures, or memory. One deployment may package both partitions, but the only boundary is an explicit generated typed bridge over TSON-derived transport.

```text
one Copeland application
├── CLR partition -> generated C# -> .NET / later NativeAOT-compatible host
├── browser or Node partition -> generated JavaScript
└── generated proxy, dispatcher, and codec plans -> framed transport
```

JavaScript at a browser boundary is a generated device driver, not the application’s semantic center. The precedent is Octxiliary’s separately compiled typed transport, not shared-memory or direct-object interop.

## 2. Octxiliary evidence

The read-only local Oct authority was `C:\Users\yuech\source\repos\oct` at `b07c8849efa00fe0455e827e9a162856f389878f` on clean, synchronized `main`. Current implementation authority is `internal/octxiliary/protocol.go` and public helper `pkg/octxiliary/octxiliary.go`, with `internal/octxiliary/protocol_test.go` and `pkg/octxiliary/octxiliary_test.go`. `internal/octxiliary/rust-sdk/src/lib.rs` and `examples/ChimeraOctxHello/rust-sidecar/src/main.rs` are later binding evidence. `docs/internal/octxiliary*.md`, `chimera_octx_rust_sdk_design_m1.md`, and `uibridge_octagon_transport_recon_uib1.md` contain history/proposals, not replacement authority.

Current Octxiliary is host/client-oriented. The host starts a process, exchanges `OCTWRAP\0` plus ABI major/minor, then little-endian `uint32` length-prefixed textual Octagon-shaped frames. Requests have integer ID, family, function, and legacy or generic arguments; replies retain ID and carry `ok`, a value, or one string error. `Serve` is serial request/response processing: ordering follows processing, with no cancellation, streaming, multiplexing, timeout policy, one-way calls, reentrancy, or bounded frame size. Clean EOF is shutdown; malformed payloads may receive an error reply if an ID was recovered.

The generic `Value` algebra is void/int/float/bool/string, selected arrays, bytes, named ordered-field records, and opaque sidecar-owned handles. It validates finite floats/handles and copies API values; it permits neither schema identities, generated contracts, arbitrary graphs, callbacks, aliasing, cycles, nor shared mutation. The Rust binding proves the value of a small typed dispatcher/codec binding, but also exposes duplicated parser/dispatcher cost.

`.octagon` is not the current sidecar IDL: `examples/Octagon/laser_experiment.octagon` and experiment files are typed serialized artifacts/reports. Sidecar authority comes from wrappers/manifests, family/function strings, and protocol code. Handles and trusted-local-process assumptions are useful historical lessons, but not Copeland M1 foundations.

| Octxiliary lesson | Copeland decision |
| --- | --- |
| Process boundary, handshake, and replaceable framing | Reuse with contract identity and hard bounds. |
| Closed values/direct traversal | Reuse, derived from Copeland nominal schemas. |
| Handwritten family/function dispatch | Adapt to compiler-generated stable operation dispatch. |
| Bespoke unbounded textual parser | Reject; use bounded schema-directed TSON decoding. |
| Opaque handles/resources | Defer. |
| Serial synchronous loop | Reject as source semantics; calls are asynchronous. |

## 3. Copeland implementation audit

`Copeland.TS/Compiler/CopelandCompiler.cs` parses, binds, and lowers one source compilation to Cope MIR; its options contain module/path/assets, not partitions/contracts. `Copeland.Cli/Program.cs` composes MIR-only C# and JavaScript backends and overwrites an explicit `--out` after success; it has no sidecar manifest or stale-output policy. `MirNodes.cs`/`MirValidator.cs` own executable records, enums, Results, arrays, tables, and calls, but no RPC/async node.

TSON has semantic values/schemas, canonical reader/printer, `$schema` identity, assets, and generated direct runtime encoders. Runtime decoding intentionally remains absent; there is no public runtime `TsonValue`. Records/enums are nominal, but `r0`/enum/table ordinals and generated tokens are compiler-local, not wire identity. Tables are declaration-ordered columnar carriers; rows and columns are runtime views. Generated JavaScript uses private tokens/symbol slots/provenance rather than dynamic property discovery. Classes are excluded from TSON and must project to public record DTOs; callables and captures cannot cross.

The JavaScript backend has Node execution proof only. There is no browser/WebView/Node packaging ABI, async law, module system, target partition declaration, transport/process runtime, sidecar codec plan, or NativeAOT validation.

## 4. Source contract and partition model

Tentatively declare a contract, not an implementation selection:

```ts
sidecar Browser {
    render(model: ViewModel): RenderReceipt ! RenderError;
}
```

`sidecar` belongs to the future module owning DTO schemas. Build/project configuration later binds CLR host and JavaScript guest implementations; it does not select Node/WebView/endpoint in source. M1 is one direction, CLR host -> JavaScript guest. Host/guest means transport ownership, not semantic ownership. Reverse calls, notifications, and reentrancy are deferred.

Calls are intrinsically asynchronous. CTS-ASYNC must establish the computation/cancellation law first; CTS-SIDECAR-M1 consumes it rather than publishing a temporary blocking API. No one-way notification belongs in M1.

## 5. Transport algebra and TSON decision

M1 permits only Boolean, Number, String, nominal records, payload enums after nominal-union canonicalization, arrays, and declaration-ordered columnar tables. Tables are root-only in M1; nested tables are deferred. Classes, structural objects, Results as data, callables/captures/functions/environments, table rows/columns, `null`, and `undefined` are rejected. No identity, aliasing, cycles, shared mutation, reflection, property enumeration, or row-object conversion crosses. Classes require explicit DTO projection.

M1 chooses canonical textual TSON over byte framing with **generated schema-directed textual decoders**. Handshake validates stable contract/schema identity; generated decoders accept only canonical emitted subset and construct expected carriers by direct field/case/column plan. This creates neither a public/general `TsonValue` nor a second language frontend. It preserves authored `.obj.ts`/`.tson` reuse of the TypeScript parser because transport data is generated runtime data, not authored documents. Binary TSON and handshake-then-compact schema-directed values remain later replacements beneath the same plans.

## 6. Failure, lifecycle, identity, and generation

`T ! E` remains declared application reply. Peer unavailable/closed, timeout, cancellation, malformed frame, incompatible protocol/schema, and resource-limit failures are compiler-owned transport failures, not `E` or CLR/JavaScript exceptions. CTS-ASYNC should provide a compiler-owned `SidecarResult<T, E>`-style observation that separates declared error and transport failure without user-written nested Results; exact spelling is unresolved. Counterfeit carriers, impossible message kinds, and trusted generated-protocol violations are terminal invariant failures.

M1 requires host-owned Node launch, handshake/health, mismatch refusal, bounded timeout, stdin-close/await-exit/kill-after-grace shutdown, and failure of all pending requests on crash. It supports up to 64 correlated concurrent host requests with nonzero unsigned 64-bit IDs and out-of-order responses. Cancellation before dispatch/local pending state is required; no cancel frame is sent. Streaming, peer calls, reentrancy, callbacks, backpressure protocol, and distributed lifecycle are deferred.

The compiler owns sidecar symbols, operation/schema plans, generated client proxy, generated server dispatcher, envelope codec, and value codecs. Sidecar/operation identities are readable module-qualified URIs plus SHA-256 semantic-contract digests. Request/success/error schemas reuse `$schema` identity plus canonical-schema digest; protocol version is independent. Declaration order affects schema form but no local allocation ordinal or compiler version is durable identity. Cope MIR retains ordinary calls plus only a small sidecar-call lowering plan required by async lowering; it must not become universal RPC IR.

## 7. Transport, packaging, security, and scope

M1 is CLR <-> Node over redirected standard input/output: fixed binary preface, fixed little-endian 32-bit byte-length, UTF-8 canonical TSON envelope. The .NET host owns arguments, environment, stderr, timeouts, and cleanup. A single PE/native executable can contain the CLR host only; JavaScript is not CLR IL/machine code. It may be embedded/extracted, adjacent, or loaded as text—each differs from one application bundle/deployment. NativeAOT is a route, not validation. Browser follow-up reuses contracts/plans over WebView messages, WebSocket, worker/message-channel, or WASM bindings after a browser-host decision.

Defaults: 1 MiB frame; 64 in-flight requests; 64 operations; 64 KiB handshake/schema metadata; 32 nesting; 16,384 value nodes; 256 KiB string; 16,384 array values; 256 columns; 16,384 rows; 1,048,576 table cells; 30-second default timeout. Decode iteratively or with explicit depth bounds. Treat generated Node as trusted for invariant classification but its bytes as malformed input: reject before allocation, terminate session on unknown/mismatched contract/protocol, and fail pending calls on crash. Browser later additionally needs origin/navigation/message-source/capability validation.

M1 implements async integration, one module-local host-to-guest contract, stable identities, generated C#/JavaScript proxy/dispatcher/codecs, Node stdio lifecycle, declared/transport/invariant failures, the closed algebra, CLI manifest/stale-output ownership, and integration fixtures. It defers host objects, shared memory/identity, handles, callbacks, streaming, bidirectionality, reflection, JSON, generic/open schemas, public `TsonValue`, package publishing, CLR imports, DOM/Machina integration, and NativeAOT performance claims.

## 8. Owner decisions

Ratify `sidecar` spelling, CTS-ASYNC surface, module/project partition binding, root-table inclusion, URI/rename policy, configurable bounds/timeouts, Node artifact embedding, and later browser transport. None permits implementation in M0a.
