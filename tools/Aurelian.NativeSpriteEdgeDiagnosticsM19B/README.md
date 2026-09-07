# Native Profile edge diagnostics

This permanent inspection tool renders real Mossward Profile assets on a
controlled matte and compares CPU contours with three native reconstruction
laws: legacy glyph compensation, M19B neutral threshold, and M19C derivative
coverage.

Run the default worker/tree/building suite:

```powershell
dotnet run --project tools/Aurelian.NativeSpriteEdgeDiagnosticsM19B -c Release
```

Select assets and inspection zoom:

```powershell
dotnet run --project tools/Aurelian.NativeSpriteEdgeDiagnosticsM19B -c Release -- --asset worker --asset tree --asset hq --zoom 16
```

Supported zoom values are `1`, `2`, `4`, `8`, and `16`. Enlargement is always
nearest-neighbor so the inspector cannot introduce blur. Each comparison sheet
is ordered CPU, legacy, neutral, derivative, amplified CPU/derivative diff.
Dark and light matte versions are emitted, along with the source atlas.

The tool always runs the direct comparison because the modes are most useful
as a controlled set rather than mutually exclusive runs. It also emits:

- silhouette IoU, alpha coverage, contrast, and transition width;
- worker scale, subpixel X/Y, and field-size sweeps;
- a synthetic square Profile control;
- individual before/after/diff images;
- cache/upload and bounded frame timing measurements;
- copies of current Mossward and TinyFarm native proof frames when available.

Results are written to
`artifacts/aurelian-profile-derivative-reconstruction-m19c`. Interpret
transition width as the mean count of partially covered edge pixels per scan
line: lower is sharper, but a clipped zero-width result would be aliasing, not
success. IoU guards the silhouette while light/dark mattes expose halos.
