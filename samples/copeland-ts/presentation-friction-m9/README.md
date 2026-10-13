# Copeland presentation friction M9

One unchanged Copeland source set runs against React and Vue. Application code
returns the compiler-owned `View` type, uses semantic UI records, layout
templates, `with`, `state`/`on`, declared effects, and explicit captures. It
does not import a framework factory. Optional XML stays in the projection.

The issue list/detail surface gets typography, wrapping, spacing, fill, border,
and radius from its Copeland layouts. `control-theme.css` still supplies shell
and control styling. Actual Base UI React components and Vue SFC/Reka components
implement the same typed control contract in separate physical adapters.

From the repository root, with .NET 10, Node 26, npm, the repository `tspack`
command, and Microsoft Edge:

```powershell
pwsh -NoProfile -File samples/copeland-ts/presentation-friction-m9/run.ps1 -FullValidation
```

This compiles the shared sources for both targets and all three emission
profiles, checks/bundles the physical TypeScript/Vue support, runs mounted
browser qualification, runs the preserved React regression fixtures, and
builds/tests the full .NET solution. Qualification
starts an ephemeral local service and closes it on completion. A different
installed Playwright browser channel can be selected with `-BrowserChannel`.

After building, run the demo in the foreground:

```powershell
npm run serve --prefix samples/copeland-ts/presentation-friction-m9
```

- React: <http://127.0.0.1:4189/react/diagnostic/application/>
- Vue: <http://127.0.0.1:4189/vue/diagnostic/application/>
- Idiomatic Vue baseline: <http://127.0.0.1:4189/baseline/>
- Effects/children: <http://127.0.0.1:4189/react/diagnostic/lifecycle/>
  and <http://127.0.0.1:4189/vue/diagnostic/lifecycle/>

Replace `diagnostic` with `symbolic` or `production` for another profile.
Stop the foreground server with Ctrl+C.

Read `src/Application.ts`, `src/IssuesPage.ts`, and `src/State.ts` first.
`src/UI.tsx` projects the shared semantic records. `src/Lifecycle.tsx` exercises
effects and keyed owners through the adapter's child/slot convention.
`tooling/compiler/Program.cs` selects the target using the public compiler API.

Adapter identities are open strings. React and Vue lifecycles are qualified;
the compiler also checks an arbitrary stateless factory contract. Other
frameworks and simultaneous framework islands are not claimed as implemented.
This remains a bounded compiler API fixture, not a new production TSPack target.
The sample HTTP service is reproducible demonstration behavior, not storage.
See `docs/Copeland/reviews/cts-presentation-friction-m9.md` for design, evidence,
and the next interop boundary.
