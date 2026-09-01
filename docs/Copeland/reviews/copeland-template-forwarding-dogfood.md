# CTS-TEMPLATE-M2 forwarding dogfood

## Burn-in repair

The Metaprogramming burn-in previously specialized the nested inventory with a
concrete type:

```ts
emit(instantiate TypeInventory<Worker>);
```

It now keeps the natural abstraction:

```ts
template<type T extends Named = Worker, static label: string, static includeWorker: boolean> LabeledInventory: ProjectTree {
    static if (includeWorker) {
        emit(instantiate TypeInventory<T>);
    }
}
```

`BurnInMetadata` instantiates that outer template with `Worker`. The evaluator
then substitutes `Worker` through the nested call. Repeated evaluation remains
byte deterministic and produces the same 15 artifact paths, sizes, and SHA-256
hashes. The duplicate `Label<value: "same">` output remains byte-identical and
the run retains seven realized instantiation-chain entries.

## Constraint matrix

| Outer evidence | Inner requirement | Result |
| --- | --- | --- |
| `Named` | `Named` | allowed |
| `Named & Versioned` | `Named` | allowed |
| `Named` | `Named & Versioned` | `COPE-REQUIREMENT-0011` |
| none | `Named` | `COPE-REQUIREMENT-0011` |
| structural alias `{ name: string }` | equivalent structural alias | allowed |
| concrete record, pure class, or `Table.Row` satisfying `Named` | `Named` | allowed |

The nested `Outer -> Middle -> Inner` case evaluates `fieldsOf<T>()` and
`nameOf<T>()` as the concrete record. No compiler-private name escapes. The
table-row case uses `nameOf` because `fieldsOf<Table.Row>` is outside the
existing reflection surface and was not broadened here.

## Negative evidence

Weaker-to-stronger forwarding reports one diagnostic at `Inner<T>`:

```text
COPE-REQUIREMENT-0011: Template argument 'T' does not satisfy constraint
'Named & Versioned' required by 'Inner': missing version: int. Known
constraints for 'T': Named.
```

An unconstrained parameter reports the same focused ID with known constraints
`none`. Concrete outer defaults continue through ordinary satisfaction, so a
primitive cannot acquire structural permission merely by crossing a template.

## Compile-time impact

The maintained coarse warmed measurement moved from 18.9027 ms template time
to 18.9942 ms, a +0.0915 ms (+0.48%) single-run delta. Bind time moved from
10.0610 ms to 15.2005 ms; total compile time moved from 38.4407 ms to 46.1313
ms. These are coarse, non-benchmark signals. Artifact count (15), realized
instantiation-chain entries (7), and reflection query sites (5) are unchanged.
Constraint-check count is not exposed by the existing tool, so no profiler or
counter framework was added.

## Remaining friction

This milestone deliberately leaves generic structural aliases, new reflection
categories, anonymous type argument spelling, and module-system expansion
unchanged. The next evidence-backed burn-in action is the separate table-runtime
JavaScript size investigation.
