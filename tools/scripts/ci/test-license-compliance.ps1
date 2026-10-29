$ErrorActionPreference = "Stop"
$testDir = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $testDir | Out-Null

try {
    $validator = Join-Path $PSScriptRoot "check-license-compliance.ps1"
    $policy = Join-Path $PSScriptRoot "../../../docs/legal/dependency-license-policy.json"
    $sbomPath = Join-Path $testDir "bom.json"
    $reportPath = Join-Path $testDir "report.json"
    $cases = @(
        @{ Name = "Tmds.DBus.Generator"; License = "Unknown - See URL"; Status = "CompliantByException"; ExitCode = 0 },
        @{ Name = "Tmds.DBus.Generator"; License = "GPL-3.0-only"; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Unapproved.Package"; License = "Unknown - See URL"; Status = "Unknown"; ExitCode = 1 },
        @{ Name = "Approved.Package"; License = "MIT"; Status = "Compliant"; ExitCode = 0 }
    )

    foreach ($case in $cases) {
        @{ components = @(@{ name = $case.Name; version = "0.92.0"; licenses = @(@{ license = @{ name = $case.License } }) }) } |
            ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $sbomPath
        $output = & (Join-Path $PSHOME "pwsh") -NoProfile -File $validator -SbomPath $sbomPath -PolicyPath $policy -ReportPath $reportPath 2>&1
        if ($LASTEXITCODE -ne $case.ExitCode) {
            throw "Unexpected exit code for $($case.Name): $LASTEXITCODE. $output"
        }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.Results[0].Status -ne $case.Status -or $report.NonCompliantCount -ne $case.ExitCode) {
            throw "Unexpected compliance report for $($case.Name)."
        }
        if ($case.ExitCode -ne 0 -and ($output -join "`n") -notlike "*$($case.Name) 0.92.0:*") {
            throw "Failure diagnostics omitted the package name and version."
        }
    }
    Write-Host "License compliance regression checks passed."
}
finally {
    Remove-Item -LiteralPath $testDir -Recurse -Force
}
