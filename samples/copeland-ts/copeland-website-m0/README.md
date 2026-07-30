# Copeland website — lexical component capsule M1 dogfood

`src/App.tsx` contains ordinary typed render functions and three explicit
stream roots: `CopelandDesktop`, `CopelandTablet`, and `CopelandMobile`.
`FeatureCard(props)` and `Hero(profile)` each declare a private local
`Surface` stream, capture their typed arguments lexically, and explicitly
`return Surface()`. The page owns only each component's outer host; the local
stream attaches an opaque React-backed child inside that host.

The profile law is `<600` mobile, `600–1023` tablet, and `>=1024` desktop.
`src/Main.ts` uses explicit state capture to select the profile.

Each profile root is fixed to its viewport and owns one `page` `scrollY`
surface. The page's taller `content` box is deliberately stable and scrolls
locally. Hero text uses fixed local profile hosts, `clamp`, and line clamping;
actions remain separate from text so copy changes do not alter page geometry.

The compiler projects component definitions, stream-attached instances,
private local presentations, and lexical captures. Parent placement stays a
neutral generated host; private stream wrappers flatten in browser realization
so the parent sees only the assigned host geometry.

Run from the sibling TSPack checkout:

```powershell
go run ./cmd/tspack build --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 browser
go run ./cmd/tspack scenario C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0\scenarios\layout-profiles-m1.json --run site --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0
go run ./cmd/tspack run --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 browser-proof --once
```

Screenshots and numerical rectangle evidence are generated under
`artifacts/cts-web-content-fit-m0/` and ignored by Git.

After the normal TSPack build materializes the compiler context, inspect the
same website project without starting a browser:

```powershell
$cli = C:\Users\yuech\source\repos\Copeland\src\Copeland\Copeland.Cli\bin\Debug\net10.0\Copeland.Cli.exe
& $cli table list --project .\manifest.tsx --format json
& $cli table rows layout::Boxes --project .\manifest.tsx --format json
& $cli table rows text::Documents --project .\manifest.tsx --format json
& $cli table rows text::Blocks --project .\manifest.tsx --format json
& $cli table rows text::Inlines --project .\manifest.tsx --format json
& $cli table rows text::Bindings --project .\manifest.tsx --format json
& $cli table rows component::Definitions --project .\manifest.tsx --format json
& $cli table rows component::Instances --project .\manifest.tsx --format json
& $cli table rows component::Bindings --project .\manifest.tsx --format json
& $cli table rows component::Captures --project .\manifest.tsx --format json
& $cli table rows component::LocalPresentations --project .\manifest.tsx --format json
& $cli layout inspect CopelandDesktop --project .\manifest.tsx --json
```

`--source .\src\App.tsx` discovers this manifest upward. Both forms reopen the
materialized project contracts and report the same `graphFingerprint`; neither
performs package installation or browser lifecycle work.
