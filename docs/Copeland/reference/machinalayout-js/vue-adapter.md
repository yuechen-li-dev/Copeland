# Vue adapter (`machinalayout/vue`)

`MachinaVueView` renders a `ResolvedLayoutDocument` into DOM wrappers plus Vue view components.

- Import path: `import { MachinaVueView } from "machinalayout/vue";`
- Peer dependency: `vue` (`>=3.4 <4`).
- Machina layout stays record-shaped (`LayoutRow[]` -> resolved rectangles) across frameworks.

## Shared model boundary

MachinaVueView uses Vue render functions internally so application code can stay record-shaped: layout rows describe geometry, and Vue components render inside the resolved rectangles.

You do not need to write `h()` calls, template layout loops, or directive ceremony to place boxes. Users can keep normal Vue SFC/template authoring for component internals, reactivity, and lifecycle.

## Basic usage

```vue
<script setup lang="ts">
import { MachinaVueView } from "machinalayout/vue";

const views = { Panel };
</script>

<template>
  <MachinaVueView :layout="layout" :views="views" :view-data="viewData" :node-data="nodeData" />
</template>
```

## Stable view registry and data channels

Keep stable component references in `views`. Pass reactive/computed dynamic values through `viewData` and `nodeData`.

Avoid rebuilding component definitions as data carriers.

## Props note

To avoid conflicts with Vue fallthrough attrs, root/node styling props are:

- `rootClass`, `rootStyle`
- `nodeClass`, `nodeStyle`

## Supported

- `view ?? slot` lookup
- `viewData` / `nodeData`
- layer/z sorting (`layer z`, then `node z`, then sibling order)
- DOM containment/content-visibility policy
- debug mode
- parent-local coordinate normalization

## Not included

- this package only renders layout boxes; text rendering is provided separately by `machinalayout/text/vue`
- portals/reparenting
- router/state abstraction layers
