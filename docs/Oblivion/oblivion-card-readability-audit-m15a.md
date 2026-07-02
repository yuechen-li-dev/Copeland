# Oblivion Card Readability Audit M15a

## Purpose

M15a isolates why current Oblivion compact cards are fast but not usable for real work.

This is an audit document only. It does not implement the card fixes.

## Card preview role

Compact cards are the primary browsing surface for the workbench.

They must let a user understand enough before selection to decide where to click next.

If the preview is unreadable, selection becomes a blind inspection workflow instead of a workbench workflow.

## Current preview failure

Current failure pattern:

- preview body areas are short
- plain preview lines are clipped instead of wrapped
- Markdown preview rows mix wrapped and non-wrapped rendering paths
- the preview body frame is dark
- Markdown summary text inherits dark default foreground from the light-surface theme

Result:

- previews stop carrying semantic value early
- some Markdown summary previews become dark-on-dark and unreadable
- users can see that content exists, but cannot actually read it

## Body text rendering

Plain preview bodies currently use:

- `UI.Text`
- `TextSize.Sm`
- `PresenterCardLayoutHelper.ClipLinesToFit(...)`

That gives bounded single-line output, but not readable browsing behavior for dense notebook text.

## Markdown preview rendering

Markdown previews currently use three separate modes:

- heading rows: single-line `UI.Text`
- code/diagnostic rows: single-line `UI.Text`
- summary rows: wrapped `StandardUI.TextBlock`

That inconsistency is the current root usability problem.

The summary path is also where the preview picks up dark default foreground on a dark frame.

## Word wrap expectations

Usable compact previews do not need full document rendering.

They do need one intentional rule:

- wrap within a bounded line budget, or
- elide within a bounded line budget

Current behavior is mixed and therefore not legible enough.

## Clipping expectations

Text must never appear clipped only because no consistent preview rule was chosen.

Current presenter card naming suggests clipping intent, but card content is not backed by a strong clipping contract yet.

M15b should treat clipping as an explicit behavior, not an accidental side effect of small boxes.

## Contrast expectations

Any dark preview background must provide explicit readable light foreground colors.

Preview text must not rely on default light-surface theme foreground values when it is drawn on dark frames.

## Inspector relationship

The inspector is currently more readable than the compact preview because it usually renders on light surfaces and uses the richer wrapped text path.

That means the workbench already has a better readability target inside the same subsystem.

M15b should align compact preview readability with that stronger inspector baseline.

## Minimal acceptance for usable cards

A compact card is minimally usable only if:

- title and subtitle remain readable
- preview body text is understandable before selection
- Markdown summary text has readable contrast
- text wraps, elides, or clips intentionally
- no preview body text degenerates into accidental bars
- no text visibly spills outside the intended card body

## Deferred work

- full compact preview implementation changes
- richer preview ranking or semantic summarization
- density/font scale controls
- Markdown editing
- execution/runtime features

## M15b follow-through

M15b follows through on this audit by making compact preview text readable enough for real scanning:

- preview body text now wraps or intentionally elides inside the card body
- preview foregrounds are explicit on dark preview frames
- Markdown preview rows use a more consistent bounded-preview policy
- the milestone keeps the work scoped to readability and controlled resizing only

It does not turn compact cards into full document renderers, editors, or execution surfaces.
