# Built-in shader bundle

This assembly distributes Aurelian's built-in Visual TypeScript shader sources,
their explicit catalog, and the adapted Aetheris temporal policy. The combined
bundle is offered under AGPL-3.0-only. The temporal adaptation's provenance and
license text are supplied in `licenses/Aetheris-Temporal-NOTICE.txt` and
`licenses/Aetheris-AGPL-3.0.txt`. Existing source attribution remains applicable.
The ComputeMath family adapts Oct Prometheus algorithms; its source paths and
license boundary are recorded in `licenses/Prometheus-NOTICE.txt`.

The EON diffuse fit in `Lighting3D.v.ts` is adapted from Jamie Portsmouth's MIT-licensed `portsmouth/EON-diffuse` implementation. Attribution and the full license are supplied in `licenses/EON-LICENSE.txt`.

The experimental PathDiffuse lobe uses an Aurelian separable first-bounce model
with Frostbite-inspired attenuation and the colored multiple-scattering closure
described by Portsmouth, Kutz and Hill in EON (JCGT 2025, section 3.1).
Its derivation and scope are documented in `docs/Aurelian/diffuse-comparison.md`.

The compiler assembly `Aurelian.Shaders` remains independent of this bundle;
applications select the bundle by referencing `Aurelian.Shaders.Builtins`.
The repository's GPL license and the optional Aetheris integration boundary are
unchanged. This package does not import the Aetheris implementation or its pinned
projects.

Corresponding shader and catalog sources are supplied under `sources/` in the
NuGet package. Repository authoring files live in
`src/Aurelian/Aurelian.Shaders/Assets`, grouped by family. Runtime source copies
retain flat `Assets/<module>.v.ts` names. Applications built with project
references also receive those source files and temporal license notices when
published. NuGet consumers load the embedded library and can obtain the source
and notices from this package's `sources/` and `licenses/` directories.
