# AURELIAN-GAME-AUDIO-M4 report

## Outcome

**Outcome B — the reusable mixer/playback path works; streamed music and a Linux device backend remain bounded seams.**

```text
accepted game result -> immutable AudioCue -> Aurelian.Audio mixer -> null/offline or NAudio Windows backend
Dominatus AudioArtifact --------------------^ (PCM WAV adapter)
```

Gameplay never calls a device, owns a voice, or observes playback as state truth. `AurelianGameHost` owns the optional audio runtime, advances it with elapsed host time, forwards focus, and disposes it deterministically.

## Dominatus audit

The vendored and standalone repositories contain the same audio implementation at the audited commit. Source and tests show that `Dominatus.Actuators.Audio` is a provider-neutral generation package. It owns TTS/SFX requests, provider selection, consent/provenance policy, idempotent provider caching, concrete file artifacts, open sidecars, and safe actuation results. It does not own a realtime mixer.

`Dominatus.GodotConn.Audio` is the only playback connector found. It maps a generated path to Godot `AudioStreamWav`/`AudioStreamMP3`, binds a player per agent, optionally stops the old stream, starts playback, and records inspection counts. It has no shared voice pool, buses, music slot, fades, crossfade, dedupe, device ownership, or completion-on-natural-end bridge. No audio implementation was found in the MonoGame or Stride connectors. Ariadne/Dominatus already has typed `ActuationId`, `ActuationCompleted`, and wait-event machinery; future dialogue can marshal `AudioCompletion` into that application-facing completion path without calling gameplay from the device thread.

| Existing Dominatus concept | Current purpose | Reuse directly | Adapt | Reject for realtime |
| --- | --- | ---: | ---: | ---: |
| `AudioArtifact` | Generated file path, format, MIME, duration, rate, channels, size, sidecar |  | yes, at load boundary |  |
| `AudioGenerationMetadata` | Provider/model/voice provenance and idempotency |  | yes, correlation source |  |
| `AudioFormat` / MIME mapping | Generated output description | yes, in adapter validation |  |  |
| `FakeAudioProvider` | Deterministic WAV generation and cache proof | yes, integration fixture |  |  |
| provider registry and TTS/SFX commands | Generation routing and policy |  |  | yes |
| voice conditioning/consent | Generation ethics and provider policy |  |  | yes |
| sidecar writer | Open generated-artifact metadata | yes, upstream |  |  |
| generation cache | Idempotent provider work |  |  | yes; not a decoded-resource cache |
| generation actuation handler | Correlated generation request/result |  | future completion bridge | yes as playback API |
| Godot artifact loader | Godot WAV/MP3 stream creation |  | pattern only | yes outside Godot |
| registered Godot player handler | Agent-bound scene playback |  | pattern only | yes |
| Godot playback snapshot | Connector diagnostics |  | inspection precedent | yes as mixer state |
| MonoGame/Stride audio connector | No implementation found |  |  | n/a |

Classification: artifact/format metadata is **B, reusable with a bounded adapter**; provider, voice, sidecar, request and cache semantics are **C, generation-specific**; the fake provider is **A for deterministic generation proof**; Godot playback is **A only inside its connector and rejected for Aurelian realtime**. Nothing audited required deletion or owner-lane refactoring, so the Dominatus diff is zero.

## Package ownership and API

- `Aurelian.Audio` owns typed identities, PCM resources, WAV preparation, voice allocation, buses, fades, music policy, spatial coefficients, dedupe, inspection, completion queues, and null/offline output.
- `Aurelian.Audio.NAudio` is the Windows output leaf and owns `WaveOutEvent` plus the bounded PCM handoff.
- `Dominatus.Audio.Aurelian` is the one-way generated-artifact adapter. Aurelian has no provider dependency.
- `TinyFarm.Runtime` maps accepted `IntentResult` events to semantic cues. `TinyFarm.Core` remains free of Aurelian references.
- `Aurelian.GameHost` owns update, focus, and disposal of an optional `IAurelianAudioRuntime`.

Game code uses `AudioCue`, `SetMusic`, `CrossfadeMusic`, `SetBusVolume`, and typed `AudioAssetId`; filenames and backend handles never enter runtime cue calls.

## Runtime laws

`AudioAssetId`, `AudioEventId`, `AudioVoiceId`, and `AudioBusId` are distinct types. A resident `AudioClipResource` records content hash, sample rate, channel count, frame count, duration, samples, default loop policy, and preparation strategy. `AudioResourceScope` validates identity and metadata and deterministically owns decoded buffers. M4 decodes mono/stereo 16-bit PCM RIFF/WAV only. Authored files and Dominatus-generated WAVs use the same decoder. Ogg/MP3/FLAC decoding and long-resource streaming are deferred instead of importing codec or playlist scope.

One mixer serves SFX, music, ambient, UI, dialogue, and application-defined buses. The effective gain law is:

```text
voice gain * fade gain * bus gain * master gain * spatial attenuation
```

All public gains are finite `[0,1]`. Mute produces zero gain without corrupting voice state. Bus pause freezes that bus's cursor. Focus defaults to muting SFX and ambient projection while music/UI/dialogue continue; applications may choose `KeepPlaying`.

The default capacity is 32 voices. The default exhaustion law steals the lowest-priority, then oldest voice if incoming priority is at least as high; otherwise it rejects the incoming voice. `RejectNewest` is also available. Completion reasons are `Finished`, `Stopped`, and `Stolen`. Natural one-shots self-release. Loops wrap their prepared PCM cursor.

Music uses the Music bus rather than a second engine. `SetMusic` replaces current music; `CrossfadeMusic` fades every current Music voice down while the new loop fades up over the same explicit duration. Multiple ambient loops are ordinary bounded voices on the Ambient bus.

The listener is explicit application policy. Pan is `clamp(relativeX / panRange, -1, 1)` with linear stereo balance. Attenuation is one through near distance, linearly falls to zero at max distance, and remains bounded. These values are presentation metadata and do not query collision or mutate spatial state.

Event dedupe retains a bounded FIFO set of the most recent 4096 `AudioEventId` values. Re-projecting the same accepted result realizes it once; a different ID for the same asset realizes independently. Backend voice state is neither replay nor save truth. Replay re-projects semantic events. One-shot voices are not saved; music and ambience are re-established from session/scene state.

Diagnostics cover unknown assets, decode/format failures, device failure, voice capacity, disposed resources, invalid values, and duplicate events. Inspection exposes active typed voices, ages, gains, pan/attenuation, music state, submitted frames, and dropped/stolen/duplicate counts.

## Backend audit and decision

The audited choices were NAudio, miniaudio, OpenAL through Silk.NET/OpenTK, SDL-style output, and direct platform APIs. NAudio 2.3 is MIT, mature, and gives the smallest stable Windows implementation. It matches the current Windows priority and avoids writing WASAPI/WinMM interop. The engine contract keeps it in a leaf so it does not impose NAudio's architecture on game code.

Primary references: [NAudio repository and platform/package matrix](https://github.com/naudio/NAudio), [miniaudio supported platforms and integration model](https://github.com/mackron/miniaudio), [Silk.NET.OpenAL 2.23 package](https://www.nuget.org/packages/Silk.NET.OpenAL/), and [OpenAL documentation](https://www.openal.org/documentation/).

Miniaudio is a credible cross-platform C substrate, but selecting and maintaining a .NET binding/native packaging path is extra M4 risk. OpenAL is cross-platform and Silk.NET already exists here, but it would require explicit OpenAL Soft deployment plus more manual buffer/source/device lifetime code. Direct WASAPI duplicates mature library work. NAudio 3 advertises cross-platform core, NativeAOT compatibility, Linux ALSA output, and libsndfile formats, but adopting a newly split major line was unnecessary for this PCM-WAV Windows slice.

The Windows backend accepts 48 kHz interleaved stereo float frames into a two-second bounded `BufferedWaveProvider`; overflow discards rather than blocking gameplay. A NAudio device thread pulls those bytes. Mixer commands, resources, fades, allocation, diagnostics, and completion draining stay on the game/host thread. No gameplay callback occurs on the audio thread. Submission exceptions become diagnostics and do not crash authoritative simulation.

NativeAOT: `Aurelian.Audio` and null output use ordinary managed code and no reflection-heavy registration. The NAudio 2 Windows leaf is not claimed NativeAOT-qualified. Linux audio is not claimed; it needs a selected ALSA/OpenAL/miniaudio/NAudio-3 leaf and real-device CI. These two claims, plus streamed long music, are the exact bounded M4 remainder.

## Game and integration proof

`TinyFarmAudioProjector` emits `EnemyDefeated -> SwordSwing`, `ItemTaken -> Pickup`, `ActorMoved -> Footstep`, and crop/forage success to HarvestPop, plus explicit looping Farm music and positional River ambience. Only accepted results are traversed. IDs derive from stable intent sequence, event index, and kind, so rebuilding a projection dedupes. A rejected repeated attack emits no success cue. Core has no `Aurelian.Audio` reference.

The Dominatus proof generates a deterministic fake harvest WAV, adapts its artifact metadata, decodes it as an Aurelian resource, derives a stable correlation ID from generation idempotency metadata, and plays it through the same runtime as authored WAV.

The offline proof renders 500 ms of deterministic stereo PCM and records its hash. Tests cover identity, resource load, one-shot, loop, stop, completion, both capacity policies, bus/master gain, mute, fade, crossfade, pan, attenuation, dedupe, focus, pause, null/device failure behavior, disposal, unknown asset, invalid gain, generated-artifact adaptation, TinyFarm accepted/rejected projection, and 1000 one-shot lifecycles. The Windows test opens NAudio and submits 480 frames when a device is available; an unavailable device returns a concrete error without making CI depend on speakers.

## Validation and evidence

Compact evidence is under `artifacts/aurelian-game-audio-m4/`: `proof.json`, `dominatus-audit.json`, `mixer.json`, `resources.json`, `game-events.json`, and `manifest.json`. The generator is `tools/Aurelian.Audio.M4Evidence`.

Validation completed on Windows:

- `dotnet test Aurelian.slnx -m:1`: 713 passed;
- `dotnet test TinyFarm.slnx -m:1`: 307 passed;
- `dotnet test JointTaskForce.slnx -m:1`: 3,476 passed;
- standalone Dominatus audio owner tests: 48 passed on .NET 8 and 48 passed on .NET 10;
- NAudio device open and 480-frame submission: passed on the current machine;
- deterministic offline submission: 24,000 frames with PCM hash recorded in `proof.json`;
- `git diff --check`: passed.

## Exact next milestone

Proceed to `AURELIAN-SIMULATION-SCENE-KIT-M5`. Treat streamed music, compressed decoding, Linux device output, and NAudio-leaf NativeAOT as explicit later backend/resource qualifications, not prerequisites for the scene kit.
