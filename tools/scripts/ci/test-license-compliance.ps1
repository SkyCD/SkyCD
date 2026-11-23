$ErrorActionPreference = "Stop"
$testDir = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $testDir | Out-Null

try {
    $validator = Join-Path $PSScriptRoot "check-license-compliance.ps1"
    $policy = Join-Path $PSScriptRoot "../../../docs/legal/dependency-license-policy.json"
    $sbomPath = Join-Path $testDir "bom.json"
    $reportPath = Join-Path $testDir "report.json"
    $conditionalPolicy = Join-Path $testDir "conditional-policy.json"

    function Read-ProcessOutput {
        param([object[]]$Output)
        $lines = $Output | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { [string]$_ }
        }
        return (($lines -join "`n") -replace "`e\[[0-9;]*[A-Za-z]", "")
    }

    $policyDocument = Get-Content -LiteralPath $policy -Raw | ConvertFrom-Json -Depth 20
    $matrixSource = Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $policy)) $policyDocument.compatibilityMatrix
    Copy-Item -LiteralPath $matrixSource -Destination $testDir
    $policyDocument.allowedWithConditions = @(
        [PSCustomObject]@{
            package        = "Test.Lgpl.Package"
            version        = "1.0.0"
            licenseId      = "LGPL-2.1-only"
            conditions     = @("dynamic-linking", "unmodified", "notices-and-source-offer", "replaceable")
            approvalIssue  = "#925"
        },
        [PSCustomObject]@{
            package        = "Test.Mpl.Package"
            version        = "2.0.0"
            licenseId      = "MPL-2.0"
            conditions     = @("dynamic-linking", "unmodified", "notices-and-source-offer", "replaceable")
            approvalIssue  = "#925"
        }
    )
    $policyDocument | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $conditionalPolicy

    $cases = @(
        @{ Name = "SQLite"; License = "Unknown - See URL"; Status = "CompliantByException"; ExitCode = 0 },
        @{ Name = "SQLite"; License = "GPL-3.0-only"; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Tmds.DBus.Generator"; License = "Unknown - See URL"; Status = "CompliantByException"; ExitCode = 0 },
        @{ Name = "Tmds.DBus.Generator"; License = "GPL-3.0-only"; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "FirebirdSql.Data.FirebirdClient"; License = "Unknown - See URL"; Status = "CompliantByException"; ExitCode = 0 },
        @{ Name = "FirebirdSql.Data.FirebirdClient"; License = "GPL-3.0-only"; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Unapproved.Package"; License = "Unknown - See URL"; Status = "Unknown"; ExitCode = 1 },
        @{ Name = "Approved.Package"; License = "MIT"; Status = "Compliant"; ExitCode = 0 },
        @{ Name = "Compatible.Package"; License = "0BSD"; Status = "Compliant"; ExitCode = 0 },
        @{ Name = "Compatible.Package"; License = "bsd-3-clause"; Status = "Compliant"; ExitCode = 0 },
        @{ Name = "Incompatible.Package"; License = "MPL-2.0"; Status = "Incompatible"; ExitCode = 1 },
        @{ Name = "Incompatible.Package"; License = "EUPL-1.2"; Status = "Incompatible"; ExitCode = 1 },
        @{ Name = "SQLite"; License = "EUPL-1.2"; Status = "Incompatible"; ExitCode = 1 },
        @{ Name = "Twemoji.Artwork"; License = "CC-BY-4.0"; Status = "Compliant"; ExitCode = 0 },
        @{ Name = "Twemoji.Artwork"; License = "Unknown - See URL"; Url = "https://creativecommons.org/licenses/by/4.0/"; Status = "Compliant"; ExitCode = 0 },
        @{ Name = "Noncommercial.Artwork"; License = "CC-BY-NC-4.0"; Url = "https://creativecommons.org/licenses/by/4.0/"; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Unreviewed.Binary"; License = "Unknown - See URL"; Url = "https://aka.ms/deprecateLicenseUrl"; Status = "Unknown"; ExitCode = 1 },
        @{ Name = "Test.Lgpl.Package"; Version = "1.0.0"; License = "LGPL-2.1-only"; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Test.Lgpl.Package"; Version = "1.0.0"; License = "LGPL-2.1-only"; Policy = $conditionalPolicy; Status = "CompliantByCondition"; ExitCode = 0 },
        @{ Name = "Test.Lgpl.Package"; Version = "1.0.1"; License = "LGPL-2.1-only"; Policy = $conditionalPolicy; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Test.Lgpl.Package"; Version = "1.0.0"; License = "LGPL-3.0-only"; Policy = $conditionalPolicy; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Test.Lgpl.Package"; Version = "1.0.0"; License = "GPL-3.0-only"; Policy = $conditionalPolicy; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Unlisted.Lgpl.Package"; Version = "1.0.0"; License = "LGPL-2.1-only"; Policy = $conditionalPolicy; Status = "Blocked"; ExitCode = 1 },
        @{ Name = "Test.Mpl.Package"; Version = "2.0.0"; License = "MPL-2.0"; Policy = $conditionalPolicy; Status = "CompliantByCondition"; ExitCode = 0 },
        @{ Name = "Test.Mpl.Package"; Version = "2.0.1"; License = "MPL-2.0"; Policy = $conditionalPolicy; Status = "Incompatible"; ExitCode = 1 }
    )

    foreach ($case in $cases) {
        $version = if ($case.Version) { $case.Version } else { "0.92.0" }
        $license = @{ name = $case.License }
        if ($case.Url) { $license.url = $case.Url }
        @{ components = @(@{ name = $case.Name; version = $version; licenses = @(@{ license = $license }) }) } |
            ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $sbomPath
        $policyPath = if ($case.Policy) { $case.Policy } else { $policy }
        $output = & (Join-Path $PSHOME "pwsh") -NoProfile -File $validator -SbomPath $sbomPath -PolicyPath $policyPath -ReportPath $reportPath 2>&1
        $outputText = Read-ProcessOutput $output
        if ($LASTEXITCODE -ne $case.ExitCode) {
            throw "Unexpected exit code for $($case.Name): $LASTEXITCODE. $outputText"
        }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.Results[0].Status -ne $case.Status -or $report.NonCompliantCount -ne $case.ExitCode) {
            throw "Unexpected compliance report for $($case.Name)."
        }
        if ($case.ExitCode -ne 0 -and $outputText -notlike "*$($case.Name) $($version):*") {
            throw "Failure diagnostics omitted the package name and version."
        }
    }

    $invalidPolicy = Join-Path $testDir "invalid-policy.json"

    $policyDocument.conditionalLicenseIds = @("GPL-3.0-only")
    $policyDocument.allowedWithConditions = @()
    $policyDocument | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPolicy
    $output = & (Join-Path $PSHOME "pwsh") -NoProfile -File $validator -SbomPath $sbomPath -PolicyPath $invalidPolicy -ReportPath $reportPath 2>&1
    $outputText = Read-ProcessOutput $output
    if ($LASTEXITCODE -eq 0 -or $outputText -notlike "*strong-copyleft*") {
        throw "Policy validation accepted a strong-copyleft conditional license. $outputText"
    }

    $policyDocument.conditionalLicenseIds = @("Made-Up-1.0")
    $policyDocument | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPolicy
    $output = & (Join-Path $PSHOME "pwsh") -NoProfile -File $validator -SbomPath $sbomPath -PolicyPath $invalidPolicy -ReportPath $reportPath 2>&1
    $outputText = Read-ProcessOutput $output
    if ($LASTEXITCODE -eq 0 -or $outputText -notlike "*Made-Up-1.0*") {
        throw "Policy validation accepted an unverified conditional license. $outputText"
    }

    $policyDocument.conditionalLicenseIds = @("LGPL-2.1-only")
    $policyDocument.allowedWithConditions = @(
        [PSCustomObject]@{
            package        = "Test.Lgpl.Package"
            version        = "1.0.0"
            licenseId      = "LGPL-2.1-only"
            conditions     = @("dynamic-linking")
            approvalIssue  = ""
        }
    )
    $policyDocument | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invalidPolicy
    $output = & (Join-Path $PSHOME "pwsh") -NoProfile -File $validator -SbomPath $sbomPath -PolicyPath $invalidPolicy -ReportPath $reportPath 2>&1
    $outputText = Read-ProcessOutput $output
    if ($LASTEXITCODE -eq 0 -or $outputText -notlike "*approval*") {
        throw "Policy validation accepted an allowance without an approval issue. $outputText"
    }

    Write-Host "License compliance regression checks passed."
}
finally {
    Remove-Item -LiteralPath $testDir -Recurse -Force
}

# Negative cases leave LASTEXITCODE set to 1; report success only after all assertions and cleanup pass.
exit 0
