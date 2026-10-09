# Composable game starter

The starter composes runtime fragments with a typed C# scene. Run `starter.cmd`
from the repository root, or `humanoid-starter.cmd` for the optional untextured
humanoid player and local Mixamo locomotion bank. The latter uses the corrected sibling Aetheris locomotion body;
pass `--humanoid <path> --locomotion <matching-bank>` to select another compatible profile explicitly.

WASD moves, Left Shift sprints with the locomotion bank, mouse movement turns, Space jumps, V switches camera, left mouse
aims/fires and R reloads. Escape pauses. The title and pause menus expose the
shared Deliverance save/load slots.

```powershell
.\humanoid-starter.cmd --playtest-script Games/Starter/Playtests/humanoid-proof.json --output artifacts/local/playable-humanoid-starter --save-root artifacts/local/playable-humanoid-starter/saves
```

See [game composition](../../docs/Aurelian/game-starter.md) and
[humanoid presentation](../../docs/Aurelian/humanoid-animation.md) for C# APIs,
replaceable clips, named joint attachments and current limits.

The corrected Mixamo bank passes its dense surface qualification. See
[locomotion](../../docs/Aurelian/humanoid-locomotion.md) for baking, native proof,
GPU parity evidence and the scope of the animation qualification.
