# Architecture reading route

Begin with the [repository overview](../getting-started.md) if you are choosing
an authoring language or a first application. This route is for understanding
ownership and changing shared infrastructure.

| Read in this order | Meaning and status |
| --- | --- |
| [Topology and ownership](jtf-m0-topology-and-ownership.md) | Physical project families and enforcement doctrine; compare actual solution membership when building |
| [Semantic boundaries](jtf-target-semantic-boundaries.md) | Target semantic ownership and recorded migration status; a target is not proof every edge has migrated |
| [Test lanes](jtf-test-lane-doctrine.md) | Intended contract/integration/diagnostic separation; current exceptions and tool prerequisites are in maintenance |
| [Repository maintenance](../repo/maintenance.md) | Current lane selection, verification, file placement and known operational prerequisites |

For subsystem details, continue to [compiler topology](../Copeland/architecture/compiler-topology.md),
[Machina.UI](../Machina.UI/README.md), or [Aurelian](../Aurelian/README.md).
[Marionette ownership](../Marionette/marionette-aurelian-ownership.md) describes the
Skyrim-specific composition boundary; it is separate from the engine.

The [repo-wide cleanup record](../audits/repository-5s-pass3.md#remaining-ownership-findings)
lists eleven existing dependency findings. Those do not authorize new hidden
cross-system dependencies. Use the existing dependency validator to inspect the
current graph; do not treat the target architecture as a claim that it passes.

Other `jtf-*`, `AURELIAN_*`, milestone-numbered and migration documents explain
specific decisions or domain integrations. The [historical catalog](../history/README.md)
keeps them discoverable without making them prerequisites for ordinary authoring.
