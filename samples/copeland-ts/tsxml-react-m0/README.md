# Copeland TS-XML React M0

This bounded browser sample owns state, `AppEvent`, `Reduce`, dispatch, and the TS-XML view. React only owns element representation and DOM reconciliation.

Run `dotnet run --project Copeland.TsXml.React.M0.csproj` to emit production browser ESM. The page expects TSPack's transformed React 19.2.7 browser packages under `packages/`; its import map preserves one shared `react` realization for both `react` and `react-dom/client`.

The expected browser result is `Count: 0`, then `Count: 1` after clicking **Increment**. No hooks, React state, Context, direct DOM mutation, or raw event objects are used.
