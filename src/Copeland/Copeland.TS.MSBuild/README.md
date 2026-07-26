# Copeland TS SDK

`Copeland.TS.Sdk` makes explicit `.ts` and `.tsx` `CopelandCompile` items part
of a normal SDK-style .NET project. See the repository MSBuild integration
decision record for the supported source surface and integration model.

Relative named imports between those explicit project sources are supported;
see the current [local-module authoring guide](../../../docs/Copeland/authoring/local-modules-m1.md)
for resolution order, visibility, and deliberately unsupported TypeScript
module features.

Authored C# declarations from that same project are projected through Roslyn
into the existing CLR binding model before Copeland generation. The task uses an
in-memory metadata-only declaration image, not a temporary implementation DLL.
See the same-project declaration projection decision record for the supported
surface and limitations.
