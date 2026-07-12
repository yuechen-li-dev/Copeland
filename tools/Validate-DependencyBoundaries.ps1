[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$exceptionPath = Join-Path $PSScriptRoot "dependency-boundary-exceptions.json"
$exceptions = @(Get-Content -Raw $exceptionPath | ConvertFrom-Json)

function Get-RepositoryRelativePath {
    param([string]$Path)

    return ([IO.Path]::GetRelativePath($repositoryRoot, $Path)).Replace("\", "/")
}

function Get-Subsystem {
    param([string]$RelativePath)

    if ($RelativePath.StartsWith("src/Copeland/")) { return "Copeland" }
    if ($RelativePath.StartsWith("src/Machina.UI/")) { return "Machina.UI" }
    if ($RelativePath.StartsWith("src/Aurelian/")) { return "Aurelian" }
    if ($RelativePath.StartsWith("src/Integrations/")) { return "Integrations" }
    return $null
}

function Test-Exception {
    param(
        [string]$Project,
        [string]$Package
    )

    return @($exceptions | Where-Object {
        $_.project -eq $Project -and $_.package -eq $Package
    }).Count -gt 0
}

$violations = [Collections.Generic.List[string]]::new()
$projects = Get-ChildItem (Join-Path $repositoryRoot "src") -Recurse -Filter *.csproj | Sort-Object FullName

foreach ($projectFile in $projects) {
    $projectPath = Get-RepositoryRelativePath $projectFile.FullName
    $sourceSubsystem = Get-Subsystem $projectPath
    if ($null -eq $sourceSubsystem) { continue }

    [xml]$project = Get-Content -Raw $projectFile.FullName

    foreach ($packageReference in @($project.Project.ItemGroup.PackageReference)) {
        $package = [string]$packageReference.Include
        if ($sourceSubsystem -in @("Copeland", "Machina.UI") -and $package -like "Dominatus.*") {
            if (-not (Test-Exception $projectPath $package)) {
                $violations.Add("$projectPath references prohibited Dominatus package $package without a recorded exception.")
            }
        }
    }

    foreach ($projectReference in @($project.Project.ItemGroup.ProjectReference)) {
        $targetPath = [IO.Path]::GetFullPath((Join-Path $projectFile.DirectoryName ([string]$projectReference.Include)))
        $targetRelativePath = Get-RepositoryRelativePath $targetPath
        $targetSubsystem = Get-Subsystem $targetRelativePath

        if ($targetRelativePath.StartsWith("samples/")) {
            $violations.Add("$projectPath references sample project $targetRelativePath.")
            continue
        }

        if ($null -eq $targetSubsystem -or $sourceSubsystem -eq $targetSubsystem) {
            continue
        }

        if ($sourceSubsystem -ne "Integrations") {
            $violations.Add("$projectPath has cross-subsystem production reference to $targetRelativePath; use an explicitly named integration project.")
            continue
        }

        if ($targetSubsystem -notin @("Copeland", "Machina.UI", "Aurelian")) {
            $violations.Add("$projectPath references unsupported integration target $targetRelativePath.")
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("Dependency boundary validation failed:`n- " + ($violations -join "`n- "))
    exit 1
}

Write-Output "Dependency boundary validation passed for $($projects.Count) production projects."
Write-Output "Recorded temporary exceptions: $($exceptions.Count)."
