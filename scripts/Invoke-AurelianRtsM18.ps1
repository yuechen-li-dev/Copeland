param(
    [switch]$AuditCorpus,
    [switch]$FullValidation
)

$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
Push-Location $taskRepository
try {
    if ($AuditCorpus) {
        python scripts/Build-RtsM18Audit.py
        if ($LASTEXITCODE -ne 0) {
            throw 'Corpus audit generation failed.'
        }
    }

    $taskProjects = @(
        'tests/Aurelian/Aurelian.Strategy.Tests/Aurelian.Strategy.Tests.csproj',
        'tests/Machina.UI/Machina.Pipeline.Tests/Machina.Pipeline.Tests.csproj'
    )
    if ($FullValidation) {
        $taskProjects += @(
            'Aurelian.slnx',
            'Copeland.slnx',
            'Machina.UI.slnx',
            'JointTaskForce.slnx',
            '../Dominatus/tests/Dominatus.Core.Tests/Dominatus.Core.Tests.csproj',
            '../Dominatus/tests/Dominatus.SpriteForge.Tests/Dominatus.SpriteForge.Tests.csproj',
            '../InputMan/tests/InputMan.Core.Tests/InputMan.Core.Tests.csproj'
        )
    }
    foreach ($taskProject in $taskProjects) {
        dotnet test $taskProject -c Release -m:1 --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Validation failed: $taskProject"
        }
    }

    dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release --no-build -- --proof
    if ($LASTEXITCODE -ne 0) {
        throw 'Strategy semantic/render proof failed.'
    }
    dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release --no-build -- --launch-smoke
    if ($LASTEXITCODE -ne 0) {
        throw 'Native strategy launch failed.'
    }
}
finally {
    Pop-Location
}
