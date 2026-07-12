# Machina Layout Reference Audit M4b

## Imported reference material
- Source repo: https://github.com/yuechen-li-dev/MachinaLayout.JS
- Source ref: `main`
- Source commit: `deb2660b606a94aaaed8dc70ff095170f89306a8`
- Imported files: `frames-and-stack.md`, `row-model.md`, `adapters.md`, `machina-dispatch.md`

## JS reference summary
- `AbsoluteFrame` is explicit parent-local x/y/width/height placement.
- `AnchorFrame` requires exactly two horizontal and two vertical constraints.
- `FixedFrame` / `FillFrame` are valid stack direct-children only.
- `StackArrange` is ordered arithmetic (gap/padding/justify/align), not Flexbox.

## C# audit conclusion
- Core C# resolver already enforces strict `AnchorFrame`, `FixedFrame`, and `FillFrame` constraints.
- `StackArrange` behavior is aligned for direct-child validation and arithmetic layout.
- Hosted component lowering correctly scopes ids and makes the component root fill host bounds.

## Root cause identified for hosted component visuals
The hosted `SettingsCard` component used `StandardUI.Card` with style-only padding. That affected paint styling but did not create an explicit layout content region. As a result, inner stack children were laid out at the card's origin instead of an inset content region.

## Narrow fix applied
`StandardUI.Card` now lowers to:
1. card shell rect
2. explicit anchored content row inset by theme spacing
3. child content inside the inset row

This keeps frame/stack semantics and avoids renderer/layout-model rewrites.

## Guidance update
- Top-level placement remains `UiDocument` rows (`AbsoluteFrame`/`AnchorFrame`).
- Local sequential component layout should remain stack-based (`UI.Column`/`UI.Row`) with direct fixed/fill children.
- Component-local `UiDocument` is not required now; defer unless a future milestone needs row-level local authoring ergonomics.
\n## M4c layout-padding hardening note\n\nM4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (AnchorFrame), rather than relying on  to move children. Stack behavior remains ordered arithmetic () and is not Flexbox.\n

## M4c layout-padding hardening note

M4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (`AnchorFrame`), rather than relying on `UiStyle.Padding` to move children. Stack behavior remains ordered arithmetic (`StackArrange`) and is not Flexbox.
