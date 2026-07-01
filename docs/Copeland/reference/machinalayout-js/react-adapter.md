# React adapter (`machinalayout/react`)

## Shared model boundary

- Layout geometry is authored and resolved by Machina core (`LayoutRow[]` -> resolved rectangles).
- React supplies components that render inside those resolved rectangles.
- The adapter does not introduce a React-specific layout model.
- Preferred import path is `machinalayout/react`.
- Root import compatibility remains during `0.x`.

## Basic usage

```tsx
import { resolveLayoutRows } from "machinalayout";
import { MachinaReactView } from "machinalayout/react";

const layout = resolveLayoutRows(rows, rootRect);
const views = { Header, Sidebar, Inspector };

<MachinaReactView layout={layout} views={views} />;
```

Effective render key is `view ?? slot`.

## Stable view registry and data channels

`views` should hold stable component references. Pass dynamic values through data channels.

Good:

```tsx
const views = { Inspector };

<MachinaReactView
  layout={layout}
  views={views}
  viewData={{ Inspector: inspectorData }}
  nodeData={{ sidebar: sidebarData }}
/>;
```

Bad (new component identity every render):

```tsx
const views = {
  Inspector: () => <Inspector value={value} />,
};
```

## Slot props

Slot views receive:

- `id`
- `rect`
- `debugLabel`
- `node`
- `viewKey`
- `viewData`
- `nodeData`

## Coordinate normalization

Core resolved rectangles are root-space coordinates.

The adapter renders nested absolutely positioned DOM wrappers in parent-local space:

- `left = node.rect.x - parent.rect.x`
- `top = node.rect.y - parent.rect.y`

The outer wrapper represents root coordinate space; the root node itself renders at local `left: 0`, `top: 0`.

## DOM renderer policy boundary

Containment/content-visibility are DOM renderer policies implemented by the adapter layer.

Allowed for Machina wrappers:

- position (`relative`/`absolute`)
- `left` / `top` / `width` / `height`
- `box-sizing`
- `z-index`
- containment/content-visibility
- optional debug/cosmetic wrapper styles

Not used as geometry authority:

- flexbox/grid/margins/transforms
- DOM measurement for layout solving
- CSS classes determining solved geometry

React components render payload UI inside adapter-owned rectangles; React does not own outer layout geometry.
