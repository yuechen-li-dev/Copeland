# Copeland tables

Compiler-projected relations are read-only facts generated from immutable bound data. Layout exposes `layout::Layouts`, `layout::Boxes`, and `layout::Derivations` through the ordinary table tooling.

`layout::Derivations` contains one row per relative immutable row derivation: stable derivation ID, layout and target box IDs, transform, source box ID, normalized read/write field sets, authored order, resolution status, gap/padding, and project-relative source provenance. Mutation commands reject projected relations.

Nested `with` clauses and CSV `derivations` cells contribute identical rows. The source ID for a CSV derivation points to the individual transform call inside its parsed array cell, not to a synthetic CSV-only evaluator.

`layout::Boxes` retains resolved geometry and reports relative-derived fields through its normalized inspection constraints. The derivation row remains the canonical provenance; geometry is not merely replaced by a display string.
