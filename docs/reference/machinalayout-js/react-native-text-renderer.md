# React Native MachinaText renderer (`machinalayout/text/react-native`)

`MachinaNativeTextView` renders MachinaText content inside an already-owned rectangle using React Native primitives (`View`, `Text`). It is a renderer only: no sizing, measurement, or layout resolution.

## Import

```ts
import { MachinaNativeTextView } from "machinalayout/text/react-native";
```

## Peer dependency

Install `react-native` in your app. `machinalayout` declares it as an optional peer.

## Accepted `text` inputs

- `string`
- `MachinaTextSource`
- `MachinaTextSpec`
- `MachinaTextDocument`

## Supported rendering

- Paragraph blocks
- Bullet lists (including nested items)
- Inline nodes: strong, emphasis, code, link

## Policy support

- `variant`
- `wrap`
- `overflow`
- `align`
- `leading`
- `blockGap` / `listGap`
- `valign`

## React Native differences from DOM renderer

- Uses `Text`/`View` (no DOM nodes).
- No `href`/`target`/`rel` handling.
- Link interaction uses `onLinkPress(href)`.
- `overflow: scroll` is not implemented with `ScrollView` in this renderer.

## Non-goals

- Text measurement
- Intrinsic sizing
- Routing or navigation dispatch
- Editor/caret/selection behavior
