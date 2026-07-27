# CTS-STANDALONE-WEB-M0 review

## Product shape

`samples/copeland-ts/standalone-web-m0/StandaloneWebM0.csproj` is an ordinary
`Microsoft.NET.Sdk.Web` executable. Its MSBuild graph builds the Copeland generator,
materializes the locked React browser graph through TSPack, generates browser ESM and
the ASP.NET bridge, stages a validated asset set, compiles the executable, and copies
only the staged output to `bin/.../wwwroot` and publish output.

The build contract is:

```text
dotnet build -> locally runnable executable
dotnet publish --self-contained -> distributable application folder
```

TSPack is a build dependency only. The launched application starts a single ASP.NET
Core process; it never starts Node, npm, TSPack, esbuild, Vite, Electron, or an
embedded browser.

## Launch behavior

The default executable binds `http://127.0.0.1:0`, obtains the actual bound address
from ASP.NET Core after startup, prints it as `COPELAND_STANDALONE_READY`, then asks
the operating system to open that URL in the default browser. `--no-browser` keeps
the server headless. `--port`, `--host`, `--urls`, and `--open-browser` provide the
small command-line surface; `ASPNETCORE_URLS` remains supported. Non-loopback binding
requires explicit intent and prints a warning.

The browser launcher uses Windows shell URL opening, macOS `open`, and Linux
`xdg-open` without shell concatenation. Launch failure is diagnostic-only and never
stops the server. `COPELAND_BROWSER_LAUNCHER=record` is the bounded test seam: it
records the post-readiness URL without starting a personal browser.

## Same-origin application

ASP.NET Core serves generated `index.html`, Copeland modules, TSPack-transformed
packages, import map, bridge configuration and contract from the staged `wwwroot`.
The generated fixed `POST /__copeland/m0/bridge/serialize-state` endpoint runs the
real CLR `System.Text.Json.JsonSerializer.Serialize` realization. React rendering,
the Copeland reducer/dispatch flow, and the typed bridge client are the accepted
React + CLR proof reused in this fixture.

At startup the host requires the generated entry, bridge configuration/contract and
valid browser manifest/import map. Missing output produces `COPE-HOST-0001` rather
than silently serving old source-tree assets. A failed next frontend build removes the
previous build output `wwwroot` before compilation, so it cannot masquerade as a
current successful launch.

## Asset and publish layout

```text
bin/Debug/net10.0/
  StandaloneWebM0.exe
  StandaloneWebM0.dll
  wwwroot/
    index.html
    Main.js
    Bridge.js
    packages/
    import-map.json
    bridge-contract.json
```

Folder-based self-contained publish is proven terminology. This M0 does not claim
single-file deployment because browser assets intentionally remain external. It is
compatible with normal reverse proxies and service supervisors, but does not add TLS,
authentication, containers, installers, or desktop-shell behavior.

## Security and limitations

The default bind is loopback-only and no directory browsing or source directories are
served. The bridge registers generated fixed endpoints only; it has no reflection or
dynamic operation dispatch. Public deployment still needs ordinary production
security decisions. Browser tab closure does not stop the server; Ctrl+C and normal
host termination do.

Deferred: single-file embedding, hot reload/HMR, reverse-proxy/TLS productization,
authentication, service installation, embedded Chromium/Electron/WebView hosts, and
deep desktop integration.
