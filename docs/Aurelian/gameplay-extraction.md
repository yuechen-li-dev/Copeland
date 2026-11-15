# Platformer movement, synchronized score and spawn placement

This pass combines a direct extraction of Cloudstep's existing controller with two independently authored capabilities informed by the [Saltline audit](saltline-capability-research.md). It reuses the existing motor, mixer and agent resolver instead of adding parallel physics, audio or agent owners.

## Cloudstep movement belongs to the engine

[PlatformerCharacter3D](platforming.md) in `Aurelian.Spatial3D` owns the reusable movement state: acceleration, air control, buffered/coyote jumps, short hops, turn rate, fall limiting, platform carry/yaw/momentum, wall velocity rejection and edge slipping. `PlatformerSettings3D` is explicit, bounded tuning; `PlatformerSnapshot3D` retains the timers and velocities needed for replay/save continuation.

Cloudstep's adapter preserves its public tuning and save fields. The original course, camera, pickups, animation, audio, respawn and checkpoint code remain game-owned. The engine controller also works on static query worlds without constructing a moving-platform wrapper. It adds no dependency on a game or sibling repository.

The full original headless tour and extracted tour have byte-identical observation traces. The native extracted tour completes with the same final facts and 14 captures. This checks the real motivating platformer, including shuttle, lift, crumble and turntable sections. Zero grace intervals additionally support immediate grounded jumps rather than disabling jumping entirely.

## Synchronized music is one mixer voice

`AurelianAudioRuntime.PlayMusicLayers` accepts 1–8 distinct prepared layers sharing frame count and sample rate. Mono and stereo resources can coexist. The layers share one source cursor, resampling step, Music bus, fade and voice lifetime. Their mix is produced during the ordinary PCM pass; no device-specific clock or independently advancing stem voices are involved.

```csharp
using LayeredMusic score = audio.PlayMusicLayers(new("encounter-score"),
    [new(new("score.base"), .55f), new(new("score.combat"), 0)])
    ?? throw new InvalidOperationException("Score voice was not admitted.");

score.SetGains([.55f, encounterIntensity * .65f], TimeSpan.FromSeconds(.4));
score.SetPaused(paused);
score.Reset(); // frame zero, authored initial gains, no new voice allocation
```

Gain ramps advance per mixed sample, not per host frame. Re-projecting an unchanged target does not restart its ramp. Pause freezes every layer and its ramp. Muting Music silences output while the shared cursor continues. Reset returns all layers to their initial phase/gains and clears a pending stop fade. Stop, stealing, capacity admission and disposal apply to the entire logical voice. Invalid periods, duplicate layers and invalid gain edits are rejected before changing live playback. One group uses one voice-capacity slot; replacing other music remains an explicit caller decision.

Beacon prepares an independent eight-second, 120 BPM two-layer score during loading: a quiet plucked sequence and a percussion layer. Living-creature count, proximity and accepted hurt state drive the combat gain. These are presentation inputs, not authority from audio playback. The score pauses with the run, follows Music/Master volume, and restarts on the same voice at frame zero. Actual phase and mixed gains stay out of gameplay fingerprints and replay facts; deterministic encounter intensity is inspectable.

Cloudstep retains its existing title/course tracks. The new API does not require changing ordinary music playback. There is no new streaming, arbitrary-duration stem synchronization, tempo adaptation or convolution/reverb path. The current mixer resampling behavior remains authoritative.

## Placement proposes; Beacon commits

`SpawnSelector3D` in `Aurelian.Spatial3D` scores an explicit catalogue of 1–256 authored ground spawn points. It rejects capsule overlap, missing/unwalkable ground, points too near the player and points too near supplied occupied positions. Admitted choices trade preferred player distance against visibility and recent reuse. Visibility combines an explicit view cone with a world ray to the actor's body. Ties use ordinal point identities, independent of caller enumeration order.

```csharp
var selector = new SpawnSelector3D(new() { MinimumPlayerDistance = 6, MinimumSeparation = 3 });
SpawnChoice3D? proposed = selector.Select(world, authoredPoints, playerEye, playerForward,
    simulationTime, occupiedFeet);
if (proposed is not null)
{
    SpawnThroughGameResolver(proposed.Point.Feet);
    selector.RecordUse(proposed.Point.Id, simulationTime);
}
var history = selector.Capture();
selector.Restore(history);
```

Selection does not mutate history. Record a use only after acceptance. Recent-use history is bounded to 256 entries, captured in identity order, and validated atomically on restore. Time is explicit simulation time; rewinding requires restoring history. Physical validation uses the query world and caller-supplied occupancy; it does not infer dynamic actor shapes from render geometry.

Beacon now plans each 2/3/4-creature wave from eight authored points, with a six-metre player exclusion and three-metre separation. It defers a wave when a complete safe placement cannot be proposed. After a successful plan, its existing scene resolver creates the agents and attaches the existing Dominatus brains, then commits use history. That history participates in the gameplay fingerprint. Wave counts, combat authority and the nine-creature completion objective remain intact. This adds placement scoring, not a new encounter scheduler or staggered-spawn system.

## Reproduce

```powershell
dotnet test Games/Cloudstep/Cloudstep.Tests -c Release -m:1
dotnet test Games/Beacon3D/Aurelian.Beacon3D.Tests -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --music-layers --output artifacts/local/music-layers
dotnet run --project Games/Cloudstep/Cloudstep -c Release -- --playtest-script Games/Cloudstep/Playtests/tour.json --resolution 960x600 --output artifacts/local/platformer-extraction/native --save-root artifacts/local/platformer-extraction/native/saves
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release -- --playtest-script Games/Beacon3D/Aurelian.Beacon3D/Playtests/presentation.json --resolution 960x600 --output artifacts/local/remaining-extraction/beacon-native --save-root artifacts/local/remaining-extraction/beacon-native/saves
```

The audio proof uses Beacon's actual prepared resources and the real mixer. It exports `beacon-calm.wav` and `beacon-combat.wav`, checks increased percussion energy, pause silence and phase retention, zero Music volume, and byte-identical floating-point PCM after restart. On the qualification run, RMS rises from .02150 to .03033. Separate synthetic tests check exact loop/channel samples, update subdivision, atomic rejection and whole-group stealing. Scripted native games use silent device output; ordinary launch uses the existing NAudio backend. Mix quality and human comfort still need hands-on judgment.

The remaining source ledger still includes distance-aware alpha coverage, crouch clearance, wet-face detail and reverb. Those have separate material, stance and DSP acceptance cases; this pass does not claim them.

Qualification passes **1,406 standalone engine tests across 20 assemblies**, **nine Cloudstep tests**, **35 Beacon tests** and **86 shared-games integration tests**, with no failures or skips. Core closure remains 56 projects with no game or sibling dependency. Both core and full Aurelian solution builds pass; the vendored OpenFont rebuild retains its 14 existing unassigned-field warnings. The native Cloudstep tour and both native Beacon presentation/depth scripts complete with 31 captures in total. Evidence is under `artifacts/local/platformer-extraction`, `artifacts/local/music-layers` and `artifacts/local/remaining-extraction`. These are movement, mixer and playable integration witnesses, not a frame-rate or subjective audio-quality guarantee.
