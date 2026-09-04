# Upstream provenance

- Project: `LayoutFarm/Typography`
- Source directory: `Typography.OpenFont`
- Upstream URL: https://github.com/LayoutFarm/Typography
- Pinned commit: `5877180c7c5271091379a0eaf9f03ab6ebd256b3`
- Commit date: 2023-09-17
- Package source import date: 2026-09-03

The files under `Upstream/` originate from that exact revision. Downstream semantic
changes are listed in `PATCHES.md`; packaging metadata and documentation live outside
`Upstream/`.

The pinned upstream implementation of `Tables/HorizontalMetrics.cs` follows the
OpenType `hmtx` rule: after reading `numberOfHMetrics` long records, it repeats the
last advance width for all remaining glyph IDs while reading their individual left
side bearings.
