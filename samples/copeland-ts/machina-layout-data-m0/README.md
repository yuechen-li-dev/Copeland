# Machina layout-data M0 invariants

Each numbered fixture proves one public language invariant. These fixtures use
the `layout` declaration model only; they deliberately do not use `HStack`,
`VStack`, `Fixed`, or `Fill`.

`08-cross-module` proves ordinary exported layout import/alias resolution.

All canonical declarations state a required local root origin. `09-origin-px`
and `10-origin-ui` prove the two coordinate units; `11-origin-composition`
proves a derived declaration establishes its own root origin. Invalid
origin diagnostics are exercised by the compiler test corpus so the canonical
fixture tree itself remains buildable.
