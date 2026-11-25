# Call NuGet packages on Node, .NET, and in the browser

All four targets build [the same Copeland source](src/Main.ts). It imports
`Quote` and `FormatBoolean`, which select two overloads of
`Newtonsoft.Json.JsonConvert.ToString`, and `Ordinalize` from Humanizer.
The invalid ordinal string exercises the typed package error.

[nuget-worker.json](nuget-worker.json) pins the packages and lists the selected
overloads. The compiler reads their restored CLR metadata and generates
`src/generated/Nuget.ts`, including the typed functions and TSON records.
Standalone builds can generate the module in memory. Run `tscl nuget generate .`
to write it to disk for tools that read project sources. Rerun generation after
changing the declared APIs.

## Build and run

Use a `tscl` with M4 support and the .NET 10 SDK. Install the matching
`wasm-tools` workload for the WebAssembly publish.

```text
tscl nuget generate .
tscl build --standalone . --target node --result obj/node.json
tscl build --standalone . --target clr --result obj/clr.json
tscl build --standalone . --target browser-wasm --result obj/wasm.json
tscl build --standalone . --target browser-service --result obj/service.json
node dist/node/main.js
dotnet dist/clr/main.dll
```

Both commands print `"Ship the docs" | true | typed error`. The Node app
starts a local worker. The CLR app calls the selected package methods directly.

With TSPack, run `tscl nuget generate .` before `tspack update` and
`tspack build` so its source scanner sees the generated imports. Use the
same build of `tscl` for generation and compilation.

## Deploy each browser target

The **WebAssembly** target is a static deployment. Serve the contents of
`dist/wasm` over HTTP with `.wasm` files served as `application/wasm`.
Import `./main.js` from a page in that directory. Its `result` export contains
the completed entry result. A dedicated browser worker loads .NET and the
package assemblies on demand.

The **service** target serves the JS at the same origin as its HTTP endpoint.
Run `dotnet dist/service/copeland-nuget/service/NugetWorker.dll` with the
ASP.NET Core 10 runtime. Keep the `copeland-nuget/service` directory on the
server and serve the rest of the output as static files. Proxy POST requests
for `copeland-nuget/invoke` relative to the app's base path to
`/copeland-nuget/invoke` on the service. The worker dispatches only operations
declared in the package config. The contract digest detects client and service
version mismatches.

Use the browser proof with Node and Playwright Chromium installed. Set
`TSPACK_PLAYWRIGHT_MODULE` to the local Playwright module path if it is
installed elsewhere.

```text
node browser-proof.mjs dist
```

The proof copies the published files to a fresh directory, serves them under a
subpath, and checks both browser modes independently. It also interrupts
delivery to verify that the browser entry reports a transport failure.
