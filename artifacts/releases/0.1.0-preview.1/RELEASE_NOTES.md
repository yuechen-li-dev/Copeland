# Copeland TS 0.1.0-preview.1

Copeland TS Preview 1 demonstrates a normal .NET 10 project where Copeland
TypeScript and C# compile, test, and run together. `.tsx` is part of the
language; React is only one possible consumer.

## What this preview proves

- `.ts` and `.cs` coexist in one SDK-style .NET 10 project.
- Copeland TS can `using` .NET APIs and authored C# types.
- npm dependencies can participate through explicit typed package contracts.
- enums, exhaustive `match`, and immutable `with` expressions compile through
  the supported .NET path.
- `.tsx` represents language-native typed structure, including non-React Text
  documents.
- `.tsxtest` tests run through `dotnet test`.
- `tsconfig.tsx` is a typed workspace and ownership manifest.
- typed templates support `template<...>`, type parameters, static parameters,
  typed result entities, and project bootstrap materialization.
- the packaged VS Code extension provides grammar, ownership, and language
  server integration for `.ts`, `.tsx`, and `.tsxtest`.

## Installation

```powershell
dotnet tool install --global Copeland.TS.Tool `
    --version 0.1.0-preview.1

Invoke-WebRequest `
    https://github.com/yuechen-li-dev/Copeland/releases/download/v0.1.0-preview.1/BootstrapTemplate.tsx `
    -OutFile BootstrapTemplate.tsx

tscl template materialize BootstrapTemplate.tsx `
    --entry BootstrapTemplate `
    --name HelloCopeland `
    --output ./HelloCopeland

cd HelloCopeland
npm install
dotnet build
dotnet test
dotnet run
```

Project-local command installation is also available:

```powershell
npm install --save-dev @copeland/tscl@0.1.0-preview.1
npx tscl --version
```

Install `copeland-ts-0.1.0-preview.1.vsix` from this GitHub release with:

```powershell
code --install-extension ./copeland-ts-0.1.0-preview.1.vsix --force
```

## Known limitations

- This is preview-quality software; APIs and package contracts may change.
- Windows x64 is the primary package-tested platform. The npm launcher rejects
  unvalidated platforms and requires .NET 10.
- Arbitrary Node compatibility and Express, NestJS, or Fastify compatibility
  are not promised.
- NativeAOT release proof and a Visual Studio extension are deferred.
- Frontend and browser workflows remain experimental/internal and are not in
  this release.
- Structural CLR inspection of `Document` is unsupported.
- JavaScript Text materialization may be unavailable.
- Templates do not provide partial specialization, SFINAE, or related advanced
  metaprogramming facilities.
- XML control flow is unsupported.
- No formatter or linter is included.
