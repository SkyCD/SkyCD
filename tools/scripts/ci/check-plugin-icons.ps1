<#
.SYNOPSIS
    Enforces that every first-party plugin library ships its own icon.

.DESCRIPTION
    Fails when:
      - a plugin project under Plugins/ has no Assets/icon.png;
      - Assets/icon.png is not a PNG (signature mismatch);
      - the icon exceeds the host's PluginIconResolver.MaxIconBytes limit;
      - the icon is not square or smaller than 24 pixels.

    Plugins/Directory.Build.props embeds the file as the SkyCD.Plugin.Icon.png manifest resource, which
    PluginIconResolver reads at discovery time.
#>
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
)

$ErrorActionPreference = "Stop"

$pngSignature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
$maxIconBytes = 256 * 1024
$minIconPixels = 24
$iconRelativePath = "Assets/icon.png"

$violations = New-Object System.Collections.Generic.List[string]

function Add-Violation([string]$message) {
    $script:violations.Add($message)
}

function Get-RelativePath([string]$path) {
    [System.IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
}

function Test-PngSignature([byte[]]$bytes) {
    if ($bytes.Length -lt $script:pngSignature.Length) {
        return $false
    }

    for ($index = 0; $index -lt $script:pngSignature.Length; $index++) {
        if ($bytes[$index] -ne $script:pngSignature[$index]) {
            return $false
        }
    }

    return $true
}

function Get-BigEndianInt32([byte[]]$bytes, [int]$offset) {
    return ([int]$bytes[$offset] -shl 24) -bor
        ([int]$bytes[$offset + 1] -shl 16) -bor
        ([int]$bytes[$offset + 2] -shl 8) -bor
        [int]$bytes[$offset + 3]
}

$projectFiles = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot "Plugins") -Directory |
    Where-Object { $_.Name -ne "Shared" } |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter "*.csproj" -File }
$checked = 0

foreach ($projectFile in $projectFiles) {
    $checked++
    $projectDirectory = $projectFile.Directory.FullName
    $iconPath = Join-Path $projectDirectory $iconRelativePath
    $relativeProject = Get-RelativePath $projectFile.FullName

    if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
        Add-Violation "Plugin project '$relativeProject' has no $iconRelativePath."
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($iconPath)
    if (-not (Test-PngSignature $bytes)) {
        Add-Violation "Plugin icon '$iconPath' does not start with a PNG signature."
        continue
    }

    if ($bytes.Length -gt $maxIconBytes) {
        Add-Violation "Plugin icon '$iconPath' exceeds the $maxIconBytes byte host limit."
        continue
    }

    if ($bytes.Length -lt 24) {
        Add-Violation "Plugin icon '$iconPath' is too small to describe its dimensions."
        continue
    }

    $width = Get-BigEndianInt32 $bytes 16
    $height = Get-BigEndianInt32 $bytes 20
    if ($width -ne $height) {
        Add-Violation "Plugin icon '$iconPath' must be square but is $($width)x$($height)."
    }
    elseif ($width -lt $minIconPixels) {
        Add-Violation "Plugin icon '$iconPath' must be at least ${minIconPixels}x${minIconPixels} but is $($width)x$($height)."
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Plugin icon violations:" -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Host "  - $violation" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Plugin icon check passed ($checked plugin projects)." -ForegroundColor Green
exit 0
