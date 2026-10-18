$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "../..")).Path
. (Join-Path $repositoryRoot "tools/font-reference/FontReference.Common.ps1")
$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
$passed = 0

function Invoke-TestChild {
    param([string]$Source, [int]$TimeoutSeconds = 5)

    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Source))
    return Invoke-BrowserProcess -BrowserExe $pwshPath `
        -Arguments @("-NoProfile", "-EncodedCommand", $encoded) `
        -Operation "launcher regression" -TimeoutSeconds $TimeoutSeconds
}

$result = Invoke-TestChild '[Console]::Out.Write("captured output"); [Console]::Error.Write("captured error")'
if ($result.StandardOutput -ne "captured output" -or $result.StandardError -ne "captured error") {
    throw "Concurrent output/error capture changed."
}
$passed++

try {
    Invoke-TestChild '[Console]::Error.Write("expected failure"); exit 7' | Out-Null
    throw "Expected a nonzero child failure."
} catch {
    if (-not $_.Exception.Message.Contains("code 7") -or -not $_.Exception.Message.Contains("expected failure")) {
        throw
    }
}
$passed++

try {
    Invoke-TestChild 'Start-Sleep -Seconds 30' -TimeoutSeconds 1 | Out-Null
    throw "Expected a bounded child timeout."
} catch {
    if (-not $_.Exception.Message.Contains("exceeded the 1 second timeout")) {
        throw
    }
}
$passed++

$fontPath = Get-CanonicalReferenceFontPath -RepositoryRoot $repositoryRoot
if (-not $fontPath.EndsWith("CrimsonText-Regular.ttf") -or -not (Test-Path -LiteralPath $fontPath)) {
    throw "Canonical reference-font resolution changed."
}
try {
    Get-CanonicalReferenceFontPath -RepositoryRoot $PSScriptRoot | Out-Null
    throw "Expected missing-font input rejection."
} catch {
    if (-not $_.Exception.Message.Contains("canonical Crimson Text reference font is missing")) {
        throw
    }
}
$passed++

$query = Build-QueryString @{ text = 'AV & "quoted"'; fontUrl = "file:///C:/path%20with%20spaces/font.ttf" }
if (-not $query.Contains("text=AV%20%26%20%22quoted%22") -or -not $query.Contains("fontUrl=file%3A")) {
    throw "Browser query escaping changed."
}
$passed++

foreach ($element in @("capture-output", "metrics-output")) {
    $payload = Get-JsonFromDom -DomLines "<pre id=`"$element`">{`"text`":`"AV To`"}</pre>" -ElementId $element
    if (($payload | ConvertFrom-Json).text -ne "AV To") {
        throw "Browser payload parsing changed for $element."
    }
    $passed++
}

Write-Output "FONT_REFERENCE_TOOLS_PASSED cases=$passed"
