# CopelandHello

This is the canonical Copeland TS Preview 1 mixed-language sample. It combines
Copeland-owned TypeScript, traditional TypeScript, and C# in one ordinary .NET
10 project.

```powershell
cd CopelandHello
npm install
dotnet restore
dotnet build
dotnet run
```

Expected output:

```text
C# says hello to Copeland through System.String
lodash-es says: helloFromNpm
```

See `docs/Copeland/preview-quickstart.md` in the Copeland distribution for the
packaged VSIX and tool setup.
