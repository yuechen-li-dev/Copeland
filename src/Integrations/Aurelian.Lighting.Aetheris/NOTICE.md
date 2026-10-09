# Optional Aetheris lighting integration

The adapter and authored tracing/presentation shaders in this directory are
Copeland code under GPL-3.0-only. Aetheris.Fields and Aetheris.Kernel.Core are
separate Aetheris code under AGPL-3.0-only. They are consumed as NuGet packages;
this adapter does not import Firmament or its pinned Copeland backend packages.

Generated analytic field implementations are derived from Aetheris's evaluator
and are conservatively distributed with AGPL-3.0-only provenance. Keep their
generated source, originating field description, structural hash and notices.
Geometry and artist assets retain their own terms; this policy does not assign
AGPL to every file exported by Aetheris.

GPLv3 section 13 and AGPLv3 section 13 permit this combination. Each component
retains its licence. Conveying a combined build requires the applicable notices
and Corresponding Source. AGPL section 13 network-source requirements apply to
the combination when its conditions are met, including incorporated GPL code.
This is not a relicensing of the GPL runtime or an assertion that a shader bake
removes AGPL obligations.

Licence texts: Copeland root LICENSE and Aetheris root LICENSE. The proof copies
both into its output and records generated-source identity. See
docs/Aurelian/aetheris-field-lighting.md for the concrete build and source boundary.
