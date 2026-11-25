# Round trip a nested NuGet DTO

The same Copeland entry runs on Node through a generated worker and on .NET
through direct CLR calls. It serializes a shipment with
`Newtonsoft.Json.JsonConvert.SerializeObject(object)` and reads it back with
`DeserializeObject<Shipment>(string)`.

[nuget-worker.json](nuget-worker.json) declares the shipment and address shapes
once. For the `object` overload, `wireParameters` supplies the shape that CLR
metadata leaves open. For deserialization, `typeArguments` closes the generic
over that shape. Signature inspection infers the `Option<Shipment>` result
from the package's nullable return annotation.

The compiler generates the immutable Copeland records, TSON plans, CLR DTOs,
and conversions. A `None` field writes as JSON `null`. JSON null and absent
optional fields come back as `None`. The sample checks nulls at the root and
inside records and arrays, along with present `0`, `false`, and empty strings.
Malformed JSON and missing required properties return a typed `NugetError`.

Use a `tscl` with M5 support and the .NET 10 SDK to build. The Node deployment
needs Node and the .NET 10 runtime. Copy each complete output directory to run
from a different working directory.

```text
tscl nuget generate .
tscl check --standalone .
tscl build --standalone . --target node --result obj/node.json
tscl build --standalone . --target clr --result obj/clr.json
node dist/node/main.js
dotnet dist/clr/main.dll
```

Both entries print the serialized shipment after checking the decoded record
and error cases. For a TSPack build, set `compilerPath` on the manifest's
`Package` to the installed `tscl` executable. Materialize the bindings with
`tscl nuget generate .`, then run `tspack update` and `tspack build`. The
VS Code extension offers **Generate NuGet Bindings** when the generated module
is missing.
