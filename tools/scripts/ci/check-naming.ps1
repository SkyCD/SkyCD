<#
.SYNOPSIS
    Enforces first-party assembly naming rules.

.DESCRIPTION
    Fails when:
      - Directory.Build.props does not pin AssemblyName to $(MSBuildProjectName);
      - a project folder and its .csproj name differ;
      - a project explicitly overrides AssemblyName to a value other than its name;
      - two projects produce the same assembly simple name;
      - a plugin project under Plugins/ does not match
        SkyCD.Plugin.<Kind>.<Subject>[.<Variant>] with a known <Kind>;
      - a production plugin uses Menu, Modal or Cli as <Kind> or first subject token;
      - a plugin project under Plugins/ lacks a non-empty <AssemblyTitle> or
        <Description>, or its title repeats the assembly name;
      - a test project directly under tests/ is not named <subject>.Tests
        (fixture projects are allow-listed; a subject may also name a
        source-linked shared folder under Plugins/Shared/).
#>
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
)

$ErrorActionPreference = "Stop"

$allowedKinds = @(
    "FileFormat",
    "Metadata",
    "Preview",
    "Config",
    "Feature",
    "Accounts",
    "Ai",
    "Volume",
    "Sample"
)

$surfaceWords = @("Menu", "Modal", "Cli")
$testProjectAllowList = @("SkyCD.Acp.FakeAgent", "SkyCD.TestSupport")
$pluginNamePattern = "^SkyCD\.Plugin\.(?<Kind>[A-Za-z0-9]+)(?<Subject>(?:\.[A-Za-z0-9]+)+)$"
$testFolderPattern = "^tests/[^/]+/[^/]+\.csproj$"

$violations = New-Object System.Collections.Generic.List[string]

function Add-Violation([string]$message) {
    $script:violations.Add($message)
}

function Get-RelativePath([string]$path) {
    [System.IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
}

function Test-SharedSourceSubject([string]$subject) {
    $folder = $subject -replace '^SkyCD\.Plugin\.', ''
    $sharedPath = Join-Path $RepositoryRoot "Plugins/Shared/$folder"
    if (-not (Test-Path -LiteralPath $sharedPath -PathType Container)) {
        return $false
    }

    return [bool](Get-ChildItem -LiteralPath $sharedPath -Recurse -Filter "*.cs" -File)
}

function Get-PluginMetadataViolations([string]$projectName, [string]$projectText) {
    $result = New-Object System.Collections.Generic.List[string]

    $titleMatch = [regex]::Match($projectText, '<AssemblyTitle>\s*(?<Value>[^<]*?)\s*</AssemblyTitle>')
    $title = if ($titleMatch.Success) { $titleMatch.Groups["Value"].Value } else { '' }
    if ([string]::IsNullOrWhiteSpace($title)) {
        $result.Add("Plugin '$projectName' must declare a non-empty <AssemblyTitle>.")
    }
    elseif ($title -eq $projectName -or $title -like 'SkyCD.Plugin.*') {
        $result.Add("Plugin '$projectName' must declare a human-readable <AssemblyTitle>, not the assembly name.")
    }

    $descriptionMatch = [regex]::Match($projectText, '<Description>\s*(?<Value>[^<]*?)\s*</Description>')
    $description = if ($descriptionMatch.Success) { $descriptionMatch.Groups["Value"].Value } else { '' }
    if ([string]::IsNullOrWhiteSpace($description)) {
        $result.Add("Plugin '$projectName' must declare a non-empty <Description>.")
    }

    return $result
}

function Invoke-PluginMetadataSelfTest {
    $cases = @(
        @{ Name = 'complete'; Text = '<PropertyGroup><AssemblyTitle>TOML</AssemblyTitle><Description>Reads TOML catalogs.</Description></PropertyGroup>'; Expected = 0 },
        @{ Name = 'missing'; Text = '<PropertyGroup><Company>SkyCD</Company></PropertyGroup>'; Expected = 2 },
        @{ Name = 'assembly-name-title'; Text = '<PropertyGroup><AssemblyTitle>SkyCD.Plugin.FileFormat.Toml</AssemblyTitle><Description>Reads TOML catalogs.</Description></PropertyGroup>'; Expected = 1 },
        @{ Name = 'blank-description'; Text = '<PropertyGroup><AssemblyTitle>TOML</AssemblyTitle><Description>  </Description></PropertyGroup>'; Expected = 1 }
    )

    foreach ($case in $cases) {
        $actual = @(Get-PluginMetadataViolations -projectName 'SkyCD.Plugin.FileFormat.Toml' -projectText $case.Text).Count
        if ($actual -ne $case.Expected) {
            Add-Violation "Plugin metadata self-test '$($case.Name)' expected $($case.Expected) violation(s) but found $actual."
        }
    }
}

Invoke-PluginMetadataSelfTest

$propsPath = Join-Path $RepositoryRoot "Directory.Build.props"
if (-not (Test-Path -LiteralPath $propsPath)) {
    Add-Violation "Directory.Build.props is missing at the repository root."
}
else {
    $propsText = Get-Content -LiteralPath $propsPath -Raw
    if ($propsText -notmatch '<AssemblyName>\s*\$\(MSBuildProjectName\)\s*</AssemblyName>') {
        Add-Violation "Directory.Build.props must pin <AssemblyName>`$(MSBuildProjectName)</AssemblyName>."
    }
}

$projectFiles = Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -Filter "*.csproj" -File |
    Where-Object {
        $relative = Get-RelativePath $_.FullName
        $relative -notmatch '(^|/)(obj|bin|legacy)/'
    }

$projectNames = New-Object System.Collections.Generic.HashSet[string]
$assemblyNames = @{}
$pluginNames = New-Object System.Collections.Generic.List[string]

foreach ($projectFile in $projectFiles) {
    $relative = Get-RelativePath $projectFile.FullName
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    $folderName = Split-Path -Leaf (Split-Path -Parent $projectFile.FullName)

    [void]$projectNames.Add($projectName)

    if ($folderName -ne $projectName -and $relative -notmatch '^tests/.+/Slice/') {
        Add-Violation "Project folder '$folderName' and project file '$projectName.csproj' differ ($relative)."
    }

    $projectText = Get-Content -LiteralPath $projectFile.FullName -Raw
    $assemblyMatch = [regex]::Match($projectText, '<AssemblyName>\s*(?<Name>[^<]+?)\s*</AssemblyName>')
    $effectiveAssemblyName = $projectName
    if ($assemblyMatch.Success) {
        $effectiveAssemblyName = $assemblyMatch.Groups["Name"].Value
        if ($effectiveAssemblyName -ne $projectName) {
            Add-Violation "Project '$projectName' overrides AssemblyName to '$effectiveAssemblyName' ($relative)."
        }
    }

    if ($assemblyNames.ContainsKey($effectiveAssemblyName)) {
        Add-Violation "Duplicate assembly simple name '$effectiveAssemblyName' used by '$($assemblyNames[$effectiveAssemblyName])' and '$projectName'."
    }
    else {
        $assemblyNames[$effectiveAssemblyName] = $projectName
    }

    if ($relative -match '^Plugins/SkyCD\.Plugin\.' -or $projectName -match '^SkyCD\.Plugin\.') {
        if ($relative -notmatch '^Plugins/') {
            continue
        }

        $pluginNames.Add($projectName)
        foreach ($message in (Get-PluginMetadataViolations -projectName $projectName -projectText $projectText)) {
            Add-Violation $message
        }

        $match = [regex]::Match($projectName, $pluginNamePattern)
        if (-not $match.Success) {
            Add-Violation "Plugin '$projectName' does not match SkyCD.Plugin.<Kind>.<Subject>[.<Variant>]."
            continue
        }

        $kind = $match.Groups["Kind"].Value
        if ($allowedKinds -notcontains $kind) {
            Add-Violation "Plugin '$projectName' uses unknown kind '$kind'."
            continue
        }

        if ($surfaceWords -contains $kind) {
            Add-Violation "Plugin '$projectName' uses host surface '$kind' as its kind; name it by the feature or data it produces."
            continue
        }

        if ($kind -ne "Sample") {
            $firstSubject = ($match.Groups["Subject"].Value.TrimStart('.').Split('.')[0])
            if ($surfaceWords -contains $firstSubject) {
                Add-Violation "Production plugin '$projectName' is named after host surface '$firstSubject'; only sample plugins may use surface words."
            }
        }
    }
}

foreach ($projectFile in $projectFiles) {
    $relative = Get-RelativePath $projectFile.FullName
    if ($relative -notmatch $testFolderPattern) {
        continue
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    if ($projectName -like "*.Tests") {
        $subject = $projectName.Substring(0, $projectName.Length - ".Tests".Length)
        if (-not $projectNames.Contains($subject) -and -not (Test-SharedSourceSubject $subject)) {
            Add-Violation "Test project '$projectName' has no matching project named '$subject'."
        }
    }
    elseif ($testProjectAllowList -notcontains $projectName) {
        Add-Violation "Test project '$projectName' must be named <subject>.Tests."
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Assembly naming violations:" -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Host "  - $violation" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Assembly naming check passed ($($pluginNames.Count) plugin projects)." -ForegroundColor Green
exit 0
