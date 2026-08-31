# Oblivion two-card stack MVP — M19h

## Goal

M19h establishes Outcome A for one standalone Oblivion Page containing exactly two semantic Markdown Cards in one vertical stack. It preserves the M19g Machina Card shell and mature Avalonia document path while adding only independent Card state, stack recomposition, restrained selection, and page-level overflow.

The live proof has no Presenter shell, inspector, sidebar, tabs, comparison surface, diagnostics panel, or additional content kind.

## Initial state

Every fresh process starts deterministically with both Cards collapsed. Card A is selected through the existing session reconciliation default. No dogfood or persisted process state is restored.

The stable order is:

1. `The physical atom of Oblivion`
2. `From one card to a notebook stack`

## Expansion decision

M19h explicitly chooses `MULTI_EXPAND`. Each Card owns its own `OblivionCardViewState`, so collapsed/collapsed, expanded/collapsed, collapsed/expanded, and expanded/expanded are all valid. Expansion never selects a Card implicitly.

## Shared style contract

`Styles.cs` owns one immutable `OblivionStandaloneStyle` record for the M19h viewport, margins, Card heights, 24px stack gap, prose cap, shell colors, subtitle, and selected border. Rendering, Avalonia composition, tests, and manifest values consume that object rather than duplicating milestone constants.

## Stack geometry

Machina owns one normal `VStack`. Both Cards use the same renderer and options. A collapsed Card is 174px high; an expanded Card is 760px high. Expanding Card A therefore moves Card B downward by 586px. Collapsing A restores B to its original position. The stack computes a content extent from current Card heights rather than retaining absolute Card positions.

## Stack gap

The inter-Card gap is always 24px. Tests measure the second Card's top against the first Card's bottom in collapsed, mixed, and both-expanded states and reject overlap.

## Page scrolling

The standalone window contains one Avalonia page `ScrollViewer` around the complete Machina shell plus mature document controls. Both expanded Cards produce a 1688px page extent at the 1440px proof size, so the page can scroll from Card A through Card B. The standard dark Fluent control theme is installed so Avalonia realizes a functioning ScrollViewer template.

The host routes wheel input explicitly. If the pointer is over a document that can still scroll in the requested direction, that local document owns the wheel. If the document has no overflow or has reached the relevant edge, the page owns the wheel. Card shells and the stack gap always route to the page. Collapsed Cards mount no document scroller.

## Local content scrolling

Both expanded Cards use `AvaloniaReadOnlyDocument` through `AvaloniaOblivionContentHost`. The M19h proof documents fit their 760px Cards, so local scrolling remains idle and page scrolling is the primary behavior. Focused routing tests cover genuine local overflow and both local-scroll boundaries.

## Selection behavior

Clicking a Card shell or body selects that Card. Clicking its square affordance changes expansion only. The selected treatment is a two-pixel blue border that preserves the caller's dark Card background. Selection remains independent of expansion and survives recomposition.

## Resize behavior

Window resize recomputes the shared Card width as viewport width minus two 88px margins. Both Cards receive the same width. Card order, per-Card expansion, selected Card, stack gap, and centered 1040px prose cap survive maximize/restore and ordinary size recomposition.

## Visual findings

Codex reviewed the collapsed, both-expanded, and scrolled 2560×1440 captures at original resolution. The final proofs show identical shells, aligned square controls, a consistent 24px gap, no collapsed body previews, mature heading/paragraph/list typography, restrained selection, and no frame overlap. In the both-expanded proof, Card B begins below Card A and continues naturally beyond the viewport. The scrolled proof shows the page displaced to its lower bound with Card B readable and the vertical page scrollbar visible.

## Bugs found and fixed

- The shared selected Card theme hard-coded a near-white background. Selection now retains the caller's background and strengthens only the border; the standalone style owns its selected color.
- The standalone Avalonia application had no control theme, leaving ScrollViewer extent and viewport at zero. Installing Avalonia's standard dark Fluent theme realizes the mature scroll controls; the both-expanded capture reports a 1688px extent.
- Short document ScrollViewers could ambiguously retain wheel input. Boundary-aware routing now delegates to local content only when it can move and otherwise advances the page.
- Modified Space shortcuts such as `Alt+Space` could toggle the selected Card. Expansion keyboard handling now requires no modifiers.

## Tests

Sixteen focused standalone test cases cover exact two-Card Markdown materialization, stable order, the retained M19g one-Card contract, deterministic collapsed launch, all independent expansion combinations, mature presenter selection, A-to-B movement, collapse restoration, page overflow, no overlap, fixed gap, selection and expansion through resize, identical responsive widths, square controls, page session offset, wheel ownership boundaries, and Avalonia-free semantic assemblies.

Canonical playback passed 14/14 with zero failures or skips. All required solution tests and builds passed, and `git diff --check` passed.

## Remaining limitations

M19h intentionally remains exactly two Markdown Cards. Expansion, selection, and page offset are session-only. The proof content does not overflow an individual Card, although the mature local scroller and its ownership boundaries are retained and tested. No persistence, additional content kind, editing, execution, or notebook chrome is introduced.

## Next step

Recommended M19i: load the same two-Card Page through a bounded real notebook workspace/session entry point while preserving this standalone shell, exact Card contract, stack ownership, and zero new presentation kinds. This would qualify semantic Page loading without diluting the now-proven stack interaction.
