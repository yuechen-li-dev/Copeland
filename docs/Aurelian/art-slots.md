# Art slots

Engine and game work often needs art before the final art exists. An **art slot** is a named piece of 2D art with an
explicit placeholder. The placeholder is a hand-written SVG that fixes what code depends on: canvas size,
silhouette, proportions, where the feet or pivot sit, the pose grid, and the rough palette regions. Painted art
later replaces it "within the lines" without any code change.

The code lives in `Aurelian.Assets` (`Aurelian.Assets.ArtSlots`). TinyFarm is the first user
(`Games/TinyFarm/TinyFarm/Assets/art/art-slots.json`).

## Manifest

```jsonc
{
  "schemaVersion": 1,
  "slots": [
    {
      "id": "forage.riverside-hen-of-the-woods",       // stable id; the game decides the naming scheme
      "placeholder": "placeholders/riverside-hen-of-the-woods.svg",
      "final": "riverside-hen-of-the-woods.png",        // absent until painted
      "finalSha256": "…",                               // required with final
      "width": 128, "height": 112,                      // the placeholder canvas (its viewBox)
      "columns": 1, "rows": 1,                          // pose or animation grid, optional
      "worldHeight": 0.95,                              // metres of the tallest cell's opaque content, optional
      "usage": "How the game uses it: scale, anchor, layering, frames.",
      "brief": "What it should look like: subject, style, palette, mood."
    }
  ]
}
```

Comments are allowed. Paths are relative to the manifest.

`ArtSlotCatalog.Load` validates the manifest: unique ids, sizes, grids that divide the canvas, a usage and a brief,
present files, and the final file's hash. `Realize(id, scale)` returns straight-alpha RGBA. If there is final art it
is decoded at its own resolution, which must keep the slot's aspect ratio within 1%. Otherwise the placeholder is
rasterized at `width × scale` by `height × scale`. `IsPlaceholder` says which one you got. `Pending` lists slots
that still show their placeholder.

## Writing a placeholder (engine and game developers)

- The SVG's `viewBox` must be `0 0 width height`, exactly the slot canvas.
- Use a transparent background for sprites.
- Draw flat, readable shapes with a dark outline. Keep the real silhouette and proportions, and put the feet or
  contact point where the game will anchor the art. TinyFarm anchors sprites bottom-centre and trims transparent
  margins per cell.
- For pose sheets, fill every cell, even with near-copies, so code can index any frame.
- Do not draw text, guides or labels into the SVG. The brief and usage in the manifest are the instructions.

## Painting a final (the art pass)

1. Run the exporter:

   ```
   dotnet run --project tools/Aurelian.ArtSlots -- Games/TinyFarm/TinyFarm/Assets/art/art-slots.json --out artifacts/art-slots
   ```

   It writes three things:
   - `pending.md`: the worklist, with every pending slot's usage and brief.
   - `placeholders/<id>.png`: each placeholder rasterized at 4×. Use it as the init or control image (img2img,
     ControlNet line or segmentation) so the painting keeps the silhouette and pivot.
   - `contact-sheet.png`: every slot as the game shows it now. Magenta frames mark placeholders.
2. Paint the final at any resolution with the same aspect ratio and a transparent background. Follow the game's house
   style; TinyFarm's is stated at the top of its manifest. Pose sheets keep the same grid and row order.
3. Save the PNG beside the manifest. Set `final` and `finalSha256` (`sha256sum file.png`). Keep the placeholder SVG.
   It documents the composition and is what you go back to if the final is ever withdrawn.
4. Run the game's tests. TinyFarm's `TinyFarmArtSlotTests` realize every slot, and the contact sheet should show the
   frame gone.

An overwritten or truncated final fails `Load` (hash mismatch) instead of shipping silently. A final with the wrong
proportions fails `Realize`.

## TinyFarm naming

| Id | Dresses |
| --- | --- |
| `object.<scene object>` | A scene object, for example `object.reeds` |
| `forage.<forage node>` and `….depleted` | A forage node while it can be gathered, and after |
| `item.<item>` | A ground item, for example `item.wild-mint` |
| `actor.<actor>` | A 4 × 4 walk sheet (rows down, up, left, right) |
| `prop.<name>` | Presentation-only props, for example `prop.riverside-bench` |

The presenter looks these ids up as it draws, so adding a slot with a matching id is enough to dress a new object.

## Reviewing generated walk sheets

`Aurelian.ArtSlots review-sheet <4x4.png> --out <directory>` imports a four-direction sheet with the same
`GridSpriteSheet.Import` used by TinyFarm. It writes a geometry report, all sixteen poses at game size, and four
animation PNGs for loop inspection. Empty cells, content touching cell boundaries and missing transparency fail the
geometry gate. Facing, opposite steps and consistent identity need visual review.

The [TinyFarm character workflow](../research/tinyfarm-character-sprite-workflow.md) supplies a generated blank
template derived from Gardener, the full Mara prompt sequence, commands, a looping preview and native evidence.
The template guides generation; reviewing the actual imported cells is still necessary before pinning final art.
