# Oblivion Diagram Card dogfood — M19o

## Real task and Cards

The dogfood task was a safety review of the real VIZ-M2 `VehicleFlow`: determine whether guarded events make recovery, self-transition, and terminal behavior understandable without manually restating the transition table. The vault Page contains exactly Markdown / Diagram / Markdown:

1. `flow-context` states the technical review question.
2. `vehicle-flow-state` derives `VehicleFlow -> state` from Copeland semantics.
3. `flow-findings` records the human conclusion.

The Diagram Card was natural because a branching guarded state relation communicates the answer faster than prose. Compiler derivation saved a duplicate transition description and made the semantic source, symbol, and projection directly inspectable.

## Diagram desire log

| Task/context | Wanted communication | Semantic source | Current IR | Existing projection | New projection desired | Backend limitation |
| --- | --- | --- | --- | --- | --- | --- |
| Review `VehicleFlow` safety behavior | guarded branches, recovery routes, self-transition, initial and terminal state | Copeland flow semantics | sufficient | `state` sufficient | no | label crossings and whitespace are renderer-controlled |
| Trace M19o realization ownership while integrating the Card | ordered derivation chain and ownership handoffs | typed Model/App/UI contracts and producer records | nodes/edges can approximate it, but ordering/grouping are weak | no existing source-authoritative projection | an ordered annotated relationship projection may be useful | Mermaid flow layout cannot guarantee the desired stable ordering/grouping |

Only the first desire became a Card. The second was not implemented because code navigation and the written pipeline were sufficient for this bounded task; creating a manually maintained architecture Diagram Card would have violated the semantic-truth goal.

## Human readability

At 2560×1440, collapsed state contains no preview. Expanded state makes the diagram dominant, uses the Card width well, preserves aspect ratio, and keeps every guard readable. The 760-pixel host Card is not too tall relative to the viewport and Page scrolling remains unambiguous. No interaction or pan/zoom was needed for this three-state/five-transition graph.

The visual communicates faster than the adjacent prose: `Still` self-transition, `Moving` recovery routes, and terminal `Crash` are visible in one scan. Mermaid introduces excess white space and some crossing/close labels, but not enough to obscure meaning.

Light appearance integrates cleanly with the renderer canvas. Dark appearance remains readable but the white PNG canvas is visually abrupt against the dark Card. This is theme pressure, not a correctness failure.

## Agent usefulness

Codex preferred the Diagram Card for the guarded state relationship and Markdown for the review question and conclusion. Derivation avoided duplicating compiler truth. `card show` made the source and fingerprint inspectable; the cache sidecar made the derived artifact trustworthy. `card content` correctly refused to pretend the visual had authored text content.

## Emerging visual vocabulary

Observed needs were smaller than a named-diagram taxonomy:

- entities with stable identities;
- directed relationships;
- ordering;
- annotations on relationships;
- initial/final annotations;
- optional grouping/ownership boundaries.

The state machine is a specialization of entities, relationships, ordering, and annotations. The unbuilt realization-chain desire adds stronger ordering and grouping. Dogfood did not produce evidence for charts, timelines, sequence diagrams, lanes, or quantitative values.

## Diagram IR pressure

Current `nodes + edges + direction + state metadata` was sufficient for the real Card. Guard labels, semantic transition identity/order, and initial/final state metadata already carried the required truth. No IR change was justified.

The second desire exerted mild pressure for groups/ownership boundaries and explicit ordering constraints. It did not justify implementation because no source-authoritative projection exists yet and the diagram was not necessary to complete the task.

## Mermaid/backend pressure

Mermaid remains good enough as the bootstrap backend: **MOSTLY**.

It succeeded on the diagram Codex actually chose, produced a readable artifact offline, and reused qualified caching and Avalonia hosting. Frictions were layout whitespace, close/crossing transition labels, inability to guarantee ordering/grouping for the unbuilt ownership chain, and the fixed white canvas in dark appearance. Large-graph behavior was not exercised, so M19o makes no claim about it.

Direct SVG is not justified now. The evidence supports a smaller next milestone: make renderer appearance an explicit derived-artifact option, qualify a dark Mermaid render alongside the current light/default render, and preserve separate cache keys/provenance. Do not begin a native SVG backend unless later dogfood shows semantic diagrams that Mermaid cannot communicate clearly.

## Outcome

Outcome A: Diagram Cards are a natural notebook primitive for compiler-derived visual relationships. The semantic connection is strong, the first real Card saves duplicated explanation, and current IR/backend are sufficient for the observed required diagram. The dark-canvas mismatch is a bounded follow-on, not an awkward semantic or presentation seam that blocks use.

