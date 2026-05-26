# Machina Standard Layout Padding Hardening (M4c)

## Rule
- Style padding is paint metadata.
- Layout padding requires explicit layout structure.

## Component audit

| Component | Classification | Padding/layout behavior | Status |
|---|---|---|---|
| Card | B. content-hosting | Explicit anchored content region inset by theme spacing | Fixed in M4c |
| Button | A/C leaf/composed | No child hosting API; button text placement comes from button node semantics | No change |
| Badge | A leaf/composed label | Text leaf inside badge shell; no external child hosting | No change |
| Input | C composed control | Shell now hosts explicit anchored content region for text/placeholder | Fixed in M4c |
| Field | C composed control | Explicit column composition for label/control/description/error | No change |
| Checkbox | C composed control | Explicit row of box + label, explicit box child marker | Verified |
| Switch | C composed control | Explicit track/thumb and optional label row | Verified |
| Separator | A leaf visual | Pure visual line, no child hosting | No change |
| Label | A text leaf | No child hosting | No change |

## Notes
- Placement remains `AbsoluteFrame` / `AnchorFrame`.
- Sequential flow remains `StackArrange`.
- Direct stack children remain `FixedFrame` / `FillFrame`.
- StandardUI components remain local components; app documents place component hosts, not component internals.
