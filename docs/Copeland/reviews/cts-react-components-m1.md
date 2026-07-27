# CTS-REACT-COMPONENTS-M1

## Purpose

This milestone proves bounded third-party React component consumption from Copeland TS. It uses the real `@base-ui-components/react` npm package and keeps the application state, event, reducer, callback adaptation, and TS-XML binding in Copeland.

The result is not general React-library compatibility and is not a Copeland dialog implementation. Base UI retains dialog rendering and interaction behavior.

## Tested package and import form

The fixture locks `@base-ui-components/react` at `1.0.0-rc.0`. React and React DOM are both locked at `19.2.7`.

The actual authored import is:

```ts
import { Dialog } from "@base-ui-components/react/dialog";
```

The package subpath exports a named `Dialog` namespace-like object. Its declaration projection exposes the compound members used by the proof: `Root`, `Portal`, `Backdrop`, `Popup`, `Title`, `Description`, and `Close`. Copeland preserves the package specifier, named export, and qualified member identity through binding, MIR, and JavaScript emission. It does not synthesize namespace semantics for a different package shape.

## Component contract

The fixture declares a curated contract for the exact tested package version and subpath. It supports:

- `Dialog.Root`: `open: boolean`, `onOpenChange: (boolean) => void`, and optional React-node children;
- `Dialog.Portal`: optional React-node children;
- `Dialog.Backdrop`: bounded `className: string`;
- `Dialog.Popup`: bounded `className: string` and React-node children;
- `Dialog.Title`, `Dialog.Description`, and `Dialog.Close`: React-node children.

The contract is intentionally smaller than the package declarations. Base UI's actual `onOpenChange` callback also supplies change-detail metadata; the curated adapter explicitly consumes only the first boolean argument needed by the controlled-state proof. Arbitrary prop bags, refs, polymorphic element types, and the rest of Base UI's declaration machinery are not accepted.

Unknown components, qualified members, unsupported props, duplicate props, incompatible prop types, unsupported children, and incompatible callback signatures are diagnosed at bind time. Package materialization and JavaScript availability are checked separately.

## TS-XML and lowering

Intrinsic elements and imported components coexist under the explicit `react-m0` target profile. `.tsx` does not select React by itself, and importing a React package does not select the profile.

For example:

```tsx
<Dialog.Root open={state.dialogOpen}>
    <Dialog.Popup>
        <Dialog.Title>Third-party React works</Dialog.Title>
    </Dialog.Popup>
</Dialog.Root>
```

retains a component expression for the imported `Dialog` value and its qualified `Root`, `Popup`, and `Title` members. The JavaScript backend emits the equivalent `createElement(Dialog.Root, props, children)` shape. Intrinsic tags continue to emit string element names.

React-node child expressions are emitted as ordered React children. Boolean and string props are validated against the curated component contract. The emitter does not special-case Base UI component names.

## Controlled-state law and callback adaptation

The fixture owns:

```text
AppState.dialogOpen
AppEvent.OpenDialog / CloseDialog
Reduce(AppState, AppEvent)
```

Copeland passes `state.dialogOpen` to Base UI's `open` prop. Base UI invokes `onOpenChange(boolean)`. The retained callable bridge validates that primitive boolean argument, invokes the typed Copeland adapter, constructs a nominal `AppEvent`, runs the pure reducer, and renders the next state. The reducer never receives a React event object or Base UI detail object.

The application has no React hooks, context store, global event bus, mutable application closure state, direct DOM mutation, or replacement dialog behavior.

## TSPack browser realization

TSPack resolves and materializes the locked package graph. Base UI's ESM entry is transformed with the bundled browser transformer because its actual graph contains nested CommonJS and extensionless package dependencies. React and React DOM remain external singleton imports. The browser import map includes the exact Base UI dialog subpath plus the React runtime subpaths required by the package (`react/jsx-runtime`) and the React DOM compatibility entries.

The generated artifact records the package version, source entry, transformed ESM mode, transformer identity, output path, and hash in `browser-materialization.json`. No Base UI source is copied into the fixture or rewritten into a Copeland widget.

## Chromium proof

The canonical fixture is `samples/copeland-ts/react-components-m1/`.

Observed in a clean Chromium tab against the generated browser artifact:

1. Initial state showed `Dialog closed`; the Open dialog button was present.
2. Clicking Open dialog changed the state text to `Dialog open` and made the Base UI dialog visible.
3. The title and description were visible.
4. Base UI focus management put focus on the package-provided Close button.
5. Escape closed the dialog and returned the state text to `Dialog closed`.
6. Reopening worked.
7. Clicking the package-provided Close button closed the dialog and returned the state text to `Dialog closed`.
8. The clean rerun recorded no console warnings or errors. The observed browser asset inventory contained the generated application, host, React, React DOM, JSX runtime, and Base UI artifacts; the temporary server and browser tabs were cleaned up.

## Compatibility assessment

Base UI was made workable by a curated component contract plus a narrow browser realization extension. The package export shape is a named namespace-like `Dialog` export from a subpath, with compound members projected as explicit component members. The required prop kinds are boolean, string, React-node children, and a callback whose first parameter is boolean. The children model is ordinary nested React-node children. The runtime graph requires package transformation, React runtime subpath materialization, React DOM compatibility exports, and one React singleton.

The package declarations contain polymorphic props, forwarded refs, event-detail types, conditional types, and other advanced TypeScript machinery. Full declaration ingestion would be disproportionate for this proof. Curated contracts remain practical for a bounded, explicitly selected surface, but they must be tied to the exact tested package version and must reject unsupported behavior rather than silently widening to `any`.

The evidence therefore supports outcome C for this milestone: define explicit Copeland component-contract packages for bounded adoption, while keeping the contract representation reusable. A later bounded declaration projection can be evaluated against a second library; broad React-library support should not be inferred from Base UI alone.

## Deferred features

This milestone does not begin shadcn, Tailwind, styling records, full Base UI coverage, hooks in authored code, context, refs, `@types/react` ingestion, arbitrary `.d.ts` support, SSR, hydration, Next.js, generalized component registries, or design-system work.

## Additional work performed

- Added imported component and qualified member identity to binding, MIR, lowering, and JavaScript emission because TS-XML otherwise only admitted intrinsic string tags.
- Added curated npm component contracts and primitive callback/children validation because the existing npm boundary described callable exports only.
- Extended TSPack's TS-XML target contract and browser host/materialization path because the real Base UI graph requires an explicit React runtime subpath, transformed ESM, a shared React singleton, and source HTML/CSS materialization.
- Added compiler coverage for imported component identity, qualified lowering, children, callback adaptation, and unsupported component props.

These changes are bounded to third-party React component consumption. Existing React intrinsic and React + CLR paths remain regression targets.
