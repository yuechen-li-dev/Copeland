# React Native adapter (`machinalayout/react-native`)

`MachinaReactNativeView` renders a `ResolvedLayoutDocument` into nested React Native `View` wrappers.

## Shared model boundary

- Uses the same Machina records and resolved rectangles as web/React/Vue adapters.
- The adapter maps resolved rectangles into React Native style objects.
- React Native components are the view payloads rendered inside those rectangles.

## Import

```ts
import { MachinaReactNativeView } from "machinalayout/react-native";
```

## Peer dependency

Install React Native in your app. `machinalayout` declares `react-native` as an optional peer.

## Basic example

```tsx
const views = { Panel: PanelView };

<MachinaReactNativeView
  layout={layout}
  views={views}
  viewData={{ Panel: { title: "Now Playing" } }}
  nodeData={{ sidebar: { selected: true } }}
/>;
```

## Stable view registry guidance

Keep `views` stable (component references). Send changing values through `viewData` and `nodeData`, not by recreating inline component functions.

## Supported concepts

- `view ?? slot` lookup
- `viewData` / `nodeData`
- layer + node z sorting
- parent-local coordinate normalization
- optional debug wrappers

## Host renderer differences vs DOM adapters

- Uses React Native `View` wrappers and numeric style values.
- No DOM attributes/class hooks.
- No DOM containment/content-visibility behavior because RN is not DOM.

## Not included

- this package only renders layout boxes; text rendering is provided separately by `machinalayout/text/react-native`
- portals/reparenting
- DOM-only features
