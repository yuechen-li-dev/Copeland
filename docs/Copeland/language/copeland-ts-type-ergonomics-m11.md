# Copeland TS type ergonomics M11

`type` names and composes compile-time types. Use it when naming a repeated
signature, a type expression, a specialization, or a field requirement makes
the source clearer. A transparent alias does not create an additional data
identity or change the underlying capture, state, or lifetime semantics.

## Authoring roles

| Construct | Role |
| --- | --- |
| `record` | Immutable data with an authored nominal identity and construction rules |
| `enum` | Named alternatives and typed payloads, consumed through `match` |
| `type` | Transparent aliases and finite compile-time structural requirements |
| `interface` | Compatible field-capability constraint syntax |
| `template<type T>` | Explicit specialization or computation involving types |
| `layout type` | The established layout topology/slot contract |

The existing `type Event = Open | Close` form remains an exception to transparent
aliasing: direct nominal record alternatives create a tagged enum. Its identity,
construction, matching, and serialization follow the canonical enum semantics.
It is not a general TypeScript union or an independently implemented union algebra.

## Reusable frontend signatures

```typescript
type EventSink<Event> = (event: Event) => void;
type Projection<Item, Output> = (item: Item) => Output;
type SaveResponse = string ! string;
type ChildViews = View[];

template<type T>
record EditorProps {
    value: T;
    change: EventSink<T>;
}

type TextEditorProps = EditorProps<string>;

function Edit(props: TextEditorProps): void {
    props.change(props.value);
}
```

Aliases to callables retain exact signatures and explicit-capture requirements.
A callable stored in a declared record, class, table-row, or constrained generic
field uses ordinary invocation. It does not gain an implicit receiver/`this`
argument. The receiver is evaluated once, then arguments are evaluated in order.
Class privacy and callable argument checks remain in the canonical binder paths.

Aliases may be exported and imported with `import type`. They do not introduce a
runtime alias binding. `type UserId = string` remains interchangeable with
`string`; use a nominal record when a distinct domain identity is required.

## Structural field requirements

```typescript
type HasValue<Value> = { value: Value; };
record Item { value: int; label: string; }

function Read<T extends HasValue<int>>(item: T): int {
    return item.value;
}

function Forward<T extends HasValue<int>>(item: T): int {
    return Read<T>(item);
}
```

Ordinary functions, host template functions, generic records and aliases, and
artifact templates normalize structural aliases through the same field
requirement binder used for interfaces. Generic alias fields are substituted
before checking satisfaction. Extra fields are accepted; required field types
must be exactly equivalent. Mixed interface/shape constraints, forwarding,
conflicts, duplicates, and resource limits use the existing requirement rules.

Artifact templates retain their earlier ability to use a named record directly
as a field requirement source. Ordinary host generics require an interface or a
structural alias; a nominal record annotation still denotes its own identity.
Optional/read-only projection metadata does not add optional runtime storage or
alter the existing readable-field requirement law.

## Explicit boundaries and repairs

A structural alias remains a compile-time description, without its own runtime
carrier. `function Read(props: { ... })` via a named structural alias and
contextual literal construction against that alias remain unsupported. Use a
named record for concrete runtime parameters and returns, or use a constrained
generic for a field-reading operation. Runtime shape annotations remain the
separate named-shape qualification decision in the record-shape design.

Primitive unions require an enum with payload cases; literal choices require
named enum cases. The parser reports one primary union diagnostic rather than a
chain of missing-alternative errors. `keyof` points to explicit template
reflection; conditional types remain explicitly unsupported. Local aliases
receive a module-scope repair without general parse cascades.

## Dogfood and validation

The shared React/Vue issue page names event sinks, projections, predicates,
selection, and child-view collections. Its generic `IssueLabel<T extends Titled>`
uses a shape requirement; `ChoiceRow` now calls `spec.text(item)` directly rather
than extracting a temporary callable. The mixed-island page names save responses
and child-view collections. Records and enums continue to own the application data.

Run compiler, browser, legacy regression, and full solution qualification:

```powershell
pwsh -NoProfile -File samples/copeland-ts/mixed-islands-m10/run.ps1 -FullValidation -EvidenceName type-ergonomics-m11
```

This writes a separate evidence snapshot and preserves the historical M10
reports. Compiler tests cover positive/negative shape constraints and callable
fields. Backend parity executes captured callable fields and generic forwarding
in generated C# and Node, including single evaluation of a receiver.
