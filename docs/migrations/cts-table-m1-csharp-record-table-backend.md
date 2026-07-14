# CTS-TABLE-M1 C# record-table backend migration

CTS-TABLE-M1 replaces the C# valid-table rejection boundary with deterministic code generation. It preserves CTS-TABLE-M0a/M0b source, semantic, diagnostic, and MIR contracts. CTS-TABLE-M2 subsequently adds the JavaScript realization.

Generated tables use one private typed array for each canonical column, in declaration order. The generated table constructor creates immutable column carriers after assigning its arrays. The module owns exactly one initialized singleton per authored table. Generated row objects retain the owning table and index and read their fields through the table's private columnar representation. Generated tables, rows, and columns are ordinary sealed classes rather than C# records, avoiding accidental record value equality.

The C# backend supports primitive, record, payload-enum, and non-void Result table constants, including nested combinations. It emits canonical record fields and enum/Result payloads in their validated orders. Empty typed columns emit typed empty arrays. Indexing uses the established `TableBoundsError.InvalidIndex` and `TableBoundsError.OutOfBounds` Result cases; negative indexes are finite integral bounds failures.

One shared-MIR production defect was discovered while implementing row realization: `MirTableRowFieldAccessExpression` validated the row type but not the field identity or field type. The correction belongs in `MirValidator`, not C# lowering; malformed row field accesses now fail before backend realization.

The C# backend corpus owns `m1-table-csharp-valid/empty-table.g.cs`, pinned to SHA-256 `B83CAA6470B05E46947F8F66591E9C0428377C642C0555BE1E1F62526FDE955A`. It demonstrates a typed empty table, private column storage, singleton creation, and empty-table bounds handling. No JavaScript table artifact is added.

Closeout validation ran the frontend/table suite (282 tests, 127 ms), C# backend suite (100 tests, about 1 s), JavaScript backend suite (74 tests, about 1 s), and CLI suite (21 tests, about 2 s). `Copeland.TS.slnx`, `Copeland.slnx`, and `JointTaskForce.slnx` built and tested successfully; the broader JointTaskForce run completed in about 24 s. Topology validation and dependency-boundary validation passed. Existing tracked non-table corpus artifacts were unchanged; the only new corpus directory is the C#-owned table fixture. The new/changed table documents were checked for balanced fences and resolvable local links. The Machina slow lane and a NativeAOT publish lane were deliberately omitted because neither shared nor Machina-owned infrastructure changed.

M1 omits JSON, dynamic construction, mutation, table/row equality, query APIs, and JavaScript realization. It maintains the existing NativeAOT-compatible ordinary-C# posture; a NativeAOT publish lane was not run.
