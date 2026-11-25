# CopelandHello

This is the canonical Copeland TS Preview 1 mixed-language sample. It combines
Copeland-owned TypeScript, traditional TypeScript, and C# in one ordinary .NET
10 project. Use an SDK build that includes automatic npm guest generation.

```powershell
cd CopelandHello
npm ci
dotnet restore
dotnet build
dotnet run
```

Expected output:

```text
C# says hello to Copeland through System.String
lodash-es says: helloFromNpm
"helloFromNpm"
```

`CopelandNpmLockFile` tells the SDK which installed npm layout to deploy.
The SDK generates the guest, stages its runtime packages, and attaches the
CLR transport when the first npm call runs. The app still needs Node on PATH.

```powershell
dotnet publish -c Release -o publish
dotnet publish/CopelandHello.dll
```

See `docs/Copeland/preview-quickstart.md` in the Copeland distribution for the
packaged VSIX and tool setup.
