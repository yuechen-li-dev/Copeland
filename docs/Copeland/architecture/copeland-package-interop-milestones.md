# Automatic package interop

An application declares a package dependency and the typed functions it uses. When a call crosses runtimes, Copeland uses the same TSON plan to generate both ends. Node imports and direct CLR calls remain the execution paths when the package runs in the target runtime.

## M0 · Generate a Node guest

The C# backend can emit a Node guest alongside the CLR client through the opt-in `EmitForRootManifest` overload. Both endpoints take their operation identities and DTO schemas from the same MIR. The guest imports the selected npm export, decodes the request, awaits the call, and validates and encodes the reply. A thrown value that fits the declared error DTO is a typed error. A value that violates the contract is a transport failure.

The process test runs the generated guest through the existing CLR host. It exercises zero and multiple arguments, arrays, flat records, escaped Unicode strings, a thrown package error, Promise results completing out of order, and an invalid package result. The explicit Node run target and host attachment are the entry point for this stage.

A package smoke test also ran `change-case@5.4.4` through the generated guest and passed the result to `Newtonsoft.Json@13.0.4`. The CLR app printed `"shipTheDocs"`.

## M1 · Build and publish own the artifacts

SDK projects opt into generated guests with `CopelandNpmLockFile` and an existing `CopelandNpmContract` item. Run `npm ci` first. The SDK checks installed package versions against the lock and copies the runtime packages, including nested dependencies, to the build and publish outputs. Publish with `--no-build` uses the assets from the previous build. Linked npm workspaces are outside this SDK path.

For a TSPack CLR target, `tspack sync` supplies the installed graph and `tspack build` publishes the guest with the .NET app. The CLI follows the materialized dependency layout and lists the copied files in the build result. Both paths use a lazy, statically bound host in the generated assembly. One guest is started per active generated assembly in the application, and Node must be available on PATH.

CopelandHello exercises this path with lodash-es and Newtonsoft.Json. The published app runs from outside the source tree and reports `"helloFromNpm"`. A separate TSPack smoke test published pascal-case with its transitive dependencies and printed `ShipTheDocs`. An SDK app using a `ProjectReference` also received its library's guest on build and on publish with `--no-build`.

## M2 · One npm contract across targets

The same fallible async import runs through the native Node module loader or the generated CLR sidecar. Both project argument records into package-facing objects and project valid values back into Copeland records. Synchronous throws and rejected promises that match the declared error DTO become typed errors. Missing exports and invalid return values fail the boundary. An entry point can `await` the call and match its `Result` on all three tested targets.

The BCL-only `Copeland.TS.Sidecar` assembly owns the child process for generated and custom guests. Generated apps bind it with typed delegates. Managed publish copies the runtime assembly and NativeAOT links the reachable runtime code. The generated host no longer brings Roslyn into either app output. The legacy host adapter retains its explicit reflection-based attachment API for existing manually wired callers.

The Windows x64 qualification built the same `pascal-case@3.1.2` source and contract through TSPack on Node, RyuJIT, and NativeAOT. All printed `ShipTheDocs`. The managed publish directory went from 22.8 MB in M1 to 0.39 MB with the runtime-only host, including the npm packages in both measurements.

The packaged SDK published CopelandHello for Windows x64 and Linux x64 under WSL with `PublishAot` and `TreatWarningsAsErrors`. Both binaries ran the lodash-es result through Newtonsoft.Json and printed `"helloFromNpm"`. The strict builds completed without trim or AOT warnings. The portable managed SDK output and the Node and managed TSPack outputs also ran on Linux. The sidecar runner still requires Node on PATH.

The controlled target tests cover record projection, typed throws and rejections, malformed results, and missing exports. Process tests cover out-of-order replies, cancellation, malformed frames, handshake mismatch, missing guest startup, process death, and calls made after the guest exits. Rebuilding the TSPack app after changing its schema produced a new guest and rejected the previous guest's handshake. The sidecar has an independent byte limit for framed TSON. Distribution through a released compiler pair remains separate.

## M3 · Call NuGet packages from emitted Node JavaScript

A Node target can include `nuget-worker.json` alongside its workspace manifest. The config pins NuGet package references and maps literal `tsonCall` operations to public static CLR methods. The compiler uses the same request, reply, and error plans to generate the Node call and the CLR dispatcher. MSBuild compiles the direct CLR call through ordinary overload resolution and publishes the worker with its NuGet dependencies.

The generated Node host starts the framework-dependent .NET 10 worker on demand and reuses it across calls. The compact TSON envelope carries the shared DTOs. The worker handshake checks the operation contract before any call is sent. CLR exceptions fill the declared error record. Launch, handshake, and malformed-frame failures stay transport errors.

The [Newtonsoft.Json sample](../../../samples/copeland-ts/nuget-worker-m3/README.md) printed `"Ship the docs"` from a relocated publish directory. Both the standalone and TSPack build paths ran on Windows, and the relocated TSPack output also ran on Linux under WSL. The CLI regression tests cover overlapping calls, one-dimensional array requests and replies, CLR exceptions, and a missing worker. M3 covers synchronous static methods with bounded string, boolean, number, and scalar-array DTO fields. Deployment needs Node and .NET 10.

## M4 · Qualify browser deployment

Browser JS targets select `wasm` or `service` explicitly in `nuget-worker.json`. Both publish the same JS-facing bindings and use the same TSON plans and operation dispatcher as Node. Package exceptions fill the declared error record. Loading, dispatch, and bad wire data fail the computation through the transport channel.

- **WebAssembly.** The JS host loads the published .NET 10 runtime in a dedicated browser worker. A generated `JSExport` bridge calls the dispatcher. The qualifying page loaded the package assemblies and made zero package API requests. The publish treated warnings as errors and used the SDK's browser trimming. This lane qualifies the selected API surface and still requires every selected dependency to work in browser .NET. [Microsoft's .NET WebAssembly interop documentation](https://learn.microsoft.com/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0)
- **Service.** The build publishes a separate ASP.NET Core app with one same-origin TSON POST route. The qualifying page used the POST route and loaded no .NET runtime assets. It verifies the contract digest on every request, applies a 1 MiB frame limit, and dispatches only the declared operations. Deploy server binaries separately from the public static assets and route the browser's endpoint to the service.

The [published sample](../../../samples/copeland-ts/nuget-browser-m4/README.md) qualifies the `string` and `boolean` overloads of `Newtonsoft.Json@13.0.4`'s `JsonConvert.ToString` and the typed failure from `Humanizer.Core@2.14.1`'s `Ordinalize(string)`. Node, direct managed CLR, and both Chromium lanes produced the same result. Browser verification uses the compiled entry, copies the published assets to a fresh location, and exercises transport failure independently for both modes.

### Generate bindings from a package signature

Schema version 2 selects a pinned package, public static CLR member, exact parameter types, and Copeland export name. The compiler restores the .NET 10 compile assets, reads PE signatures without loading the built package, and supplies generated Copeland source before binding. It derives the request and reply DTOs and constructs the `Result` wrapper. The same source calls a generated local .NET worker on Node and the static CLR methods in-process on .NET. `tscl nuget generate` also materializes the source for project tools that scan files.

This is the production entry point for the bounded signature experiment. Keep package and overload selection explicit. Reading metadata before the Copeland binder lets all targets see the same types. A Roslyn source generator could generate CLR wrappers during MSBuild, but that would come too late to supply the Node and browser binder with their types. Those wrappers already come from the shared MIR.

The generator accepts synchronous, non-generic, public static methods on public top-level types. The wire supports string, boolean, binary64 (`System.Double`), and one-dimensional arrays of those types. CLR object graphs, async CLR APIs, instance lifetimes, and overloads outside that carrier set require a wider contract. In particular, the probe rejects `JToken.Parse(string)` before emission because its return value is a CLR object.

## M5 · Generate nested and nullable DTOs

Schema version 3 accepts named data records. Their fields can contain scalars, other declared records, arrays, and `Option<T>`. Copeland emits the record declarations and typed functions before binding. The TSON plans drive both transport codecs, and the C# backend generates CLR data contracts and conversion code for the same records. Managed .NET uses the conversions around a direct package call. Node uses them in the generated worker.

The generator reads restored compile assets through Roslyn's metadata symbols during the `tscl` build. It uses nullable annotations and `Nullable<T>` to infer `Option<T>`. Operations still select an exact overload. `wireParameters` gives a concrete shape to an `object` parameter, and `typeArguments` closes a supported generic overload over declared records. Generated app and worker code call the selected method statically. The signature reader stays in the compiler.

The [M5 sample](../../../samples/copeland-ts/nuget-roundtrip-m5/README.md) calls Newtonsoft.Json's `SerializeObject(object)` and `DeserializeObject<T>(string)` on Node and managed .NET. It verifies nested records, arrays of records and optional items, absent optional fields, a null root, present zero and false, empty strings, and typed errors for malformed JSON and missing required properties. Both published entries print the same serialized shipment. This milestone qualifies these overloads on Node and managed .NET.

Projection covers declared, acyclic data shapes. Packages still need a supported signature, such as these scalar, nullable, or explicitly closed generic operations. Package-specific runtime types such as `JToken` still need a supported projection.

## M6 · Qualify the installed toolchain

The preview release workflow runs the [package interop test](../../../tools/Test-CopelandPackageInterop.mjs) on Windows and Linux before publication. It installs the candidate Copeland tool from the release feed with fresh tool and package caches and uses the checksum-verified TSPack v0.1.14-m80i.2 release. It runs the SDK's npm guest and both TSPack Newtonsoft.Json targets from relocated publish directories.

The installed VSIX test opens a mixed TSPack project with ESLint enabled. It checks that TypeScript and ESLint still report their own diagnostics on traditional TypeScript. It also checks the Copeland mode for the root manifest, missing NuGet bindings and their quick fix, and diagnostics for newly created Copeland files. See the [VS Code guide](../vscode.md).

| Command or deployment | Requirements |
| --- | --- |
| Build and binding generation | .NET 10 SDK and the Copeland tool. With TSPack v0.1.14-m80i.2, set the package's `compilerPath` to the installed `tscl` executable and run `tscl nuget generate .` before TSPack scans the project. |
| .NET calling npm | Node on PATH and the published npm guest. |
| Node calling NuGet | Node, the .NET 10 runtime on PATH, and the whole published worker directory. |
| Managed .NET calling NuGet | .NET 10 runtime and the published managed app. |
| Browser WebAssembly and service | See the [M4 deployment sample](../../../samples/copeland-ts/nuget-browser-m4/README.md) for prerequisites and the API surface qualified separately on each browser route. |
