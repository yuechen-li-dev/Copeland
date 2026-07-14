# CTS-TABLE-M3 immutable record-table closeout

CTS-TABLE-M3 closes the implemented immutable record-table ladder from source through canonical MIR, C#, and JavaScript. It is a ratification and adversarial-verification milestone, not an expansion milestone.

## Audit result and fixes

The audit retained the M0a–M2 source, MIR, fixture, diagnostic, corpus, and artifact contracts. It found two general production defects while exercising table access in adversarial expressions:

- JavaScript backend unary lowering was absent although the frontend validates unary `-` and `!`. The JavaScript backend now validates and emits these operators and recursively catalogs their operands.
- C# emitted an assignment bare when it occurred in a unary or binary operand. C# precedence then changed `(trace = trace * 10 + 2) - 12`. Operand emission now parenthesizes assignments; the non-table regression returns `190`.

No new table syntax, MIR node, constant variant, diagnostic, package, output representation, or compatibility shim was introduced.

## Closed contract

The source/type, MIR, bounds, representation, malformed-MIR, nominality, immutability, exactly-once, and artifact laws are ratified in [the M3 architecture closeout](../Copeland/architecture/copeland-ts-record-tables-closeout-cts-table-m3.md). The shared validator remains the single malformed-MIR gate; valid MIR reaches both realizations and malformed MIR produces no fresh backend artifact.

The representative parity trace is `28755,true,10,20,true,1000,1000,1000,1000,2003,2003,2003,2003,2000,3000`, repeated twice on Node and matched by generated C#. Node version is `v26.2.0`.

The retained hashes are M0b representative `.cope` `62897D4142128179A9036545CBA4A0BDB4E3EB74ACF9D722E71E90A0EF93234F`, `empty-table.g.cs` `B83CAA6470B05E46947F8F66591E9C0428377C642C0555BE1E1F62526FDE955A`, `m2-table-basic.g.js` `B9AEA6132233229C4F594E9AB34F89F9D4E8F906B160CC1485CE2706436E3C26`, and `m2-table-nested.g.js` `7D72CC23337D65B4F1841D01B5E7E7ED04BD65794109F3D43FB54EEDF3856145`.

JSON and every serializer/codec/host interop surface remain unimplemented. [CTS-TSON-M0a](../Copeland/language/copeland-ts-tson-design-cts-tson-m0a.md) supersedes direct table-to-JSON implementation: a separately approved table TSON extension must precede any JSON compatibility lowering, and private JavaScript carriers are neither TSON nor JSON.

## Validation record

| Lane | Result |
| --- | --- |
| Focused M3 C#/Node parity | 2 tests, about 1 s; trace repeated twice |
| Full Copeland TS lane | 282 frontend, 103 C#, 78 JavaScript tests; about 5 s |
| Full Copeland solution | 282 frontend, 103 C#, 78 JavaScript, 20 CLI, and 82 Markdown tests; about 6 s |
| JointTaskForce solution | passed; Copeland table lanes included; about 11 s test wall-clock |
| Topology/dependency checks | passed (`Validate-CopelandTsTopology.ps1`, `Validate-DependencyBoundaries.ps1`) |

The M3 audit deliberately did not run Machina's slow lane or a NativeAOT publish lane: no Machina/shared infrastructure changed, and no publish was requested. The corpus snapshots above and existing non-table snapshots remained byte-stable; no artifact was rewritten.
