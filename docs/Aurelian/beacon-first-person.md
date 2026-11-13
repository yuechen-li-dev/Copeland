# First-person presentation and environmental audio

Beacon's ordinary `BeaconShellGame` path now combines accepted movement and combat with spring motion, surface footsteps, water impacts and shelter ambience. This extends the [Saltline extraction](saltline-extraction.md) with independently authored engine code; no bundled implementation was copied.

## Play it

Run `beacon3d.cmd` from the repository root. Use **WASD** to move, **Shift** to sprint, **Space** to jump, **RMB** to aim, **LMB** to fire, and **R** to reload. **B** toggles presentation motion for comparison; **V** changes camera view. Effects volume controls both footsteps and ambience.

The spawn canopy covers a dry patch near Z = 9. Walk north out of it, then left toward X = -5, Z = 5 to cross exposed wet asphalt into the puddle. Footsteps change from dry to wet to splash using the rendered ground's shared samples. Splash steps inject bounded, expiring impulses into the existing water renderer. The same authored canopy bounds set ground exposure and the listener's quieter, filtered interior ambience. The outside layer is seeded wind, not rainfall or reverberation.

Normal geometric movement now walks at 4 m/s and sprints at 6 m/s. The optional humanoid locomotion bank retains its existing walk/sprint speeds. Published-lighting and GI research modes retain their reference ground rather than the new wet overlay.

## Engine ownership

`FirstPersonPresentation3D` in `Aurelian.Runtime` accepts feet position, yaw/pitch, grounded state, a monotonic accepted-shot counter and explicit aim/sprint/reload state. Advance it once per simulation tick with a finite step in (0, .05] seconds. `Reset` clears its history on a new run or replay reset; backwards shot counts and teleports over four metres also reset it. `FirstPersonProfile3D` bounds the bob, recoil impulse, spring frequency, stride lengths and FOV targets.

Distance advances stride and bob, so pushing against a wall produces no walking steps. Look deltas drive weapon sway. Analytic critically damped springs settle shot recoil and landing dip; exponential blends settle aim, sprint, reload and movement. Defaults blend from 60° to 48° for aim and 66° for sprint. Footstep and landing counters let a consumer project accepted events into its own surface and sound policy.

`FirstPersonPose3D` supplies camera offset, roll/FOV and weapon offset/angles. Beacon projects these into its existing geometric weapon and camera. Its canonical aim direction, projectile origins and combat state remain owned by `BeaconGame`. Repeated view projection advances neither motion nor simulation. Disabling motion keeps surface events and audio active, allowing the same combat/movement route to be compared.

`EnvironmentalAmbience` owns two voices on the existing **Ambient** bus. New `SetVoiceGain` and `SetVoicePaused` controls adjust only the owned voices, preserving shell volume, focus, pause, capacity and disposal behavior. Scene transitions no longer accumulate custom buses. `Reset` clears the blend without allocating replacement loops. `ProceduralSoundKind.Wind` prepares bounded, seeded, filtered gust noise during loading; playback uses the ordinary mixer.

Beacon's presentation adapter maps accepted shots, reload starts, distance steps and landings into prepared sounds. Motion advances on fixed ticks, while listener ambience uses the shell's explicit frame elapsed time. Menus pause both gameplay voices and presentation advancement. Restart clears transients and blend history, stops old effects and retains exactly two ambience loops.

## Boundaries

The wet ground is a visual overlay up to 4.5 cm above the existing flat collision floor; this does not add heightfield physics or evolving water. Shelter is authored, not inferred from arbitrary walls. The interior layer is filtered and quieter; there is no new propagation or reverb solver. Seeded loops have quiet endpoints, not a claim of seamless stationary recordings.

The geometric weapon still shares the world's depth pass and .1 m near plane. A separate viewmodel depth pass and wall avoidance remain future work. The optional skeletal weapon keeps its existing animation presentation. Aim changes FOV and model placement, without adding accuracy or spread rules. Small camera translation can create near-field parallax, while the canonical shooting ray stays unchanged. Human comfort, mix quality and difficulty still need hands-on judgment.

## Reproduce

```powershell
dotnet test Games/Beacon3D/Aurelian.Beacon3D.Tests -c Release -m:1
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release -- --playtest-script Games/Beacon3D/Aurelian.Beacon3D/Playtests/presentation.json --resolution 960x600 --output artifacts/local/beacon-presentation --save-root artifacts/local/beacon-presentation/saves
dotnet build Aurelian.Core.slnx -c Release -m:1
powershell -NoProfile -File scripts/Invoke-AurelianCoreTests.ps1
```

The native script uses the ordinary shell, physical input and Vulkan renderer. It captures covered spawn, puddle steps, aiming, recoil, reload, sprint, disabled motion, resize and restart, and asserts replay, accepted audio events, landing and focus-pause behavior. Scripted runs deliberately use silent device output; ordinary interactive launch uses NAudio. Integration tests capture actual mixer PCM to verify audible signal, silence under pause/Effects=0 and restored output. They also compare motion enabled/disabled simulation hashes and canonical aim.

Qualification on the Radeon 780M passes all 32 Beacon tests, 1,392 standalone engine tests across 20 assemblies, and six connected-weather integration tests. The core builds with zero warnings/errors and its 56-project closure reaches no game or sibling repository. The full Aurelian solution builds with zero errors and 14 existing vendored OpenFont warnings. The native script completes all 48 steps with nine captures under `artifacts/local/beacon-presentation`; its trace records 1 dry, 3 wet and 2 water footsteps, one landing, aim FOV 48.03° and sprint FOV 65.836°. Rewind restores all published facts, focus pause preserves them, restart clears the presentation counters, and native resize rebuilds the surface once. These are integration witnesses, not GPU performance or subjective comfort claims.
