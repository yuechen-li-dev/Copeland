# AURELIAN-ARIADNE-MACHINA-DIALOGUE-M7B

Outcome A: Aurelian is usable today as a small, text-first visual novel engine.
The executable sample presents original generated art, branching dialogue, a call,
a condition, a typed consequence, native composition, deterministic input capture,
and Deliverance-backed save/load through the real qualified paths.
The sample now starts as a playable native-frame presenter; pass `--proof` to run
the deterministic artifact-producing qualification instead.

## Ownership and flow

`VnDialogueDefinition` authors stable Ariadne operation IDs and runs them in the
Dominatus HFSM. `DialoguePresentationProjector` reads the definition plus the active
semantic blackboard/checkpoint state and emits an immutable renderer-neutral view.
It does not traverse the graph. `VnMachinaLayer` alone owns widgets, hit testing,
focus, capture, and the VN skin. `VnNativeRenderer` submits background, portrait,
and Machina RGBA overlay as three ordered direct passes to one Aurelian native target.
InputMan owns logical keyboard actions; the compositor owns pointer routing.

Deliverance stores one explicit required `vn.dialogue.session` module containing the
Dominatus checkpoint plus the declared presentation state: selected choice, auto,
and skip. On restore, the surface is recovered from Ariadne's stable operation keys.
The Dominatus checkpoint boundary now also reserves the actuator sequence past all
restored pending IDs, preventing a new completion from aliasing an old dialogue ID.
The added regression test qualifies that invariant without reflection.

## Proof

The runner asserts and writes evidence for:

- exact mid-line resume without redispatch;
- exact pending-choice resume, including selection and declaration order;
- a completed typed consequence retained but never re-emitted;
- a real subdialogue push/pop and a blackboard condition;
- InputMan advance, choice, auto, skip, cancel, save, and load actions;
- opaque VN routing with no gameplay action leakage;
- Machina pointer focus/capture/release and mouse activation;
- native layer order `world-background`, `portrait`, `machina-dialogue-overlay`.

The three screenshots show a normal line, the two-choice state, and the exact soft
expression line restored after the completed effect. `proof.json` records the runtime
claims and native pixel hash; `manifest.json` records files, asset keys, generation
prompts, and SHA-256 hashes.

No browser, React, second UI runtime, dialogue graph, reflection persistence, or
gameplay-side semantic authority was introduced. SkiaSharp is used only to decode and
resize source bitmaps before their Aurelian texture upload. Audio was deliberately
left out: a line-ID cue hook is straightforward, but it does not improve this bounded
text/save/input qualification enough to justify another asset and device lane.

## Pressure answers

1. The minimum engine-worthy presentation model is an immutable step projection:
   stable dialogue/step IDs, step kind, speaker, text, explicit background/portrait/
   expression keys, ordered visible choices and selection, advance capability,
   auto/skip state, and pending semantic actuation ID.
2. The authored metadata catalog, story, asset catalog/loader, skin, quick-slot policy,
   and host timing for auto/skip remain demo-local.
3. A small story RPG substrate still needs an application-owned bridge from typed
   consequences to world intents, conversation ownership across scene transitions,
   relationship/inventory flag projections, and coordinated semantic pause/time.
   It does not yet need a generic quest framework.
4. Keep the Ariadne-to-Machina adapter application-local until a second consumer
   demonstrates shared policy. The immutable projection is the likely first extraction;
   the Machina skin is not.
5. Asset references should be explicit authored presentation metadata per dialogue
   operation. Semantic operation IDs remain the durable save/debug identity, and
   speaker IDs can provide defaults, but neither should implicitly choose expression
   or background.

## Exact next milestone

Build one second consumer: an in-world TinyFarm conversation that pauses semantic
time, projects a world actor into this same immutable model, and converts one typed
dialogue consequence into an existing application intent. If both consumers retain
the same projection contract, extract only that contract/projector seam into a small
Ariadne-Machina integration package; keep skins and asset policy application-local.
