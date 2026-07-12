# Machina Presenter M3f — standard control skin hardening

## Scope

M3f hardens control chrome for composed checkbox and switch controls without adding new renderer primitives.

- Rendering remains rectangular-only (`FillRect`, `StrokeRect`, `DrawText`).
- No rounded corners, line primitives, animation, or font/backend changes.
- Flat row-first composition remains canonical.

## Control model

### Checkbox

Checkbox is composed from row parts (`email-row`, `email-box`, `email-label`) with `StandardView.CheckboxBox` metadata.

- Outer 18x18 square: light background + medium border.
- Checked state: centered inner fill communicated through style metadata (`Foreground` + inset `Padding`).
- Unchecked state: transparent inner fill.

### Switch

Switch is composed from row parts (`notifications-row`, `notifications-track`, `notifications-thumb`, `notifications-label`) with `StandardView.SwitchTrack` and `StandardView.SwitchThumb` metadata.

- Track 42x20: border and stateful background.
- Thumb 16x16 with inset 2, white fill and border.
- OFF: pale track + thumb at left.
- ON: dark track + thumb at right.

## Notes

M3f is intentionally rectangular-only skin hardening. Rounded corners remain out-of-scope for this milestone.
