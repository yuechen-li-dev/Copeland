# Copeland TS structured JavaScript emission (CTS-JS-EMIT-M0b)

**Status:** implemented Diagnostic-preservation foundation. The public JavaScript contract remains `--emit javascript` with the existing Diagnostic text, layout, filenames, and corpus ownership. Symbolic, Release, source maps, helper deduplication, and runtime packaging are not implemented.

## Architecture

M0b keeps JavaScript semantic lowering in `Copeland.TS.Backend.JavaScript`. It adds a deliberately backend-local emission model rather than a general compiler IR or a JavaScript AST:

```text
Cope MIR
  -> existing JavaScript semantic lowering
  -> JavaScriptEmissionDocument (scopes, bindings, validation)
  -> JavaScriptNameAllocator (Diagnostic names)
  -> JavaScriptTokenWriter (token/trivia-safe future printer)
  -> Diagnostic line-event printer
```

`JavaScriptEmissionDocument` owns `JavaScriptScopeId`, `JavaScriptBindingId`, scope kinds, binding roles, declaration kind, compiler origin, visibility/mangling flags, deterministic allocation ordinal, assigned Diagnostic name, and reference count. Its validation rejects unresolved bindings, duplicate declarations, out-of-scope references, absent names, reserved/invalid generated names, and duplicate final names within a scope. Program, function, and block scope kinds are available; current Diagnostic helper allocation remains program scoped so its historic spelling/order is unchanged.

The compatibility decision is intentional. The existing emitter is a dense, tested semantic lowering whose full conversion from line templates to token events would be a semantic rewrite, not a safe naming refactor. M0b therefore routes actual generated-name allocation through `JavaScriptNameAllocator`, records every emitted Diagnostic line as a structural event, and validates its identity document before output. Interpolated generated bindings are `BindingPart` events, not string-name lookups; rendering validates their scope and then prints the existing spelling. The token writer is independently exercised and ready to become the printer target in a later, separately reviewed lowering migration. It is not a second backend and does not reinterpret MIR.

## Diagnostic allocation and collision policy

Diagnostic allocation retains the exact legacy form:

```text
__cope_m3_<diagnostic-purpose>_<global ordinal>
```

Allocation is deterministic and skips every encoded user function, parameter, and local name already reserved by the compilation. This includes host-visible names such as `main` and hostile user names that resemble generated helper names. The allocator records the binding identity before assigning the existing spelling; later profiles can assign another spelling without changing lowering ownership. The current conservative global reservation remains collision-safe while function/block-specific reuse is deferred to a compact profile.

User-authored functions, parameters, locals, observable record/enum/TSON identities, diagnostic strings, and host entry points remain unmangled. Private helper, token, storage, Symbol-slot, singleton, flow, and temporary names remain backend-private.

## Token and trivia model

`JavaScriptTokenWriter` owns keywords, external identifiers, binding references, numeric literals through `JavaScriptLiteralWriter`, string literals through the existing escaping authority, supported punctuators, spaces, indentation, line breaks, and exactly one final LF. It separates word/number adjacency; protects `+`/`++`, `-`/`--`, and slash-comment adjacency; and separates a numeric literal from property access. Unknown punctuators and writes after completion fail as invariants. It is intentionally a closed emitted-subset writer, not a parser.

The established Diagnostic line layout remains four spaces, LF-only, existing blank lines/braces/semicolons, existing literal escaping, and one final newline. The old `JavaScriptTextWriter` now rejects negative or unfinished indentation as an additional invariant.

## Raw migration inventory

The bounded raw-fragment inventory is the compiler-owned syntax portions of the existing `JavaScriptTextWriter.WriteLine` templates in `JavaScriptBackend.cs` (149 interpolated line templates at M0b close). They are recorded as `TextPart` events; every generated binding that reaches the writer as a `JavaScriptBindingReference` is recorded separately as a `BindingPart`, checked against the active program/function scope, and rendered only after validation. No public raw-fragment API exists, user text is never accepted as raw JavaScript, and no textual JavaScript parser or post-generation renamer exists. M1/M2 may reduce this template inventory by replacing syntax families with direct token events, but must not reintroduce raw generated binding names.

## Mapping extension point

The document already gives future mapping code stable binding identities and deterministic token ranges. A later printer can attach backend-local MIR paths to token events and map compact bindings back to Diagnostic provenance. Copeland source maps still require source spans associated with MIR paths; M0b adds neither spans, `.map` files, source-map comments, nor CLI policy.

## Preservation and demand

The allocator leaves demand gates, helper ordering, `Symbol` descriptions, table/TSON planning, value/flow runtime shape, and exactly-once staging untouched. The JavaScript backend corpus passes byte-for-byte without fixture regeneration. The retained benchmark artifacts are:

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| CTS-TSON-TABLE-M1 `main.g.js` | 38,279 | `F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A` |
| CTS-TSON-TABLE-M2/M3 `main.g.js` | 62,425 | `D7363BCD7050B8A255E290CDCEF7CC633A6250EF887731DD78539BFC4BA19EF9` |

## Deferred M1 decisions

The owner-ratified future vocabulary remains documentation only: Heavenly-Stem ordinals (`甲` through `癸`, then bijective pairs); `组` array; `项` enum case; `接` handler; `符` token; `印` nominal provenance; `更` immutable update with `写` reserved for mutation; `纲` schema; `识` stable identity; `布` Boolean; `域` record field; `配` match; Symbolic `Symbol` descriptions; `.sym.js` Symbolic suffix; `.g.js` Diagnostic suffix; and `.min.js` Release suffix.

M1 may adopt a reviewed Symbolic allocator and migrate compiler-owned templates to token events. It must not change user/public names. Release compaction, source maps, helper/runtime factoring, imported packages, and external minifiers remain later work.
