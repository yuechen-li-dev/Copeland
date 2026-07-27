# CTS-REACT-M0 unified React + CLR proof

This bounded application combines the accepted Copeland browser, TS-XML React,
and ASP.NET Core bridge seams in one real page.

* React and ReactDOM come from TSPack-materialized npm packages at the locked
  version `19.2.7`.
* `System.Text.Json` comes from the CLR through the authored remote operation.
* Copeland owns `AppState`, `AppEvent`, the pure `Reduce` function, dispatch,
  the async effect, and the generated typed bridge client.
* React owns only element representation, mounting, reconciliation, and DOM
  updates.

Build the browser dependencies from the `host` manifest with TSPack, then run
the Copeland generator and same-origin host:

```text
tspack update --root host
tspack sync --root host
tspack build --root host browser-materialization
dotnet run --project Copeland.TsXml.React.M0.csproj
dotnet run --project host/Copeland.ReactClr.M0.Host.csproj
```

The host prints a loopback URL and serves the generated browser graph.

The expected visible sequence is:

```text
Count: 0
{"message":"Hello from CLR","count":0}

Count: 1
{"message":"Hello from CLR","count":1}
```

The JSON is returned by the generated ASP.NET endpoint after direct invocation
of generated CLR code. The browser does not compute it, use a handwritten
fetch, mutate the DOM directly, or use React hooks/state/context.
