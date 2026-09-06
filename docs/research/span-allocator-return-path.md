# Span allocator return path

M15 is a graphical, finite-span experiment, not a byte allocator. The useful carry-forward is the separation between ordered allocation policy, inspectable resolution, diagnostics, and domain-specific realization.

| Concept | M15 | Future extension | Visually simulatable | Safe to test graphically first |
|---|---|---|---|---|
| Contiguous spans | Yes: offset/length placements | Preserve | Yes | Yes |
| Fixed requests | Yes | Preserve | Yes | Yes |
| Flexible requests | Minimum + integer weight only | Preferred/max/caps if demanded | Yes | Yes |
| Alignment | No | Required for byte/page work | Yes | Yes, but semantics must precede UI |
| Holes | No | Free-list/domain model | Yes | Yes |
| Fragmentation | No | Derived metric over holes | Yes | Yes |
| Relocation | No | Requires identity and move contract | Yes | Partly; correctness is not visual |
| Pinning | No | Requires relocation owner | Yes | Partly |
| Priority | No | Optional/reclaimable policy | Yes | Yes |
| Lifetime groups | No | Requires temporal model | Yes | Partly |
| Ownership/borrowing | No | Separate language/runtime authority | Only as projection | No; must be proved semantically |

The safe return path is: first add alignment as an isolated request/result law with deterministic tests and a strip visualization; then add holes and fragmentation as a separate allocator model. Do not reuse sprite sampling, panel metadata, or Machina types. Pinning, relocation, lifetime, and borrowing wait until a real systems consumer establishes their authority and failure rules.
