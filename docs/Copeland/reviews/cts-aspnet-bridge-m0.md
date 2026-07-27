# CTS-ASP.NET-BRIDGE-M0 review

Status: honestly complete for the bounded M0 proof.

This milestone proves one generated browser-to-CLR operation. It is not a
general RPC platform or a deployment-ready application framework.

## Authored operation

The fixture declares:

    using System.Text.Json;

    export record SerializeRequest {
        message: string;
        count: int;
    }

    export record BridgeError {
        kind: string;
        message: string;
    }

    export remote function SerializeState(
        request: SerializeRequest
    ): string ! BridgeError {
        return JsonSerializer.Serialize(request);
    }

The remote marker is parsed as function realization metadata and travels
through export discovery, binding, MIR, and both backend emitters. Remote calls
are typed as asynchronous in Copeland; the CLR realization remains synchronous.

## Generated contract

The generator emits one deterministic versioned contract at
Host/wwwroot/bridge-contract.json:

    schemaVersion: 1
    id: Bridge.ts/SerializeState
    method: POST
    route: /__copeland/m0/bridge/serialize-state
    request: nominal SerializeRequest { message: string; count: int; }
    response: string
    error: nominal BridgeError { kind: string; message: string; }
    fallible: true

The nominal record identities, source field names, response type, error shape,
route, and schema version are retained. Unsupported boundary fields are
rejected by the generator. Compiler and generator diagnostics are bounded to
the M0 shape; there is no runtime type-name or method-name dispatch.

## Generated browser client

Host/wwwroot/Bridge.js emits an ordinary production-browser ESM function named
SerializeState. It validates the nominal request, maps generated production
record slots to the contract field names, performs browser-native POST fetch,
checks the JSON envelope and schema version, and resolves a generated Copeland
Result inside the existing asynchronous computation representation.

The client projects host-unavailable, HTTP failure, malformed request,
malformed response, bridge-version mismatch, and declared remote failure into
the bounded BridgeError record. It does not expose a dynamic response value.
The base URL is supplied by generated bridge-config.js as the current
same-origin, rather than being compiled from a temporary test port.

## Generated ASP.NET Core endpoint

Host/Generated/BridgeEndpoints.g.cs registers the fixed route through idiomatic
MapPost. ASP.NET Core owns HTTP transport, request cancellation, status codes,
routing, and lifecycle. The endpoint:

* accepts JSON only;
* rejects bodies above 64 KiB when content length is available;
* deserializes a generated request DTO with System.Text.Json;
* rejects malformed JSON, missing fields, and unknown routes;
* directly invokes Copeland.Generated.CopelandModule.SerializeState;
* returns stable success, declared-failure, or server-failure envelopes; and
* logs server exceptions without exposing stack traces to browser code.

No reflection, operation-name parameter, arbitrary CLR type name, custom web
server, OpenAPI layer, or general RPC framework is involved.

The generated CLR source is Host/Generated/Copeland.g.cs. The operation calls
the real System.Text.Json.JsonSerializer.Serialize<T>(request) API. The
generated record properties carry JsonPropertyName attributes, so the visible
result is produced by CLR serialization rather than duplicated in browser
code.

## Hosting and lifecycle

The fixture has a small generation project and a normal ASP.NET Core host:

    samples/copeland-ts/aspnet-bridge-m0/Generate
    samples/copeland-ts/aspnet-bridge-m0/Host

The generation project builds the Copeland graph, CLR source, browser ESM,
contract, endpoint source, and runtime configuration. The Host project serves
the generated static output and bridge endpoints from one same-origin
ASP.NET Core process.

It binds to 127.0.0.1 on a dynamically assigned port. Serving the browser and
bridge from the same origin removes the need for CORS and avoids a second
static server for this local proof.

The process is started with ordinary dotnet hosting and stops through normal
ASP.NET Core shutdown. This same-origin seam is the smallest correct fixture
for M0; TSPack supervision and a separate deployment asset server remain
future application-host work, not a new transport implementation.

## Reducer/effect boundary

The proof does not put transport in a pure reducer. The direct HTML fixture is
a renderer-neutral event adapter: it changes local count, invokes the
generated typed callable, and incorporates the typed completion into visible
state. React can consume the same client through its existing
event -> reducer -> effect -> completion-event -> reducer path without any
bridge-specific DOM or React dependency.

## Chromium evidence

Using a fresh in-app Chromium tab against the generated host:

* the page loaded over HTTP;
* initial visible state was Count: 0;
* initial visible CLR JSON was {"message":"Hello from CLR","count":0};
* a real click changed visible state to Count: 1;
* the second visible CLR JSON was {"message":"Hello from CLR","count":1};
* browser console diagnostics contained no warnings or errors;
* the ASP.NET request proof returned HTTP 200 for both counts;
* malformed JSON returned HTTP 400 with malformed-request;
* an unknown bridge route was not handled by the generated operation; and
* the browser tab and ASP.NET process were closed with no orphan bridge process.

The server-side response values were captured from the live endpoint, and the
generated CLR source contains the direct JsonSerializer.Serialize call. No
browser-side serialization algorithm substitutes for that operation.

## Supported boundary and deferred work

M0 supports int, bool/boolean, string, one nominal record with primitive
fields, one string success response, and one nominal { kind: string; message:
string; } error record.

Deferred: React integration and CTS-REACT-M0 closure, arbitrary object graphs,
cyclic values, streams, uploads, callbacks, server push, WebSockets, CLR
handles, generics, inheritance, polymorphism, binary protocols,
authentication, authorization, cookies, sessions, databases, EF Core,
service discovery, load balancing, SSR, Blazor, deployment automation,
OpenAPI productization, and reflection dispatch.

## Additional work performed

* Added explicit remote realization metadata through syntax, binding, MIR,
  and async invocation typing; required because one declaration must produce
  both realizations.
* Added deterministic bridge identity/route helpers and versioned contract
  serialization; required to prevent endpoint/client drift.
* Added generated ASP.NET Core endpoint source and request DTO validation;
  required to keep HTTP ownership in ASP.NET Core while keeping operation
  ownership in Copeland.
* Added production browser client emission, generated base URL configuration,
  and production record-field mapping; required because browser records use
  compiler-owned storage slots while the wire contract uses authored names.
* Added a same-origin fixture host and generated artifacts; required to prove
  startup, static serving, bridge invocation, and cleanup without introducing
  a custom server.
* Added focused contract, client, CLR direct-call, route, and unsupported-shape
  tests.

These changes preserve the existing TSON transport and React path. They do not
change package graphs, assembly resolution, or deployment configuration.

## Validation

Focused bridge tests:

    dotnet test tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj --no-restore --filter FullyQualifiedName~AspNetBridgeM0Tests

Result: 3 passed.

Fixture generation and host build passed:

    dotnet run --project samples/copeland-ts/aspnet-bridge-m0/Generate/Generate.csproj --no-restore
    dotnet build samples/copeland-ts/aspnet-bridge-m0/Host/Copeland.AspNetBridge.M0.csproj --no-restore

The focused live HTTP and Chromium proofs passed as described above. Full
solution build/test and final diff checks are run as the final repository
validation. Any inherited corpus/hash failures are reported there without
updating unrelated baselines.
