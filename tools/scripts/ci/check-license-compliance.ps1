param(
    [Parameter(Mandatory = $true)]
    [string]$SbomPath,

    [Parameter(Mandatory = $true)]
    [string]$PolicyPath,

    [Parameter(Mandatory = $true)]
    [string]$ReportPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SbomPath)) {
    throw "SBOM file not found: $SbomPath"
}

if (-not (Test-Path $PolicyPath)) {
    throw "Policy file not found: $PolicyPath"
}

$sbom = Get-Content $SbomPath -Raw | ConvertFrom-Json -Depth 100
$policy = Get-Content $PolicyPath -Raw | ConvertFrom-Json -Depth 20

$allowed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$blocked = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($id in $policy.allowedLicenseIds) { [void]$allowed.Add([string]$id) }
foreach ($id in $policy.blockedLicenseIds) { [void]$blocked.Add([string]$id) }

$allowedUnknownPackages = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($pkg in @($policy.allowedUnknownLicensePackages)) { [void]$allowedUnknownPackages.Add([string]$pkg) }

function Resolve-LicenseTokens {
    param([string]$LicenseValue)

    if ([string]::IsNullOrWhiteSpace($LicenseValue)) {
        return @()
    }

    # Split SPDX expressions conservatively by AND/OR and trim wrappers.
    $tokens = $LicenseValue -split '\s+OR\s+|\s+AND\s+|\(|\)' |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne "" }

    return $tokens
}

$results = @()
$nonCompliant = @()

$components = @($sbom.components)
foreach ($component in $components) {
    $licenseValues = @()

    foreach ($licenseEntry in @($component.licenses)) {
        if ($null -ne $licenseEntry.license.id) {
            $licenseValues += [string]$licenseEntry.license.id
            continue
        }

        if ($null -ne $licenseEntry.license.name) {
            $licenseValues += [string]$licenseEntry.license.name
            continue
        }

        if ($null -ne $licenseEntry.expression) {
            $licenseValues += [string]$licenseEntry.expression
            continue
        }
    }

    $status = "Compliant"
    $reasons = @()

    if ($licenseValues.Count -eq 0) {
        $status = "Unknown"
        $reasons += "No license metadata."
    }
    else {
        $allTokens = @()
        foreach ($licenseValue in $licenseValues) {
            $tokens = Resolve-LicenseTokens -LicenseValue $licenseValue
            if ($tokens.Count -eq 0) {
                $allTokens += $licenseValue
            }
            else {
                $allTokens += $tokens
            }
        }

        foreach ($token in $allTokens) {
            if ($blocked.Contains($token)) {
                $status = "Blocked"
                $reasons += "Blocked license '$token'."
            }
            elseif (-not $allowed.Contains($token)) {
                if ($status -eq "Compliant") {
                    $status = "Unknown"
                }
                $reasons += "Unknown/unapproved license '$token'."
            }
        }
    }

    $item = [PSCustomObject]@{
        Name = [string]$component.name
        Version = [string]$component.version
        Licenses = $licenseValues
        Status = $status
        Reasons = $reasons
    }

    $results += $item

    $isUnknownException = $status -eq "Unknown" -and $allowedUnknownPackages.Contains([string]$component.name)

    if ($isUnknownException) {
        $item.Status = "CompliantByException"
        $item.Reasons = @("Unknown license allowed by policy exception for package '$($component.name)'.")
        continue
    }

    if ($status -eq "Blocked" -or ($status -eq "Unknown" -and $policy.failOnUnknownLicense)) {
        $nonCompliant += $item
    }
}

$summary = [PSCustomObject]@{
    GeneratedUtc = (Get-Date).ToUniversalTime().ToString("o")
    ComponentsScanned = $results.Count
    NonCompliantCount = $nonCompliant.Count
    FailOnUnknownLicense = [bool]$policy.failOnUnknownLicense
    Results = $results
}

$reportDir = Split-Path -Parent $ReportPath
if ($reportDir -and -not (Test-Path $reportDir)) {
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
}

$summary | ConvertTo-Json -Depth 100 | Set-Content -Path $ReportPath -Encoding UTF8

if ($nonCompliant.Count -gt 0) {
    Write-Error ("License compliance failed. Non-compliant components: " + $nonCompliant.Count)
    exit 1
}

Write-Host "License compliance check passed for $($results.Count) components."
