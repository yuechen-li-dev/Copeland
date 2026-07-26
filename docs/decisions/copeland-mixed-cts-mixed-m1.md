# CTS-MIXED-M1: same-project authored C# declaration projection

## Decision

Copeland source in an ordinary SDK-style C# project may bind supported authored
C# declarations from that same project. The MSBuild task builds an in-memory,
metadata-only declaration image before `CoreCompile`; it never writes or loads
a temporary implementation DLL. Roslyn parses the authored `@(Compile)` files
with the project's language version, nullable context, and preprocessor symbols.
Executable bodies, field initializers, attributes, and top-level statements are
removed only for the declaration image. The final project compilation remains
the normal Roslyn compilation of authored C# and generated Copeland C#.

The declaration image is fed into the existing CTS-CLR-M1 metadata resolver,
which continues to bind members and retain their reflection-backed CLR identity
through MIR. C# emission uses that identity to produce direct, fully-qualified
calls such as `global::Demo.Names.Normalize(value)`; there is no reflection,
`dynamic`, dispatch bridge, or runtime lookup.

## Build flow

```text
authored .cs + resolved metadata references
  -> Roslyn parse + declaration-only metadata image (in memory)
  -> existing Copeland CLR resolver and binder
  -> .cope MIR + generated .g.cs under obj/Copeland
  -> ordinary CoreCompile of authored and generated C#
```

The target receives authored `@(Compile)` items as an input and excludes the
`obj/Copeland` generated directory, preventing recursive projection. Its input
fingerprint includes the authored C# text, so any C# declaration change cannot
leave stale Copeland output. A method-body-only change may rerun Copeland in M1,
but `WriteIfChanged` preserves generated output when its semantic result is
unchanged.

## Supported surface and accessibility

This shares the bounded CTS-CLR-M1 law: namespaces, named public or same-project
internal types, static methods, constructors, instance methods, readable
properties, primitives, strings, one-dimensional arrays, and the existing
bounded generic/overload rules. Copeland can use an imported CLR type as a
nominal annotation, for example `const counter: Counter = new Counter(1)`.
Overload selection occurs in the Copeland binder and the selected member identity
is preserved into MIR.

Public types and members are visible. `internal` types and members are visible
only from the same-project declaration image. Private, protected, and
protected-internal members are not projected as ordinary Copeland members and
produce the existing `COPE-CLR-0004` accessibility diagnostic at the Copeland
usage site. Unsupported member/type shapes continue to produce the existing
`COPE-CLR-0005` through `COPE-CLR-0007` diagnostics.

## Boundaries deliberately retained

The projection honors `DefineConstants`; declarations in inactive conditional
regions are absent. Roslyn source generators and analyzer-produced declarations
are not projected in M1: they run only in final normal compilation. Generated
Copeland C# is excluded from the input projection.

The useful project-level cycle is supported: authored C# can call generated
Copeland declarations while Copeland calls independent authored C# declarations.
Recursive declaration shapes, C#/Copeland partial-type merging, generated
Copeland types in C# signatures that must be projected before generation,
cross-language base or constraint cycles, inline C# blocks, async CLR adaptation,
writable properties, events, delegates, and broader nullable/object/enum
projection remain deferred.
