# CTS-CALL-M1 first-class callables and explicit capture

CTS-CALL-M1 completes the callable implementation begun by M0b.

The migration replaces the M0b reference-only boundary with lifted arrow code and immutable explicit environments. Existing named and closed-generic references remain stable; direct named calls remain direct calls. The JavaScript callable runtime grows environment provenance only for programs that construct captured callables, preserving demand emission for reference-only programs.

Representative corpus: `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/cts-call-m1`.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `main.cope` | 2120 | `832B6A103421844A98F654EA1407A27D79445E653772B507D698D2A94ED1EC67` |
| `main.g.cs` | 5062 | `0C1FB55CFCC47E9E05BE677C53E38D9FA3C61AF3A32C3411E5C44E4C7326BA2A` |
| `main.g.js` | 9029 | `9A750ED79A41E25D2CAFBA1C0D43AB1EEE227F797A1BD11733347D684EB1A775` |
| `main.sym.js` | 7560 | `6D74561BEBBE54641B81E1626B6CEF6B558F14C5EC3F08E2AA46A993B15A3C6C` |

The corpus includes named and closed-generic references, expression and block arrows, an escaping captured callable, callable parameter/return flow, container storage, and Result flow. M0b artifact hashes remain intentionally unchanged: the new C#/JavaScript environment infrastructure is demand-emitted only when capture is used.

CTS-CALL is closed for the accepted feature: no implicit lexical closures, serialization, callable equality, async/generator arrows, methods, or host callable interop are introduced.

