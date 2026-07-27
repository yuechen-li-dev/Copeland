# COPELAND-NUGET-M1 fixture

This fixture proves the native package law. `Producer` compiles Copeland TS to
the ordinary `Example.Copeland.dll`, then `dotnet pack` includes that binary,
`copeland/contract.v1.json`, and a conventional `buildTransitive` target. The
target contributes one exact `CopelandPackageContract` item; it does not search
the NuGet cache.

From the repository root, run:

```powershell
dotnet build src/Copeland/Copeland.TS.MSBuild/Copeland.TS.MSBuild.csproj --no-restore
dotnet restore tests/fixtures/copeland-nuget-m1/Producer/Example.Copeland.csproj
dotnet pack tests/fixtures/copeland-nuget-m1/Producer/Example.Copeland.csproj --no-restore -o tests/fixtures/copeland-nuget-m1/LocalFeed
dotnet restore tests/fixtures/copeland-nuget-m1/Consumer/Consumer.csproj --configfile tests/fixtures/copeland-nuget-m1/NuGet.config --packages tests/fixtures/copeland-nuget-m1/.packages
dotnet build tests/fixtures/copeland-nuget-m1/Consumer/Consumer.csproj --no-restore
dotnet run --project tests/fixtures/copeland-nuget-m1/Consumer/Consumer.csproj --no-build
```

Expected output is `42`. The Copeland import resolves `example/parser` from the
contract; `using Example.Runtime` is ordinary CLR metadata binding from the
same PackageReference assembly. Generated C# has a direct call to
`global::Example.Copeland.Copeland.Parser.Parse`.
