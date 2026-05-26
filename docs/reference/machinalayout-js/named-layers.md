# Named Layers (M6a)

Named layers add semantic paint grouping on top of existing node `z`.

## What layers do
- `layer` is optional node metadata.
- Layers affect paint order only.
- Geometry, placement, and coordinate spaces are unchanged.

## Layer and z bounds
- Layer registry z values are conceptually bounded to integer `-5..5`.
- Node `z` remains integer `-5..5`.

## Paint order
Siblings are painted by:
1. layer z ascending
2. node z ascending
3. original sibling order ascending

React CSS representation uses `zIndex = layerZ * 100 + nodeZ`.

## React adapter props
- `layers?: Record<string, { z: number }>`
- `defaultLayer?: string` (default: `"base"`)

If `layers` is omitted, behavior is equivalent to:

```ts
{ base: { z: 0 } }
```

## Fallback behavior
- Unknown node layer name: keeps declared name for metadata, layer z falls back to `0`.
- Invalid registry z (non-finite, non-integer, out of range): falls back to `0`.

## Important M6a limitations
- Not portals.
- No DOM reparenting.
- No root overlay container.
- No clipping escape. Overflow clipping from ancestors can still apply.
