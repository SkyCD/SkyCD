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
$matrixPath = Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $PolicyPath)) $policy.compatibilityMatrix
$matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
if ($matrix.outboundLicense -ne $policy.projectLicenseId -or -not $matrix.inboundLicenses) {
    throw "Compatibility matrix does not cover SkyCD's configured license '$($policy.projectLicenseId)'."
}

$allowed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$blocked = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($id in $policy.allowedLicenseIds) { [void]$allowed.Add([string]$id) }
foreach ($id in $policy.blockedLicenseIds) { [void]$blocked.Add([string]$id) }

$allowedUnknownPackages = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($pkg in @($policy.allowedUnknownLicensePackages)) { [void]$allowedUnknownPackages.Add([string]$pkg) }

# Weak/file-scoped copyleft licenses may be used only for an exact package version that the
# policy lists explicitly. Strong copyleft is never eligible, even if listed by mistake.
$conditionalLicenses = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($id in @($policy.conditionalLicenseIds)) {
    $licenseId = [string]$id
    if ([string]::IsNullOrWhiteSpace($licenseId)) { continue }

    if ($licenseId -match '^(A?GPL|SSPL)-' -or $licenseId -match '^CC-BY-NC') {
        throw "Conditional license '$licenseId' belongs to a strong-copyleft or noncommercial family and cannot be allowed by condition."
    }

    $verdict = $matrix.inboundLicenses.PSObject.Properties[$licenseId].Value
    if (-not $blocked.Contains($licenseId) -and $verdict -ne "No") {
        throw "Conditional license '$licenseId' must be a known license that the compatibility matrix marks 'No' or that is explicitly blocked."
    }

    [void]$conditionalLicenses.Add($licenseId)
}

$conditionalAllowances = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($allowance in @($policy.allowedWithConditions)) {
    $packageName = [string]$allowance.package
    $packageVersion = [string]$allowance.version
    $licenseId = [string]$allowance.licenseId

    if ([string]::IsNullOrWhiteSpace($packageName) -or [string]::IsNullOrWhiteSpace($packageVersion) -or [string]::IsNullOrWhiteSpace($licenseId)) {
        throw "Conditional allowance entries require package, version and licenseId."
    }

    $verdict = $matrix.inboundLicenses.PSObject.Properties[$licenseId].Value
    if (-not $blocked.Contains($licenseId) -and $verdict -ne "No") {
        throw "Conditional allowance for '$packageName' uses '$licenseId', which is not a conditional license that the matrix marks 'No' or that is explicitly blocked."
    }

    if (-not $conditionalLicenses.Contains($licenseId)) {
        throw "Conditional allowance for '$packageName' uses '$licenseId', which is not listed in conditionalLicenseIds."
    }

    if (@($allowance.conditions | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -eq 0) {
        throw "Conditional allowance for '$packageName' '$packageVersion' must record the conditions it relies on."
    }

    if ([string]::IsNullOrWhiteSpace([string]$allowance.approvalIssue)) {
        throw "Conditional allowance for '$packageName' '$packageVersion' must record the approval issue."
    }

    if (-not $conditionalAllowances.Add("$packageName|$packageVersion|$licenseId")) {
        throw "Duplicate conditional allowance for '$packageName' '$packageVersion' '$licenseId'."
    }
}

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
    $componentName = [string]$component.name
    $componentVersion = [string]$component.version

    foreach ($licenseEntry in @($component.licenses)) {
        if ($null -ne $licenseEntry.license.id) {
            $licenseValues += [string]$licenseEntry.license.id
            continue
        }

        if ($null -ne $licenseEntry.expression) {
            $licenseValues += [string]$licenseEntry.expression
            continue
        }

        $licenseUrl = [string]$licenseEntry.license.url
        $licenseName = [string]$licenseEntry.license.name
        if ($policy.licenseIdsByUrl -and $licenseUrl -and ($licenseName -in @("", "Unknown - See URL", $licenseUrl))) {
            $knownLicense = $policy.licenseIdsByUrl.PSObject.Properties[$licenseUrl]
            if ($null -ne $knownLicense) {
                $licenseValues += [string]$knownLicense.Value
                continue
            }
        }

        if ($licenseName) {
            $licenseValues += $licenseName
        }
    }

    $status = "Compliant"
    $reasons = @()
    $conditionalMatch = $false

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
            $compatibility = $matrix.inboundLicenses.PSObject.Properties[$token].Value
            if ($conditionalLicenses.Contains($token) -and $conditionalAllowances.Contains("$componentName|$componentVersion|$token")) {
                $conditionalMatch = $true
                $reasons += "Conditional allowance for '$token' in '$componentName' '$componentVersion'."
                continue
            }

            if ($blocked.Contains($token)) {
                $status = "Blocked"
                $reasons += "Blocked license '$token'."
            }
            elseif ($allowed.Contains($token) -or $compatibility -in @("Yes", "Same")) {
                continue
            }
            elseif ($compatibility -eq "No") {
                if ($status -ne "Blocked") {
                    $status = "Incompatible"
                }
                $reasons += "OSADL marks '$token' incompatible with '$($policy.projectLicenseId)'."
            }
            else {
                if ($status -eq "Compliant") {
                    $status = "Unknown"
                }
                $reasons += "Compatibility with '$($policy.projectLicenseId)' could not be determined for '$token'."
            }
        }
    }

    if ($status -eq "Compliant" -and $conditionalMatch) {
        $status = "CompliantByCondition"
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

    if ($status -in @("Blocked", "Incompatible") -or ($status -eq "Unknown" -and $policy.failOnUnknownLicense)) {
        $nonCompliant += $item
    }
}

$summary = [PSCustomObject]@{
    GeneratedUtc = (Get-Date).ToUniversalTime().ToString("o")
    ProjectLicense = $policy.projectLicenseId
    CompatibilityDataSource = $matrix.source
    CompatibilityDataTimestamp = $matrix.sourceTimestamp
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
    foreach ($component in $nonCompliant) {
        Write-Host ("{0} {1}: {2}" -f $component.Name, $component.Version, ($component.Reasons -join " "))
    }
    Write-Error ("License compliance failed. Non-compliant components: " + $nonCompliant.Count)
    exit 1
}

Write-Host "License compliance check passed for $($results.Count) components."
