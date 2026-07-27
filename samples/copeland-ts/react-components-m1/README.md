# CTS-REACT-COMPONENTS-M1

This is the canonical bounded third-party React component consumption proof.

`@base-ui-components/react` is an unchanged third-party npm React library,
resolved and materialized by TSPack at the locked version
`1.0.0-rc.0`. Copeland owns `AppState`, `AppEvent`, the pure `Reduce` function,
the typed sender, and the controlled `open` state. React and Base UI retain
element rendering, dialog behavior, focus management, keyboard handling,
portal behavior, and accessibility behavior.

The authored application contains no React hooks, context store, direct DOM
mutation, or replacement dialog implementation. The selected contract is
intentionally bounded to the actual `@base-ui-components/react/dialog` export:
the named `Dialog` namespace and its `Root`, `Portal`, `Backdrop`, `Popup`,
`Title`, `Description`, and `Close` members.

Build the real browser artifact from the TSPack manifest:

```text
go build -o tspack.exe ./cmd/tspack
tspack.exe update --root .
tspack.exe sync --root .
tspack.exe build --root . browser
```

The build output is `dist/browser/`. TSPack owns the lock graph, package
materialization, transformed browser graph, import map, and React singleton.
