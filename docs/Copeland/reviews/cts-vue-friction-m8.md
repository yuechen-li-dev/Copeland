# CTS-VUE-FRICTION-M8: Vue component interop

Status: **Success for the bounded Vue experiment.** The existing issue-triage
page now runs both as an idiomatic Vue 3 SFC and as Copeland TS projected into
Vue. Mounted checks establish domain parity and exercise Copeland effects and
owned child state through genuine Vue components.

## Authoring boundary

The intended model is language-level component composition: Copeland owns
application state, effects, layout, and its supported style declarations;
framework components supply reusable presentation and their internal interaction.
The application does not need Vue's SFC template/directive language.

The page's entry point is ordinary Copeland source:

```typescript
export function Application(width: int): VueNode {
    state: AppState = InitialState(width);
    on Apply(event: AppEvent) => Reduce(state, event);
    return presentViewport(IssuesPage(state, on.Apply),
        capture { send: on.Apply } (width: int) => send(AppEvent.Resize(width)));
}
```

`IssuesPage.ts` uses semantic presentation records, `Option`/enum matches, a
contribution stream, and reusable layouts. Layout overrides are source data:

```typescript
layout IssueWideBase = instantiate TwoPane<sidebarWidth: 300px>;
layout IssueWideLayout = IssueWideBase with {
    root.style: { fill: "#e8eef0" };
};
```

`UI.tsx` contains the optional XML projection and framework prop names. The page
calls `PresentArrangement`, whose projection hosts Reka UI's actual `Toggle`
using `modelValue` and `onUpdate:modelValue`. Search uses a real typed Vue SFC;
DOM event extraction stays inside that control. `Panel.vue` supplies a real
default slot containing Copeland-owned editors.

Nonblank line counts are recorded separately: baseline SFC 97; Copeland
application 10, page 94, state 57; supporting UI projection 136 and layouts 35.
This demonstrates a different authoring boundary, not a reduction in total
source or a universal claim that Copeland programs are shorter. Shared domain
helpers/content and physical build support also remain source dependencies.

## Compiler and runtime integration

- `VueComponents` selects the renderer and exposes the opaque `VueNode` type.
  Layout, stream, capsule, endpoint, MIR, and ESM paths preserve that type.
  React and Vue node values are distinct.
- Npm component contracts declare `renderer: "vue"` and, where supported,
  `defaultSlot: true`. Scalar props and callback signatures remain typed.
  JSON/context contract paths retain those fields. Existing contracts default
  to React for compatibility.
- Imported component children become a function-valued default slot; absent
  children become `null`. Compiler-created slot metadata allows the existing
  ownership visitor to inspect children without executing arbitrary user slots.
- The Vue adapter wraps generated component hosts, subscribes through
  `shallowRef`, and acknowledges the exact rendered revision using `onMounted`
  and `onUpdated`. `onBeforeUnmount` releases the owner. The existing canonical
  frame registry still owns transitions, effect phases, cancellation, and
  keyed child retirement. There is no Vue-specific state/effect engine.
- Private layout capsules carry Vue adapter identity and presentation payload.
  Legacy internal `BoundReactElement`/MIR names and shared `COPE-REACT-*`
  diagnostics remain for compatibility; their nodes carry the selected type.

The fixture's small C# driver calls `CopelandProjectCompiler`,
`JavaScriptProjectEmitter`, and `LayoutJavaScriptProjectEmitter` directly.
Physical TypeScript build support uses Vue's official SFC compiler for the
baseline and imported controls. TSPack materializes the existing canonical
browser host package through its locked fixture. This does not qualify a
general Vue SFC compiler inside Copeland or a production Vue TSPack target.

## Verification

Versions: .NET SDK 10.0.401, Node 26.2.0, Vue 3.5.43, Reka UI 2.11.0,
Playwright 1.56.1, and Microsoft Edge 153.0.4234.48.

- Full solution build: zero warnings and errors. Full .NET tests: **2,225
  passed, zero failed/skipped**. Thirteen new Vue backend tests cover selected
  types, layout/template overrides, slot emission, natural `class`/`for`
  attributes, colon-bearing event props, and exact contract/endpoint rejection.
- Vue mounted qualification: **96 observations**, no browser diagnostics.
  The baseline and all three Copeland profiles run at 1280, 768, and 390 px.
  Checks cover search, selection, insertion/removal, reversal, loading/error
  matches, controlled Reka interaction, viewport changes, long text, 200% text,
  absence of overflow, compiled layout width/fill, and matching domain results.
- Eight lifecycle observations per profile establish keyed reorder preservation,
  focus after presentation commit, actual HTTP typed completions, stale response
  rejection, fresh state after key reinsertion, typed failure handling, rejection
  of work from a retired owner, and subtree retirement on root unmount.
- React regression: **55 compiler expectations**, seven mounted state/on cases
  and twelve effects/children cases in each profile, 74 paired page observations,
  and four semantic library assertions passed. Expected retired-endpoint
  diagnostics are asserted; browser console/page errors are absent.
- Frozen Zig suites are outside this change and were not run.

Evidence lives in `evidence/vue-friction-m8/`: compiler source/emission hashes,
browser report, viewport screenshots, authoring counts, validation hashes, and
the separate `react-regression/` reports. Full solution logs are retained under
`artifacts/validation/vue-friction-m8-{build,tests}.log`.

Reproduction and foreground demo URLs are in
`samples/copeland-ts/vue-friction-m8/README.md`.

## Deliberate limits

One renderer is selected per project target. Selecting React and Vue together
is rejected with `COPE-COMPONENT-RENDERER-0002`; importing a component declared
for the other renderer is rejected with `COPE-COMPONENT-RENDERER-0001`.
Simultaneous framework islands require a qualified host boundary and are not
implemented by this milestone.

Default slots are qualified. Named/scoped slots, custom directives, Teleport,
KeepAlive, Suspense, SSR/hydration, HMR, and general component-library coverage
are not. `AttachmentsSettled` remains unsupported without declared attachment
acknowledgements. Vue components may own internal interaction, but application
transitions/effects use the Copeland registry.

Layout templates, configuration through `with`, and generated fill/size rules
work in the motivating page. The existing shared CSS still supplies typography
and control/theme styling; this pass does not eliminate all CSS. The HTTP
fixture provides bounded search/save responses rather than durable persistence
or arbitrary backend querying.
