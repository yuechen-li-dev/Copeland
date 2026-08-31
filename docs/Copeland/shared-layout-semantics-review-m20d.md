# Shared layout semantics review — M20d

## Order evidence

Order matters in all three diagram families, but the evidence does not yet justify a new authorable Diagram concept. Graph A uses semantic transition order to preserve state-machine reading and path layering. Graph B uses canonical reflected call identity/order so repeated direct calls aggregate deterministically. Graph C uses canonical semantic identity as the seed and tie-breaker for ranks, barycenter sweeps, components, and routes. Changing these inputs can change useful presentation, but no graph required an author to override the existing semantic order.

Copeland UI layout also has order, but its contracts are more specific: lexical row order becomes a paint tie-breaker, layer declaration order becomes total layer rank, and child/track order controls containment and allocation. Diagram order instead constrains graph traversal, crossing reduction, and edge layering. They share the broad law that durable semantic sequence must be stable; they are not yet one interchangeable authoring contract.

Verdict: order is genuine semantic input and should remain preserved internally, but M20d does not add public `DiagramOrder`. An author override has not earned a language/API surface.

## Alignment evidence

Graph A's specialized policy uses explicit-looking phase/repair alignment. Graph B aligns its two-row fan-out through specialized geometry. Graph C centers layer baselines and uses barycentric parent/child pressure. In Graph C, changing alignment improves readability, but no domain fact says that two nodes *mean* alignment; it is a consequence of the resolved algorithm.

UI layout alignment is authorable spatial intent: rows/columns/tracks and reference alignment establish box relations independent of a graph optimizer. Diagram alignment currently means continuity inferred from adjacency and layer geometry. The names overlap, but the semantic obligations do not. Treating them as one API would conflate authored box relationships with renderer-derived graph placement.

Verdict: alignment remains resolved geometry/backend policy. It does not qualify as shared author intent.

## Tracks, tables, and promotion gate

Copeland layout tables own typed boxes, rows/columns, named slots, tracks, layers, z-order, and paint order. Automatic graph layout owns ranks, within-rank ordering, routes, and label anchors. A rank is not a UI grid track: graph edges pressure rank/order, while UI tracks allocate an already-authored containing box. An edge route also has no faithful cell representation. Reusing the table system would require a translation larger and less truthful than the bounded graph policy.

Neither candidate passes the M20d public-vocabulary gate strongly enough:

- `order` appears broadly and changes presentation, but existing source semantics already supply it and no author override was needed;
- `alignment` improves presentation but remains derived geometry, and its UI namesake carries a different authoring contract;
- `spine`, `lane`, coordinates, and tracks remain topology/backend-specific.

The verdict is `KEEP_LAYOUT_VOCAB_INTERNAL`. No Diagram IR, Copeland syntax, reflection query, or stable public layout API changes in M20d.

## Evidence-bounded M20e

Choose outcome C: keep layout policies backend-internal. A bounded M20e may improve automatic layer compaction for high fan-out and large cyclic regions, then rerun the same A/B/C matrix. Do not add language syntax first. Default promotion should be reconsidered only if automatic Graph A/B Fit materially improves without graph-specific hints; alignment should remain derived, and order should be promoted only after a real author-override case appears.
