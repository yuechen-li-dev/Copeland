# CTS-TABLE-M0b record-table language and MIR migration

CTS-TABLE-M0b advances the design recorded by CTS-TABLE-M0a into source syntax, binding, canonical MIR, deterministic `.cope` text, fixture coverage, and deliberate backend rejection.

The implemented syntax is `record table Name { column: [cells]; typed: T = [cells]; }`, `Name.Row`, `column T`, and postfix indexing. Table identities are deterministic by authored declaration order (`tN`, `tN.row`, `tN.cM`). The table name is both its nominal type and singleton value. Bound and MIR definitions own closed literal/record/enum/Result constant trees rather than executable expression nodes.

The bounded `COPE-TABLE` diagnostics reserve declaration and rectangularity errors (`0001`–`0008`), constant eligibility (`0009`–`0010`), table/access rules (`0011`–`0016`), equality and nominal row mismatch (`0017`–`0018`), and unresolved row/column annotations (`0019`). Source fixtures under `Language/Valid/tables` and `Language/Invalid/tables` establish the initial filesystem contracts.

Backends validate first. Canonical valid table MIR produces no C# or JavaScript artifact and receives the respective table-unsupported diagnostic. Malformed table MIR must fail shared validation instead.

CTS-TABLE-M1 and CTS-TABLE-M2 remain responsible for executable C# and JavaScript realization. JSON remains deferred.
