# CTS-STANDALONE-WEB-M0

This is the canonical Copeland React + CLR standalone web fixture. `dotnet build`
materializes the locked npm browser graph with TSPack, generates the Copeland
browser and ASP.NET bridge artifacts, and produces a normal ASP.NET Core executable.

From this directory:

```console
dotnet build .\StandaloneWebM0.csproj
.\bin\Debug\net10.0\StandaloneWebM0.exe
```

The executable listens on `127.0.0.1` with a dynamically allocated port, prints the
actual URL, and opens the system default browser after ASP.NET Core is ready.

For a headless/server launch:

```console
.\bin\Debug\net10.0\StandaloneWebM0.exe --no-browser
.\bin\Debug\net10.0\StandaloneWebM0.exe --no-browser --urls http://0.0.0.0:8080
```

`--port <port>`, `--host <address>`, `--urls <urls>`, and `--open-browser` are also
supported. The default is loopback-only; a non-loopback host or URL prints a warning.

Build-time TSPack is located through the `TSPackExecutable` MSBuild property. It
defaults to the sibling `tspack` checkout used by this repository, and can be set
explicitly when building elsewhere:

```console
dotnet build .\StandaloneWebM0.csproj -p:TSPackExecutable=C:\tools\tspack.exe
```

Publish creates a self-contained, single-folder application (not a single-file app):

```console
dotnet publish .\StandaloneWebM0.csproj -c Release -r win-x64 --self-contained true
```

The published folder contains the executable, normal .NET deployment files, and
`wwwroot` generated browser assets. Launching it does not require Node, npm, TSPack,
esbuild, Vite, or source files.

Automated launch checks can set `COPELAND_BROWSER_LAUNCHER=record`; the host records
the post-readiness URL instead of asking the operating system to open a browser.
