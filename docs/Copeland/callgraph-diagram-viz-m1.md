# Call metadata to Diagram — VIZ-M1

## Explicit projection

The standard adapter keeps reflection and visualization separate:

```ts
const calls = reflect callsOf<CompileWorkspace>();
return callGraphDiagram(calls);
```

`callsOf` returns semantic call-site data, never a `Diagram`.
`callGraphDiagram` maps that data to the existing VIZ-M0 `Diagram` IR, and the
unchanged `MermaidEmitter` maps the IR to `flowchart LR` source.

## Mapping and ordering

The adapter emits one root caller node, one node per distinct stable callee,
and one edge per caller/callee identity pair. Semantic node IDs are
`callable:<CallableIdentity.Id>`. Multiple source sites aggregate at this layer;
an edge receives `×N` when `N > 1`. Reflected metadata itself is never
deduplicated.

Resolved external targets are ordinary distinct callee nodes with stable
`external:` identities. Dynamic records with no callee stay visible in the
metadata but are omitted by the standard M1 projection; this is an explicit
projection policy, not silent reflection loss.

Diagram construction normalizes nodes by semantic ID and edges by source ID,
target ID, and label. Mermaid then assigns backend-local `n0`, `n1`, and so on.
Thus source metadata preserves call-site order while graph/backend output is
canonical and byte deterministic.

Labels use the callable `name` when unique. If two distinct targets share a
name, the adapter uses `displayName` for those nodes. Full signatures remain in
metadata rather than cluttering every node.

## Dogfood graph

The maintained source is
`samples/copeland-ts/visualization/CallGraph.tsx`. Its ordinary
`CompileWorkspace` function directly invokes parsing, binding, lowering,
backend emission, and artifact writing functions. `BindModules` is called
twice and itself calls `ValidateImports`; the reflected root correctly contains
two `BindModules` sites and does not include the transitive `ValidateImports`
call.

The checked-in artifacts are:

- `samples/copeland-ts/visualization/artifacts/callgraph-diagram.mmd`
- `samples/copeland-ts/visualization/artifacts/callgraph-diagram.png`

They are produced through `tscl template materialize` and the same pinned local
`@mermaid-js/mermaid-cli@11.16.0` path qualified by VIZ-M0. No second renderer,
layout engine, native SVG backend, or Mermaid architecture was added.

## Limitations

VIZ-M1 is a direct call graph, not a reachability graph. The adapter needs at
least one call site (`COPE-DIAGRAM-0004`); a zero-call function has valid empty
reflection metadata but no standard one-argument diagram root in M1. Dynamic
targets are not resolved or drawn. External framework internals are not
expanded. Control flow, branch conditions, loops, effects, AST/source text,
profiling, and runtime reflection remain out of scope.
