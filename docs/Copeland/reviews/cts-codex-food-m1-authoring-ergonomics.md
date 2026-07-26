# CTS-CODEX-FOOD-M1 — Copeland authoring ergonomics pass

## What I built

`samples/copeland-ts/authoring-food` is a small meal-prep console application
in an ordinary SDK project. It has three Copeland files and a small C# host:

- `RecipeBook.ts` uses an immutable record, a payload enum, `match`, a direct
  same-project CLR call, and a typed `csharp { ... }` migration block.
- `Planning.ts` uses arrays, deterministic `batch`, a lazy generator consumed
  with `for...of`, a transparent alias, a field-only interface requirement,
  inferred generic calls, and explicit generic calls.
- `PantryFlow.ts` supplies a compact typed flow with board state, typed events,
  and a terminal result.
- `KitchenText.cs`, `Program.cs`, and the accompanying xUnit project exercise
  the generated APIs through normal `build`, `run`, `test`, and `publish`
  commands.

The deliberate application shape is revealing: the C# host composes the three
file-module APIs because Copeland has no source-module resolver. This is a
coherent CLR application, but it is not how a TypeScript author naturally
expects several `.ts` files to collaborate.

## The first authoring attempts

I began from the root README, focused feature documents, and the SDK README.
I did not inspect binder implementation until an attempt had failed and the
public material did not teach a correction.

### String + number summary

My initial recipe summary was ordinary TypeScript:

```ts
return highlighted + " serves " + recipe.portions + " for " + recipe.calories + " calories";
```

It produced four `COPE-TYPE-0007` messages saying only `Invalid binary
operands for '+'`. The language profile explains that implicit conversion is
intentionally absent, but it has no source-level formatting/conversion API.
The real correction was to add a typed same-project CLR formatter,
`KitchenText.Describe`, and call it from Copeland.

This is an intentional semantic restriction, not a request to recover
JavaScript coercion. The original diagnostic was poor: it did not name the
operand types or a viable repair. It now says which operand types were found,
states that Copeland performs no implicit conversions, and directs the author
to a typed CLR formatting API. The workaround is still ceremonious for basic
console application text; explicit source conversions or a narrow standard
formatting surface are a candidate milestone, not an ergonomics-pass patch.

### Interface as a normal TypeScript parameter

I naturally wrote:

```ts
interface HasPortions { portions: number; }
function PortionCount(plan: HasPortions): number { return plan.portions; }
```

The first diagnostic was accurate but incomplete: `Interface 'HasPortions' is
a requirement and cannot be used as a storage type.` It was followed by the
unhelpful cascade `Field access requires a record receiver, got 'error'.`

The supported correction is more C#-like than TypeScript-like:

```ts
function PortionCount<T extends HasPortions>(plan: T): number {
    return plan.portions;
}
```

That form worked with both `PortionCount(plan)` inference and
`PortionCount<PortionPlan>(plan)`. The revised primary diagnostic now includes
the exact generic-constraint shape, and the binder suppresses the inevitable
secondary field-access error. This is an intentional erased-requirement law,
not a bug. It needs a short, prominent “interfaces are constraints, never
values” example in the SDK-facing documentation.

### Relative import

I then tried the obvious composition form:

```ts
import { BuildDailySummary } from "./Copeland/RecipeBook";
```

Before the diagnostic fix it was treated as an unconfigured npm package:
`COPE-NPM-0001 npm package './Copeland/RecipeBook' is unavailable in project
configuration`, followed by an undefined-function error. That is misleading:
the issue is not package configuration but the absence of local source modules.

The repaired diagnostic is `COPE-MODULE-0001`: it names relative imports,
says that no source-module resolver exists, and distinguishes the available
alternatives—one Copeland file, C# composition of generated file modules, or a
declared npm contract. The actual missing feature remains deliberately
deferred. It is the single largest TypeScript-shaped authoring gap exposed by
this application.

## What felt natural

Contextual immutable record literals were easy to write once the nominal type
was already present. Payload enums plus `match` are pleasant and more explicit
than TypeScript's ad-hoc discriminated-union narrowing. `batch` has a compact,
readable map shape; no scheduling controls leaked into source. Synchronous
generators and `for...of` also read naturally, and `yield return` is a useful
C#-friendly alias rather than needless syntax.

The CLR path is compelling for a C# author. `using Copeland.Authoring.Food`,
direct static calls, constructors, and readable properties all behaved like
normal typed .NET code. The same-project declaration projection worked for
both an ordinary call and the intentionally explicit inline-C# escape hatch.
The latter is conspicuous enough that it feels like a migration seam, not a
hidden second language.

Transparent aliases were unobtrusive in the good way: `type Portions = number`
gave the application a domain name without requiring wrappers. Generic calls
were also good after learning the requirement model. Inference handles the
ordinary concrete-record call, while explicit closing is predictable and
familiar to C# developers.

## Where the language stops feeling obvious

The break is justified for JavaScript truthiness, `any`, `null`, dynamic
objects, implicit coercion, and untyped interop. Those restrictions are part
of Copeland's value proposition and should stay. `match` instead of `switch`
is also defensible for its closed nominal enum model; ordinary source `switch`
is explicitly unsupported and should not be added just for TypeScript parity.

The break is less well explained for:

- Local modules. Several `.ts` files compile, but cannot import one another.
  The C# file-module surface is a workable bridge, not normal TypeScript
  application authoring.
- Interfaces. Their constraint-only meaning is coherent but surprising; the
  most discoverable TypeScript spelling fails.
- Basic formatting. The type system correctly rejects mixed concatenation, but
  an application needs an explicit CLR helper for a simple sentence.
- Flow inspection from C#. `Session.Board` exists, but its record properties
  are backend carrier names rather than authored board field names. The sample
  can inspect `State` and `Revision`, not naturally write `Board.servings`.
  This is a backend/API mismatch and should be fixed only as part of an
  explicit authored flow/session public-surface decision.

## Compiler and integration bugs found

The application was intentionally split across files, which uncovered a real
MSBuild integration defect. Each independently compiled file emitted record
carriers beginning at `__CopeRecord_r1` into the same generated C# namespace.
Two unrelated records therefore produced a duplicate C# type. The task now
scopes private record-carrier names by generated module name. A regression test
builds and runs two independent Copeland files, each with its own first record.

The same exercise exposed two ordinary-project failures:

- The MSBuild incremental stamp keyed compiler assembly version but not the
  locally built task/compiler payload. A source-built SDK could retain stale
  generated C# after a compiler edit. The stamp now fingerprints the task,
  frontend, and C# backend artifacts as well as its previous inputs.
- `batch` emitted uninitialized private test-seam fields. With the repository's
  normal warnings-as-errors policy, the generated C# failed on `CS0649`. The
  fields now have explicit default initializers.

These were compiler/backend bugs, not author mistakes. The private carrier
scoping change is intentionally narrow: other compiler-owned global helper
families should be audited before declaring arbitrary multi-file feature
combinations complete.

## Documentation and diagnostic quality

The best diagnostics encountered were specific language-law messages: strict
equality recommends typed `==`/`!=`, `batch` identifies its restricted body,
and generator diagnostics name `Iterable<T>` and `yield break`. The new
mixed-concatenation, interface, and relative-import diagnostics meet the same
standard better than their predecessors.

The weakest authoring documentation is the top-level discovery path. The root
README is concise and useful but points to feature pages; the large language
profile has stale-looking text that says suspension/iterators/modules do not
exist even though focused generator and flow documents, and the README, present
generators and flows as implemented. The MSBuild decision record also retains
an older statement that same-project C# to Copeland is deferred, while the root
README and current tests demonstrate it. An experienced user cannot reliably
tell which document is current without reading implementation-adjacent history.

`manifest.tsx` and TS-XML were irrelevant to this console application. npm
interop was also not naturally useful: it requires a manifest-owned static
contract and should not be forced into a small CLR app merely to prove a
feature. Neither absence is a language defect.

## Priorities before an adversarial external review

1. Publish one short, current authoring guide beside the SDK package: supported
   declarations, records/enums/match, aliases, interface constraints, generic
   inference, CLR calls, `batch`, generators, flows, and the no-local-modules
   boundary. Reconcile or label superseded decision records.
2. Make source-module absence unmistakable in the overview and keep the new
   relative-import diagnostic. A small module milestone is the largest future
   ergonomics candidate, but it needs a real source/project identity design;
   it should not be improvised here.
3. Decide a narrow explicit formatting/conversion library rather than relying
   on CLR helper classes for every string/number boundary.
4. Design the public Copeland flow/session inspection surface, including
   authored board-field names, before promising C# flow inspection.
5. Audit all per-file generated helper names, not only record carriers, under
   a multi-file ordinary-project corpus.

Larger candidates revealed here are source modules, explicit conversion and
formatting APIs, an authored Option standard type, and a real flow/session
source API. None should be annexed to this ergonomics pass. Async generators,
advanced flows, persistence/replay, React/Blazor/Machina integration, and the
other deferred milestones remain out of scope.

## Verdict

Copeland is learnable for an experienced TypeScript developer only after a
clear orientation to its closed-world laws. The core typed subset is pleasant,
but it currently stops feeling like “obvious, better TypeScript” at local
modules, normal interface parameters, and basic formatting. The semantic
breaks are often justified; the explanations have not consistently been good
enough.

It is more immediately attractive to an experienced C# developer working in a
normal `.csproj`: direct CLR projection, same-project calls, inline C#, and
generic constraints make sense. That audience will still reject the current
flow board projection and multi-file helper fragility if presented as mature.

This pass improved the most actionable diagnostics and integration bugs, but
does not make Copeland ergonomically complete.
