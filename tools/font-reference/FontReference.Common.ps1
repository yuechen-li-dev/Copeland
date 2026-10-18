function Resolve-FullPath {
    param(
        [string]$RepositoryRoot,
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
}

function Find-BrowserPath {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath)) {
        if (-not (Test-Path -LiteralPath $PreferredPath)) {
            throw "BrowserPath does not exist: $PreferredPath"
        }

        return [System.IO.Path]::GetFullPath($PreferredPath)
    }

    $candidates = @(
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Convert-ToFileUri {
    param([string]$PathValue)

    return ([System.Uri]([System.IO.Path]::GetFullPath($PathValue))).AbsoluteUri
}

function Get-CanonicalReferenceFontPath {
    param([string]$RepositoryRoot)

    $fontPath = Join-Path $RepositoryRoot "src\Aurelian\Aurelian.Assets\Defaults\Fonts\CrimsonText-Regular.ttf"
    if (-not (Test-Path -LiteralPath $fontPath -PathType Leaf)) {
        throw "The canonical Crimson Text reference font is missing: $fontPath"
    }

    return [System.IO.Path]::GetFullPath($fontPath)
}

function Invoke-BrowserProcess {
    param(
        [string]$BrowserExe,
        [string[]]$Arguments,
        [string]$Operation,
        [ValidateRange(1, 600)]
        [int]$TimeoutSeconds = 30
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $BrowserExe
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $killFailure = $null
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        if (-not $process.Start()) {
            throw "Could not start browser for $Operation."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timeoutMilliseconds = $TimeoutSeconds * 1000

        if (-not $process.WaitForExit($timeoutMilliseconds)) {
            try {
                $process.Kill($true)
                $process.WaitForExit(5000) | Out-Null
            }
            catch {
                $killFailure = $_.Exception.Message
            }

            $stdout = if ($stdoutTask.Wait(5000)) {
                $stdoutTask.GetAwaiter().GetResult()
            }
            else {
                "[stdout capture did not finish after the browser process was stopped]"
            }
            $stderr = if ($stderrTask.Wait(5000)) {
                $stderrTask.GetAwaiter().GetResult()
            }
            else {
                "[stderr capture did not finish after the browser process was stopped]"
            }
            $killDetails = if ($killFailure) {
                "`nProcess-tree stop error: $killFailure"
            }
            else {
                ""
            }

            throw "Browser $Operation exceeded the $TimeoutSeconds second timeout.`nstdout:`n$stdout`nstderr:`n$stderr$killDetails"
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()

        if ($process.ExitCode -ne 0) {
            throw "Browser $Operation exited with code $($process.ExitCode).`nstdout:`n$stdout`nstderr:`n$stderr"
        }

        return [pscustomobject]@{
            StandardOutput = $stdout
            StandardError = $stderr
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-BrowserScreenshot {
    param(
        [string]$BrowserExe,
        [string]$Url,
        [string]$ScreenshotPath,
        [int]$Width,
        [int]$Height
    )

    $arguments = @(
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        "--allow-file-access-from-files",
        "--virtual-time-budget=3000",
        "--window-size=$Width,$Height",
        "--screenshot=$ScreenshotPath",
        $Url
    )

    Invoke-BrowserProcess `
        -BrowserExe $BrowserExe `
        -Arguments $arguments `
        -Operation "screenshot to $ScreenshotPath" | Out-Null

    $attempt = 0
    while ($attempt -lt 20 -and -not (Test-Path -LiteralPath $ScreenshotPath)) {
        Start-Sleep -Milliseconds 250
        $attempt++
    }

    if (-not (Test-Path -LiteralPath $ScreenshotPath)) {
        throw "Expected screenshot was not created: $ScreenshotPath"
    }
}

function Invoke-BrowserDumpDom {
    param(
        [string]$BrowserExe,
        [string]$Url,
        [string]$ExpectedOutputId
    )

    $arguments = @(
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        "--allow-file-access-from-files",
        "--virtual-time-budget=3000",
        "--dump-dom",
        $Url
    )

    $result = Invoke-BrowserProcess `
        -BrowserExe $BrowserExe `
        -Arguments $arguments `
        -Operation "DOM capture for $Url"

    try {
        Get-JsonFromDom -DomLines $result.StandardOutput -ElementId $ExpectedOutputId | Out-Null
    }
    catch {
        $stderr = if ([string]::IsNullOrWhiteSpace($result.StandardError)) {
            "[empty]"
        }
        else {
            $result.StandardError.Trim()
        }

        throw "Browser DOM capture for '$Url' did not produce JSON output '$ExpectedOutputId'. The font-dependent page may have stopped before rendering; verify that the canonical reference font exists and the browser can read it. Browser stderr:`n$stderr"
    }

    return $result.StandardOutput
}

function Get-JsonFromDom {
    param(
        [object]$DomLines,
        [string]$ElementId
    )

    if ($null -eq $DomLines) {
        throw "Browser DOM dump did not return any content."
    }

    $dom = if ($DomLines -is [System.Array]) {
        [string]::Join([Environment]::NewLine, [string[]]$DomLines)
    }
    else {
        [string]$DomLines
    }

    $pattern = '<pre[^>]*id="' + [System.Text.RegularExpressions.Regex]::Escape($ElementId) + '"[^>]*>(?<json>\{.*\})</pre>'
    $match = [System.Text.RegularExpressions.Regex]::Match(
        $dom,
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    if (-not $match.Success) {
        throw "JSON payload '$ElementId' was not found in browser DOM output."
    }

    return $match.Groups["json"].Value
}

function Build-QueryString {
    param([hashtable]$Values)

    $pairs = foreach ($key in $Values.Keys) {
        $encodedKey = [System.Uri]::EscapeDataString([string]$key)
        $encodedValue = [System.Uri]::EscapeDataString([string]$Values[$key])
        "$encodedKey=$encodedValue"
    }

    return [string]::Join("&", $pairs)
}
