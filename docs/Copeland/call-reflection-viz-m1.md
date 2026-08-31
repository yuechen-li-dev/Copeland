# Bounded call reflection — VIZ-M1

## Syntax and semantic boundary

VIZ-M1 adds one executable semantic query:

```ts
const calls = reflect callsOf<CompileWorkspace>();
```

The generic-looking argument position is a dedicated callable reference for
this query. It resolves a directly named Copeland `FunctionSymbol`; it is not a
runtime type argument. Names that do not resolve, values and records, and other
non-callables produce `COPE-REFLECT-0007`. Runtime use is rejected by the
existing `COPE-REFLECT-0001` boundary before MIR.

`reflect` is intentionally the hazard marker. `callsOf` exposes semantic
implementation relationships across an abstraction boundary and should be
used deliberately. It is never inferred from an arbitrary template.

## Callable identity

The compiler reuses `FunctionSymbol.StableIdentity` and exposes a typed,
backend-independent projection:

```text
CallableIdentity(
    Id,
    Name,
    DisplayName,
    Module?,
    ContainingType?,
    ParameterTypes,
    GenericArity)
```

`Id` is the semantic key and `DisplayName` is presentation only. Global source
functions use the existing `function:module:<module>#<name>` project identity
when module ownership exists and `function:<name>` for standalone source.
Associated functions retain their existing class-owned identity. Copeland does
not currently permit same-scope overload declarations, but parameter types and
generic arity are retained for inspection and future disambiguation. No source
position, GUID, object hash, or compiler object identity is used as the key.

## Direct-call and call-site semantics

`callsOf<F>()` returns only call sites directly owned by `F`. It never follows
the callee. If `F` calls `G` twice, reflection returns two records in source
order. The `callGraphDiagram` adapter may aggregate them into one edge.

Each immutable record has this shape:

```text
ReflectedCall {
    caller: CallableIdentity
    callee: CallableIdentity?
    kind: "direct" | "external" | "dynamic"
    source: { path?, startLine, startColumn, endLine, endColumn }
    unresolvedDisplayName?: string
}
```

`direct` is a statically resolved Copeland `BoundCallExpression`. Stable CLR,
npm, and JavaScript-host targets are `external` and receive namespaced external
identities; framework bodies are never traversed. A callable-value invocation
is `dynamic`, retains its source site, and has no invented target. An actually
undefined call remains an ordinary binding error, so template evaluation does
not pretend that a callable exists. VIZ-M1 adds no dynamic dispatch analysis.

Ordering is source path, one-based line and column, then callee identity only
as a deterministic fallback. For one source file this is source call-site
order. Correlation points at the call's opening parenthesis and carries the
compiler input path when one exists. No source-map subsystem was added.

## Bounds and runtime prohibition

One query is limited to 256 direct semantic call-site records and 262,144 bytes
of evaluated metadata. Either limit produces `COPE-REFLECT-0008`. The query has
no traversal-depth setting because it performs no traversal. Existing template
instantiation and static-iteration limits remain independently active.

The summary is produced during binding and consumed only by template
evaluation. It contains semantic identities and scalar source coordinates,
not AST nodes, tokens, source text, CLR `Type`, or symbol handles. It is not
lowered to runtime MIR. VIZ-M1 introduces no `System.Reflection`, `MethodInfo`,
assembly scanning, runtime compiler service, or emitted reflection metadata.

## Diagnostics and future boundary

- `COPE-REFLECT-0001`: any semantic reflection in runtime code.
- `COPE-REFLECT-0002`: unsupported reflection query.
- `COPE-REFLECT-0003`: wrong query arity.
- `COPE-REFLECT-0004`: missing explicit `reflect` marker.
- `COPE-REFLECT-0007`: unresolved, indirect, or non-callable target.
- `COPE-REFLECT-0008`: call-site or metadata bound exceeded.
- `COPE-REFLECT-0010`: invalid callable stable identity.

Control-flow structure is deliberately absent. A future VIZ-M2 may evaluate a
separate bounded `controlFlowOf<F>()` only if a concrete authoring need cannot
be met by direct call reflection; `callsOf` will not be widened into CFG, SSA,
effects, syntax, or source reflection.
