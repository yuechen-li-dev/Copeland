# RTS benchmark architecture mine

M18 reviewed six implementations as design evidence. The result is a bounded strategy kit, an integrated native sample, and a first-party Profile art proof. It is **Outcome B**: reusable selection, visibility, projection and HUD landed; the Profile-to-native asset realization remains a sample CPU adapter. The screenshot does not yet establish Wilderland's polish tier.

The [complete subsystem matrix](../milestones/m18-rts-corpus-audit.md) covers 30 concepts across every source. `artifacts/aurelian-rts-pearl-mining-m18/corpus-audit.json` records fetched byte counts, SHA-256 hashes and inspected symbol lines. Run `python scripts/Build-RtsM18Audit.py` to regenerate the source inventory and reviewed classifications. The classifications are explicit review conclusions, not an automatic inference from keyword counts. Future source changes require re-review.

## What each source contributes

| Source | Distinct useful evidence | Limits on adoption |
| --- | --- | --- |
| [Fable](https://gist.github.com/senko/2a51dfb52f5bb4bbca65ad628a0cb9d2) | Seeded distribution followed by guaranteed start resources and flood/carve reachability repair; full gather/carry/dropoff loop; supply reservation; selection/control-group conventions | Placement directly relocates occupants; map carving is tied to its terrain and mine rules; neither becomes engine truth |
| [Opus](https://senko.net/vibecode-bench/2026/rts-opus-5.html) | Traditional RTS completeness; cached directional sprites; multi-worker construction; cached disk visibility; explorable-area objective denominator | Building placement explicitly permits unit overlap; frame-driven timers and game-specific labor multipliers stay behind |
| [Astra / Wilderland](https://senko.net/vibecode-bench/2026/rts-gpt-6-astra.html) | Strongest coherent visual layout; reusable world/portrait drawing; diamond projection; anchored zoom; blocked production holds; placement reachability rollback | Peaceful interpretation despite soldiers; soft fog is presentation; localStorage is not a validated save contract |
| [Sol](https://senko.net/vibecode-bench/2026/rts-gpt-5.6-sol.html) | Science-fiction visual differentiation; rounded chassis, vents, stripes, radial glows and repeated crystal fans; terrain/fog/entity minimap | Much less evidence of a complete combat game; glow and animation are rendering recipes, not new semantic systems |
| [Qwen](https://senko.net/vibecode-bench/2026/rts-qwen-3.8-max.html) | Another traditional order/cancel/refund/rally implementation; blur clears held input; explicit game-speed modes | Unseeded randomness and monolithic ownership are unsuitable for deterministic reuse |
| [Kimi](https://senko.net/vibecode-bench/2026/rts-kimi-k3.html) | Shift and double-click selection, gather/build/rally, cached terrain and a high exploration target | Unseeded generation; attack-move/control groups were not found in inspected handlers |

All six sources were read, including map, input, update and draw paths. Wilderland also received browser visual inspection. This is not a six-game gameplay certification. Names follow the supplied corpus labels; common genre behavior does not prove independent invention or anything about model training.

## Convergence and family differences

Selection sets, contextual right-click, gather/carry/dropoff, queue cost/supply checks, explored-versus-visible state, camera navigation and semantic minimaps recur across implementations. This supports extracting identity selection and a bounded visibility mask, while leaving prices, faction eligibility and build rules application-owned. Convergence raises confidence; it is not sufficient reason to create an engine abstraction.

In this corpus Fable and Opus concentrate on conventional RTS mechanics. Astra and Sol invest more in distinct product presentation and procedural drawing, with less combat completeness. Qwen and Kimi corroborate the conventional workflow and contribute useful negative cases around randomness and input state. These are observations about six artifacts, not a benchmark ranking or a general model-family claim.

## Owner decisions

Already solved: InputMan physical mapping/capture/focus; Aurelian cadence, spatial queries and camera; Dominatus persistent behavior; Machina semantic layout/presentation; Copeland Profile binding, palettes and canonical contours; SpriteForge region/panel realization. M18 reuses these instead of introducing another timer, input mapper, HFSM, collider or vector parser.

New reusable mechanisms: `EntitySelection<TId>` and `VisibilityGrid` in Aurelian.Strategy; `IsometricProjection` in GameWorld2D; `StrategyHudProfile` with typed facts/style/copy in Aurelian.Machina. The sample owns intents, orders, cost/supply, movement acceptance, construction, resource identity, objectives and the semantic minimap adapter. Art templates are ordinary Copeland functions, currently distributed with the proof pack.

Rejected patterns: giant global truth bags; rendering or DOM values as world truth; mouse callbacks mutating positions; string command switches across boundaries; frame-time economy; duplicate camera transforms; unseeded semantic randomness; placing through occupants by teleporting them; localStorage objects treated as a versioned save format. `rejected-benchmark-hacks.json` ties the rejection to an observed source pattern and its replacement owner.

## What was deliberately not extracted

Fable's generate-then-validate-then-repair recipe is valuable. Its repair policy protects particular mines and corridors, and Opus has different reachability assumptions. M18 uses a small fixed map and has no second map-policy consumer, so it does not invent a shared world generator. Likewise, attack-move, queue cancellation/refund, rally, unit separation and navigation planning remain evaluated future app capabilities. Ordinary move, gather, attack, build and production are enough to test the integrated slice.

The reuse evidence is structural: a fresh ranger and wood resource use existing behaviors and typed orders; a fresh watchtower uses existing Profile templates; a farming HUD fixture preserves layout while changing style and facts. No controlled baseline measured time saved or inference cost. The requested claim that no frontier inference rebuilt systems cannot honestly be made: re-expression and integration required new code. What is demonstrated is reuse of existing infrastructure and local second-feature changes.
