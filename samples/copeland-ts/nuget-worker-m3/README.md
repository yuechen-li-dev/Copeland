# Call a NuGet package from Node

This sample calls Newtonsoft.Json from emitted Node JavaScript. The Copeland
source declares the request, reply, and error records and calls the named
operation with `tsonCall`. The worker config selects the NuGet package version
and maps the operation to `Newtonsoft.Json.JsonConvert.ToString`.

From this directory, use a `tscl` build with NuGet worker support, Node, and
the .NET 10 SDK.

```text
tscl build --standalone . --target node --result obj/build.json
node dist/main.js
```

The output is `"Ship the docs"`, including the quotes added by Newtonsoft.Json.
The build generates a .NET project with normal NuGet package references and a
direct C# call. Copeland generates the worker codecs from the same TSON plans
as the Node code. The active NuGet configuration applies during restore.

Copy the whole `dist` directory to deploy. It includes the Node entry, the
generated host, the worker DLL, and its runtime dependencies. Run
`node <deployment-directory>/main.js` from any working directory. The target
machine needs Node and the .NET 10 runtime on PATH. The build receipt lists
each deployed file.

M3 supports synchronous public static methods. The request record supplies
positional arguments in declaration order. DTO fields use `string`,
`boolean`, `number`, or one-dimensional arrays of those types. The reply has
one field, and the error record has one string
field for a CLR exception message. A worker launch or transport failure
remains a separate failed computation. Browser builds use a separate
deployment path.
