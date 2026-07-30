# Copeland website — canonical stream/table layout M0

`src/App.tsx` contains ordinary React content components and three explicit
stream roots: `CopelandDesktop`, `CopelandTablet`, and `CopelandMobile`.
The compiler owns their neutral hosts, topology, geometry, bounded feature
collections, paint order, and `with` derivations. Components do not import
generated positional classes or receive layout classes.

The profile law is `<600` mobile, `600–1023` tablet, and `>=1024` desktop.
`src/Main.ts` uses explicit state capture to select the profile. The streams
dogfood `centerXIn`, `placeRightOf`, `placeBelow`, and `expandFrom`.

Run from the sibling TSPack checkout:

```powershell
go run ./cmd/tspack build --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 browser
go run ./cmd/tspack scenario C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0\scenarios\layout-profiles-m1.json --run site --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0
go run ./cmd/tspack run --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 browser-proof --once
```

Screenshots and numerical rectangle evidence are generated under
`artifacts/cts-website-table-layout-m0/` and ignored by Git.

After the normal TSPack build materializes the compiler context, inspect the
same website project without starting a browser:

```powershell
$cli = C:\Users\yuech\source\repos\Copeland\src\Copeland\Copeland.Cli\bin\Debug\net10.0\Copeland.Cli.exe
& $cli table list --project .\manifest.tsx --format json
& $cli table rows layout::Boxes --project .\manifest.tsx --format json
& $cli layout inspect CopelandDesktop --project .\manifest.tsx --json
```

`--source .\src\App.tsx` discovers this manifest upward. Both forms reopen the
materialized project contracts and report the same `graphFingerprint`; neither
performs package installation or browser lifecycle work.
