# CTS-ASP.NET-BRIDGE-M0 fixture

This is a bounded generated browser-to-CLR bridge proof. The Generate project
compiles the Copeland sources once, emits the CLR realization, the fixed bridge
contract, the typed browser ESM modules, and the generated ASP.NET Core
endpoint source. The Host project then serves the generated browser output and
registers the generated endpoints in one same-origin ASP.NET Core process.

    browser Copeland client
      -> POST /__copeland/m0/copeland/bridge/serialize-state
      -> generated ASP.NET Core endpoint
      -> Copeland.Generated.CopelandModule.SerializeState
      -> System.Text.Json.JsonSerializer.Serialize
      -> typed result envelope

The generated client uses browser-native fetch and a bounded BridgeError record
for malformed request, malformed response, HTTP failure, host unavailability,
and declared remote failure. It never receives a dynamic result. This direct
fixture uses a small HTML event adapter; the Copeland remote function remains
renderer-neutral.

The host binds to loopback and serves both assets and bridge routes, so no CORS
is enabled. This is appropriate only for the local proof. Authentication,
authorization, deployment hosting, streams, callbacks, arbitrary CLR types,
polymorphism, and OpenAPI are intentionally deferred.
