# Vue MachinaText renderer

`MachinaVueTextView` renders MachinaText content into DOM elements inside an already-owned rectangle.

## Import

```ts
import { MachinaVueTextView } from "machinalayout/text/vue";
```

## Peer dependency

- `vue` (Vue 3)

## Accepted `text` inputs

- `string`
- `MachinaTextSource`
- `MachinaTextSpec`
- `MachinaTextDocument`

## Supported rendering

- paragraphs (`p`)
- bullet lists (`ul`/`li`)
- inline strong/emphasis/code/link

## Policy support

- `variant`
- `wrap`
- `overflow`
- `align`
- `leading`
- `blockGap`
- `listGap`
- `valign`

## Notes

- Uses Vue `h()` internally so app code does not need render-function loops for the text AST.
- Supports `rootClass`/`rootStyle` plus `linkClass`/`linkStyle` and `codeClass`/`codeStyle` hooks.
- Link handling uses `onLinkClick` only (no routing dispatch integration).

## Difference from React DOM text renderer

- Vue component API (`rootClass`/`rootStyle`) instead of React `className`/`style` prop names.
- Same parser/document model and text policy behavior.

## Non-goals

- text measurement
- intrinsic sizing
- routing behavior
- editor/caret/selection behavior
