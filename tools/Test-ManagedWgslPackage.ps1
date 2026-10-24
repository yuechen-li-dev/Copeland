param(
    [Parameter(Mandatory)][string]$Feed,
    [string]$Version = '0.1.0',
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$Feed = (Resolve-Path $Feed).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $output) {
    throw 'Use a fresh consumer directory.'
}
New-Item -ItemType Directory $output | Out-Null
Copy-Item (Join-Path $PSScriptRoot '../tests/fixtures/managed-wgsl-consumer/*') $output
$config = Join-Path $output 'NuGet.Config'
$escaped = [Security.SecurityElement]::Escape($Feed)
"<configuration><packageSources><clear/><add key=`"candidate`" value=`"$escaped`"/><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>" | Set-Content $config
dotnet restore "$output/Consumer.csproj" --configfile $config --packages "$output/packages" --no-cache "-p:CompilerVersion=$Version"
if ($LASTEXITCODE -ne 0) {
    throw 'Isolated consumer restore failed.'
}
dotnet run --project "$output/Consumer.csproj" -c Release --no-restore "-p:CompilerVersion=$Version" "-p:RestorePackagesPath=$output/packages" -m:1 -nr:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) {
    throw 'Installed compiler consumer failed.'
}
