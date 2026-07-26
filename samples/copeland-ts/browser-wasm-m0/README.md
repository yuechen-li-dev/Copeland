# Copeland C# WebAssembly M0

This discovery sample compiles the `Copeland/` sources through the ordinary C# backend, publishes them into the .NET browser runtime, and uses the small `wwwroot/Host/browser-host.js` host for runtime bootstrap and DOM events.

```powershell
dotnet build .\Copeland.Browser.Wasm.M0.csproj
dotnet publish .\Copeland.Browser.Wasm.M0.csproj -c Release -o .\publish
python -m http.server 4174 --directory .\publish\wwwroot
```

Open `http://127.0.0.1:4174/`. The bridge owns immutable reducer state in the WASM runtime. The host sends integer event discriminants and applies the returned display string to the DOM. It contains no application reducer, record, or workload logic.
