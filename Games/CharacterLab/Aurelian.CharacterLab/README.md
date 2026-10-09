# Character Lab

An untextured Antonia character animated by a scene-owned Dominatus agent and
drawn with Vulkan compute skinning. The idle/walk/aim demonstration repeats
automatically; press V to switch to manual controls. WASD selects walking, hold
the primary mouse button to aim, arrow keys orbit the camera, and Esc pauses.
Walking is an in-place presentation clip; this lab does not run a movement motor.
Input passes through the shared InputMan adapter and game controls.

From the Copeland root:

```powershell
.\characterlab.cmd
.\characterlab.cmd --body C:/path/to/antonia.gameplay-body.json
.\characterlab.cmd --proof --record
.\characterlab.cmd --launch-smoke
python scripts/assemble-character-animation.py artifacts/local/character-animation
```

The launcher defaults to the retained Aetheris gameplay body under the sibling
checkout. It does not download a character or silently substitute another mesh.
If that artifact is missing, reproduce it using Aetheris's
`scripts/qualify-antonia-gameplay.ps1`; see its `docs/public/humanoid-gameplay-body.md`.
Shader compilation uses the existing DXC and SPIR-V validation toolchain.

`--proof` uses the actual Vulkan compute/draw path for 240 frames, compares every
output corner against the Aetheris CPU reference, screens every animated pose,
and verifies four additional active-corrective cases with world rotation and
translation. It writes fail-closed `evidence.json`, shader/body hashes, captured
images and Dominatus inspection data under `artifacts/local/character-animation`.
`--record` saves every fourth frame for the optional GIF encoder. The hidden
window smoke test independently verifies four swapchain presentations.

The reusable implementation lives in `src/Integrations/Aurelian.Humanoid`;
this application owns only controls, camera, the demonstration script and its
qualification scenario. See [the runtime guide](../../../docs/Aurelian/humanoid-animation.md).
