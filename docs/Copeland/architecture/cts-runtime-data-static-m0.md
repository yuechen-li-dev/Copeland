# CTS-RUNTIME-DATA-STATIC-M0 architecture record

**Status:** honest stop after the runtime-array slice (2026-08-27)

## Product law

Copeland should prefer the semantics a TypeScript programmer would reasonably
expect if JavaScript compatibility were not constraining the design: familiar
syntax, fewer footguns, stronger invariants, and explicit escape hatches. New
syntax is justified only when it makes a semantic distinction visible.

The migration story is intentionally short: Copeland makes mutation explicit,
has no ambient null, and can eventually evaluate pure code at compile time.

## Baseline and audit

The milestone started from clean `main` at `3f25f73`, with .NET SDK `10.0.302`.
The baseline solution test passed (1,646 tests across the reported projects),
and `git diff --check` was clean.

The stated array baseline was historical. Production Copeland already had
homogeneous `T[]` literals, nested array types, typed indexed reads, `.length`,
`for...of`, backend-neutral MIR, deterministic checked bounds, and C#/JavaScript
realizations. TSON already projects finite homogeneous arrays through ordinary
array MIR and has bounded parsing plus canonical encoding plans.

Template static evaluation remains deliberately separate. It consumes
`BoundTemplateValue`/`BoundTemplateStatement` plans and admits only template
intrinsics, immutable locals, static branches/matches/loops, and template calls.
Runtime function bodies use the ordinary bound-expression graph, which currently
has no effect or static-safety classification and no post-binding constant-value
replacement phase.

## Runtime arrays

`T[]` is an immutable semantic sequence. Literals are finite, ordered, dense,
homogeneous, and evaluated left to right. Indexed read requires `int`; `.length`
returns `int`; `for...of` preserves element type. A statically negative index is
rejected. A dynamic out-of-range read traps deterministically with
`Copeland array index is out of bounds.` It never yields `undefined`.

Indexed writes to `T[]` remain a compile-time `COPE-ARRAY-0004` error. JavaScript
and C# continue to use physically mutable native arrays for ordinary literals,
but Copeland source exposes no write operation on them; a backend carrier does
not define the source law. Values crossing a future untrusted host boundary need
a separate defensive-copy/freeze boundary rather than changing source typing.

`MutableArray<T>(length)` is the explicit fixed-length computational carrier.
Its type spelling is `MutableArray<T>`. It supports construction with a
non-negative `int` length, checked indexed read/write, `.length`, `for...of`, and
`freeze()`. M0 construction accepts numeric and boolean elements, the types with
deterministic null-less defaults. `freeze()` returns a copied immutable `T[]`, so later buffer writes
cannot mutate the snapshot. This is fixed storage, not the JavaScript Array
prototype: it has no sparse writes, shape mutation, `push`, or prototype surface.

C# realizes mutable storage as zero-initialized `T[]` plus checked compiler-owned
get/set helpers. JavaScript realizes it as a dense initialized array and emits
checked get/set operations. JavaScript immutable snapshots are copied and frozen.
Both backends use the same mutable-array MIR nodes.

Current numeric storage is honest about the existing language: `int` is signed
32-bit on C# and checked as integral in JavaScript; `float`/`number` is binary64.
Copeland does not yet define `u8`, `u32`, `f32`, or `f64` source types, so this
slice does not pretend generic JS arrays are typed numeric storage. A byte-buffer
story depends on the numeric-types milestone described below.

## Runtime JSON decision and blocker

The intended API remains schema-directed:

```ts
const result: User ! JsonParseError = JSON.parse<User>(text);
const text: string ! JsonWriteError = JSON.stringify(user);
```

Typed objects must become ordinary nominal records; typed arrays must become
ordinary immutable arrays; declaration order is serialization order; unknown
fields should be ignored by default, while strict mode may be added explicitly.
Numbers must be range checked against their target type without wrapping.
Host exception strings are not semantic errors. A dedicated single-pass parser
should enforce input, depth, array-length, string-length, and node limits and
project directly into the known schema.

This cannot be implemented coherently on the current optional model. The parser
explicitly rejects optional record fields and instructs users to model absence
with nominal payload enums. There is no compiler-owned `Option<T>`/`None` value
that can distinguish a missing field from a field present with JSON `null`.
Adding JSON first would force ambient null, collapse missing and null, or invent
a JSON-only record representation; all three violate this milestone's guardrails.
Untyped `JsonValue` is therefore not implemented.

The next prerequisite is a small nominal `Option<T>` milestone with a precise
record-field default/absence law. Runtime JSON should follow it and reuse TSON's
limits, diagnostic model, schema traversal, and record/array construction, but
not the full TypeScript parser or a generic DOM.

## General static decision and blocker

The intended single spelling is expression-oriented:

```ts
const Kernel = static buildKernel(5);
```

`static` means evaluate during compilation. A pure ordinary function should be
callable at runtime and at compile time when its inputs are known and every
operation is static-safe. Values should include booleans, numbers, strings,
records, arrays, enums, branches, matches, bounded loops, and pure calls.
Mutable temporaries may be useful inside evaluation, but only an immutable value
may cross the static/runtime boundary in this milestone.

The existing template evaluator cannot safely be relabeled or lightly extracted
for this purpose. Its safety comes from consuming a template-only bound algebra
that has no runtime calls by construction. Ordinary bound calls may include CLR,
JavaScript-host, npm, async, transport, and other runtime operations; ordinary
functions carry no effect summary. There is also no post-static MIR boundary at
which an evaluated value replaces an expression for every backend.

A sound implementation therefore requires, in order:

1. a backend-neutral effect/static-safety classifier for ordinary bound functions;
2. a bounded value evaluator with step, call-depth, loop, allocation, and value-size budgets;
3. source-located static call stacks and `COPE-STATIC-*` diagnostics;
4. a post-binding rewrite that replaces static expressions with immutable bound values before MIR;
5. literal/readonly-data emission shared by all backends.

Implementing syntax alone or executing the expression independently in each
backend would be a brittle semantic fork, so neither was done.

## Dogfood and parity

The mutable-array kernel fills five `int` cells with squares, freezes a snapshot,
mutates the live buffer, and returns the sum of the old and new cell. It returns
`103` under Node/V8 and generated C#. This proves fixed-length construction,
indexed writes, reads, length, snapshot isolation, and backend parity through the
real compiler path. Existing ordinary-array tests cover indexing, length,
iteration, ordering, nested arrays, and checked failure.

Static convolution, typed JSON round-trip, JSON arrays/null, NativeAOT JSON, and
the M71d JSON/static lanes remain blocked by the prerequisites above. The
primitive-array loop and mutable convolution-style storage are now representable.

## Outcome

Outcome C — honest stop. The runtime-array capability converged and is retained.
Continuing JSON without `Option<T>` would violate null-less semantics; continuing
general `static` without ordinary-function effect classification would either
admit nondeterministic host effects or duplicate evaluation per backend. The next
milestone should be **CTS-OPTION-EFFECTS-M0**: compiler-owned `Option<T>` plus
ordinary-function effect/static-safety summaries. Runtime JSON and post-static
evaluation can then land as separate, reviewable milestones on those foundations.
