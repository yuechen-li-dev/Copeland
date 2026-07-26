# Copeland Browser M0 trial

This is a deliberately small static-browser discovery harness, not the final Copeland browser build workflow.

Build the generated ESM graph:

```powershell
dotnet run --project .\Copeland.Browser.M0.csproj
```

Serve this directory through HTTP, then open `index.html`:

```powershell
python -m http.server 4173 --directory .
```

The host page explicitly imports `generated/Main.js` and invokes `Main()`. The import map resolves the one browser host seam, `@copeland/browser-m0`, to `host/browser-m0.js`.

`Copeland/Main.ts` and `Copeland/Counter.ts` are ordinary Copeland modules. The host seam exposes only `setText(id, text)` and `onClick(id, transition)`, where `transition` has the exact type `(int) => int`. It intentionally exposes neither raw DOM values nor untyped property access.
