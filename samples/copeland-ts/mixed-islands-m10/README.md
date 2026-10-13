# Copeland mixed framework islands M10

One unchanged Copeland source file mounts genuine React and Vue controls on the
same page. Both frameworks can edit one Copeland `state` owner. A second pair of
keyed editors demonstrates independent draft ownership, reordering, removal,
reinsertion, and retirement of pending HTTP completions.

Copeland owns immutable state, `on`, declared effects, contribution streams,
surface style/layout, child generations, and presentation acknowledgement.
Each framework retains its private renderer values and local control state.
React uses an actual Base UI input; the Vue control is a Vue 3 component.

From the repository root, with .NET 10, Node 26, npm, the repository `tspack`
command, and Microsoft Edge:

```powershell
pwsh -NoProfile -File samples/copeland-ts/mixed-islands-m10/run.ps1 -FullValidation
```

This builds and mounts both outer targets in all three emission profiles, runs
the mixed-island and preserved shared-source/React checks, then builds/tests the
whole .NET solution. Browser services are ephemeral and close on completion.
Use `-BrowserChannel` for another installed Playwright channel.

After building, run the demo in the foreground:

```powershell
npm run serve --prefix samples/copeland-ts/mixed-islands-m10
```

- React outer projection: <http://127.0.0.1:4190/react/diagnostic/>
- Vue outer projection: <http://127.0.0.1:4190/vue/diagnostic/>

Both pages contain both frameworks. Replace `diagnostic` with `symbolic` or
`production` to compare emission profiles. Stop the server with Ctrl+C.

Read `src/Application.tsx` first. The compiler driver declares typed npm
component contracts with `PresentationHost: true`; the physical host bindings
remain explicit. `registerIslandExecutor` lives in the backend's reusable
browser source and delegates mount/update/unmount accounting to the canonical
browser registry. The fixture does not publish a new TSPack or npm target.

The executor identity is open-ended. New frameworks implement the same
mount/update/unmount and exact-commit callback protocol; the outer host bindings
do not need their renderer values. Only the React/Vue light DOM combination is
qualified here. Named/scoped slots, shadow DOM style reach, managed bridges,
SSR/hydration, HMR, portals/Teleport, and `AttachmentsSettled` remain outside
this milestone. HTTP behavior is bounded demonstration behavior.
