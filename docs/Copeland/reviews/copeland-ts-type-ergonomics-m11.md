# Copeland TS type ergonomics M11

Outcome: success for the bounded alias/constraint/callable-field pass. The shared
React/Vue issue page now uses named event/projection signatures and a structural
label requirement. The mixed-framework page uses named result and child-view
types. Both pages continue to work through their real compiler and browser paths.

## Implemented

- Ordinary generic functions, generic records and aliases, and artifact templates
  normalize structural aliases through the existing requirement binder. Closed
  generic shape aliases and finite projections substitute their field types;
  imported shapes, dependent type arguments, and forwarding remain explicit.
- Declared data fields can be invoked when their type is callable. The existing
  field access and callable invocation paths retain privacy, exact argument
  types, explicit capture, and receiver evaluation once. A rejected field access
  does not cascade into an additional invocation-of-error diagnostic.
- Primitive/literal union attempts, `keyof`, and local aliases have deliberate
  repairs. Named-record union shorthand retains the existing tagged-enum law.
- `EventSink`, `Projection`, `Predicate`, `IssueSelection`, `ChildViews`, and
  `SaveResponse` replace repeated type expressions in the demos. `ChoiceRow`
  invokes `spec.text(item)` directly. `IssueLabel<T extends Titled>` states its
  readable-field requirement and is explicitly closed where passed as a value.
- Qualification accepts a separate evidence name, preserving the historical M10
  evidence snapshot while reusing the existing fixtures and checks.

## Qualification

The initial complete runner invocation was:

```powershell
pwsh -NoProfile -File samples/copeland-ts/mixed-islands-m10/run.ps1 -FullValidation -EvidenceName type-ergonomics-m11
```

A final adjustment removed a secondary diagnostic after rejected callable-field
access, with a regression asserting one primary error. An additional project
test qualified imported generic projection shapes with dependent type arguments.
After those changes, the compiler drivers were rerun and both complete compiler
reports matched the browser-qualified reports byte for byte. The entire solution
build/test was rerun and the validation manifest was refreshed against the final
compiler assemblies and logs.

- Full `Copeland.slnx` build: zero warnings and errors.
- Full solution tests: 2,277 passed, zero failed or skipped across eight suites.
  This includes 20 new compiler cases and one new C#/Node execution parity test.
- Mixed page: 270 browser observations with no diagnostics, across React/Vue
  outer targets, three emission profiles, and widths 1280, 768, and 390.
- Shared source: 192 browser observations with no diagnostics, across both
  framework targets and all three profiles, plus the idiomatic Vue baseline.
- Preserved React regressions: seven state/on scenarios and twelve effects/child
  scenarios in each profile, plus layout/text and semantic-library workflows.
- Generated C# and Node execution both return the expected result for captured
  callable fields, generic forwarding, immutable `with`, and a receiver whose
  evaluation count is observable.

Evidence is in `evidence/type-ergonomics-m11/validation.json`, with compiler,
browser, shared-source, and React regression reports alongside it. Source and
artifact hashes identify the actual qualification inputs. The inner reports
retain their fixture identities M9/M10; the enclosing evidence name identifies
this subsequent pass.

## Ergonomics assessment and limits

Aliases earn their place when they name repeated application contracts. A reader
can recognize an event sink, projection, or save response without unpacking the
same function/result type repeatedly. Direct callable-field invocation removes
an extraction that existed only to accommodate dispatch classification.

A generic field requirement is useful for code that reads a shape without
claiming nominal data identity. The callback binding now spells `IssueLabel<Issue>`
because generic function values require explicit closure. That is a deliberate
remaining cost, visible at the binding rather than hidden in the implementation.

Runtime structural props annotations and construction remain outside this pass.
Concrete public values still use records; structural aliases describe compile-time
requirements. Opaque aliases, primitive/literal union value types, conditional
types, and general TypeScript mapped/indexed type machinery were not introduced.
These checks qualify the existing React/Vue light-DOM demos, not additional
framework bridges, hydration, or a general framework certification claim.

The authoring law and examples are in
`../language/copeland-ts-type-ergonomics-m11.md`.
