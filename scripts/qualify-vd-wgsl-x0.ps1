param(
    [Parameter(Mandatory = $true)][string]$X0Directory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository 'artifacts/local/vd-wgsl-x0'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$X0Directory = [IO.Path]::GetFullPath($X0Directory)
foreach ($name in @('Sphere', 'Cylinder', 'Cone', 'Torus', 'CSG')) {
    if (-not (Test-Path -LiteralPath (Join-Path $X0Directory "$name.v.ts"))) {
        throw "Missing actual CIR-DISPLAY-X0 source: $name.v.ts"
    }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$frontendProject = [Security.SecurityElement]::Escape((Join-Path $repository 'src/Copeland/Copeland.TS.Backend.Wgsl/Copeland.TS.Backend.Wgsl.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>WgslQualification</AssemblyName>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <WasmMainJSPath>bootstrap.js</WasmMainJSPath>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$frontendProject" />
    <TrimmerRootAssembly Include="WgslQualification" />
    <TrimmerRootAssembly Include="Copeland.TS" />
    <TrimmerRootAssembly Include="Copeland.TS.Backend.Wgsl" />
  </ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $OutputDirectory 'Qualification.csproj') -Value $project -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vd-wgsl-x0-browser.cs') -Destination (Join-Path $OutputDirectory 'Program.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vd-wgsl-x0-browser.js') -Destination (Join-Path $OutputDirectory 'bootstrap.js')
dotnet build (Join-Path $OutputDirectory 'Qualification.csproj') -c Release -m:1
if ($LASTEXITCODE -ne 0) { throw 'Browser qualification build failed.' }
$site = Join-Path $OutputDirectory 'bin/Release/net10.0/wwwroot'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vd-wgsl-x0-browser.js') -Destination (Join-Path $site 'bootstrap.js')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vd-wgsl-x0-browser.html') -Destination (Join-Path $site 'index.html')
foreach ($name in @('Sphere', 'Cylinder', 'Cone', 'Torus', 'CSG')) {
    Copy-Item -LiteralPath (Join-Path $X0Directory "$name.v.ts") -Destination $site
}
if (Test-Path -LiteralPath (Join-Path $X0Directory 'programs.json')) {
    Copy-Item -LiteralPath (Join-Path $X0Directory 'programs.json') -Destination (Join-Path $site 'oracle.json')
}
Copy-Item -LiteralPath (Join-Path $repository 'samples/Aurelian/ForwardTexturedM3.v.ts') -Destination $site
Get-ChildItem -LiteralPath (Join-Path $repository 'src/Aurelian/Aurelian.Shaders/Assets') -Filter '*.v.ts' | Copy-Item -Destination $site
Write-Output "Serve $site on localhost. Browser compilation uses the managed frontend/WGSL backend; DXC/Naga are not referenced."
