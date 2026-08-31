# From one card to a notebook stack

The second Card is intentionally ordinary. It uses the same shell, the same Markdown presenter, and the same session-state rules as the first Card. Its presence qualifies the stack rather than introducing another content kind.

## What the stack proves

- Card order remains deterministic from materialization through recomposition.
- Either Card can expand or collapse without changing the other Card's state.
- Normal vertical layout moves later Cards when an earlier Card changes height.
- The page owns overflow when the combined stack is taller than the viewport.

Selection is restrained and independent from expansion. After a resize, the selected Card and both expansion states survive while widths and vertical positions are recomputed from the current viewport.
