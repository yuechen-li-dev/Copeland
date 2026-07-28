# Copeland workspace M0

`tsconfig.tsx` is the source of truth. Run `tscl workspace sync` before using
the generated TypeScript config or building `App.csproj`.

The fixture deliberately keeps `src/legacy` on TypeScript and compiles only
`src/copeland` through the existing Copeland MSBuild item seam.
