# Copeland TS / Vue friction M8

The existing issue-triage page has two runnable implementations: an idiomatic
Vue 3 SFC baseline and a Copeland TS application using the Vue renderer. The
Copeland application owns immutable state, typed events, explicit captures,
layout templates, layout overrides, and semantic presentation records. Vue
renders the resulting nodes and supplies real imported components.

From the repository root, using .NET 10, Node 26, npm, the repository `tspack`
command, and Microsoft Edge:

```powershell
pwsh -NoProfile -File samples/copeland-ts/vue-friction-m8/run.ps1 -FullValidation
```

This compiles through the real project compiler, checks the physical Vue/TS
support, builds all three emission profiles, runs mounted browser checks, and
builds/tests the full .NET solution. Browser qualification starts an ephemeral
local service and closes it when finished. It never installs a background
service. Other installed Playwright browser channels can be selected with
`-BrowserChannel`.

For interactive inspection, run a foreground server after building:

```powershell
npm run serve --prefix samples/copeland-ts/vue-friction-m8
```

- Vue SFC baseline: <http://127.0.0.1:4188/baseline/>
- Copeland page: <http://127.0.0.1:4188/diagnostic/application/>
- Effects and owned children: <http://127.0.0.1:4188/diagnostic/lifecycle/>

Replace `diagnostic` with `symbolic` or `production` for the other profiles.
Stop the server with Ctrl+C.

Start reading at `src/Application.ts`, `src/IssuesPage.ts`, and `src/State.ts`.
Their application logic contains no XML elements or Vue directives. `src/UI.tsx`
is the optional document/control projection. `controls/*.vue` are genuine Vue
SFCs, and the arrangement button uses Reka UI's real `Toggle`.
`src/Lifecycle.tsx` demonstrates declared effects and keyed child ownership
through an imported Vue component's default slot.

This is a bounded compiler/renderer experiment, not a general Vue package
toolchain or simultaneous React/Vue root compositor. Shared control/theme CSS
remains. The sample HTTP service provides reproducible search/save responses;
it is not durable storage. Scope and checked evidence are documented in
`docs/Copeland/reviews/cts-vue-friction-m8.md`.
