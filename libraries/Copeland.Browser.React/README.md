# Copeland React controls

`@copeland/react-controls` is the shared React adapter for controlled text fields
and explicit form commits. The editing and variable-row café examples consume
this package, rather than owning copies of its implementation or compiler contract.

| Export | Responsibility |
| --- | --- |
| `TextField` | Label, controlled raw text, text/numeric/decimal input hint, invalid state and feedback reference; forward the exact string |
| `PreviewForm` | Prevent native navigation and call `onCommit`; domain validation remains explicit |
| `copeland.contract.json` | Versioned component input for the existing `CopelandNpmContractReader` |

Drafts, parsing, effects, accepted snapshots and purchasing policy stay in
Copeland application source and `Copeland.Editing`. This adapter has no private
input state. It does not convert incomplete strings into a previous valid number.
The `field` CSS class is the current wrapper styling hook. Callers supply a unique
input ID and the IDs of feedback elements in their own document.

This is a private, source TypeScript package for bundlers, not a published npm
release or a Node execution package. Samples lock a local `file:` dependency and
use `npm ci --install-links` to install a fresh copy. React is a peer dependency;
there is one consumer-owned React instance. Esbuild compiles the TypeScript entry.
The package is qualified with React 19.2.7 through both consuming samples:

```powershell
./samples/copeland-ts/tablescript-editing-m14/run.ps1
./samples/copeland-ts/tablescript-rows-m15/run.ps1
```

Those real browser workflows cover incomplete text, submit/Enter, accessibility
feedback, normalization by the Copeland reducer, cancellation/reset, and all three
input hints. The older application sample intentionally retains its different
numeric/native-validity bridge as regression coverage.
