<#
.SYNOPSIS
    Enforces design-token usage for spacing, insets, typography, colors and control metrics.

.DESCRIPTION
    Fails when:
      - an AXAML file under src/ or Plugins/ declares a literal numeric FontSize,
        Spacing, RowSpacing or ColumnSpacing outside DesignTokens.axaml;
      - an AXAML file under src/ or Plugins/ declares a literal numeric Margin or Padding,
        as an attribute or as a Setter value; plain zero resets stay allowed because they
        cancel an inset inherited from a shared style;
      - an AXAML file under src/ or Plugins/ paints a surface with a hard-coded hex color,
        as an attribute or as a Setter value; brush definitions (Color="...") stay allowed
        because DesignTokens.axaml and the theme dictionaries are the single place
        literal colors may appear;
      - an AXAML file under src/ or Plugins/ declares a literal numeric CornerRadius,
        as an attribute or as a Setter value; SkyCornerRadius* tokens carry every radius;
      - an AXAML file under src/ or Plugins/ opens a column list with a literal pixel width,
        as a ColumnDefinitions attribute or as the first ColumnDefinition element; star-sized
        and Auto columns need no token, and grids use the element form with
        Width="{DynamicResource ...}" because an attribute value cannot mix a markup
        extension with the comma-separated column list;
      - a {DynamicResource Sky...} or {StaticResource Sky...} reference has no matching
        key in DesignTokens.axaml (value converters keyed *Converter live beside their
        consumer, so they stay outside this check).
#>
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
)

$ErrorActionPreference = "Stop"

$tokenFile = Join-Path $RepositoryRoot "src/SkyCD.App/Styles/DesignTokens.axaml"
$literalPattern = '(FontSize|Spacing|RowSpacing|ColumnSpacing)="[0-9.]+"'
$insetPattern = '(?:(?:[A-Za-z]+)\.)?(?:Margin|Padding)="(?<inset>[^"]*)"'
$insetSetterPattern = 'Property="(?:(?:[A-Za-z]+)\.)?(?:Margin|Padding)" Value="(?<inset>[^"]*)"'
$colorAttributePattern = '(Background|Foreground|BorderBrush|Fill|Stroke)="#[0-9A-Fa-f]{6,8}"'
$colorSetterPattern = 'Property="(Background|Foreground|BorderBrush|Fill|Stroke)" Value="#[0-9A-Fa-f]{6,8}"'
$cornerAttributePattern = 'CornerRadius="[0-9]'
$cornerSetterPattern = 'Property="CornerRadius" Value="[0-9]'
$columnAttributePattern = 'ColumnDefinitions="[0-9]+(,|")'
$columnElementPattern = '<Grid\.ColumnDefinitions>\s*<ColumnDefinition Width="[0-9]'
$referencePattern = 'DynamicResource (Sky[A-Za-z]+)'
$staticReferencePattern = 'StaticResource (Sky[A-Za-z]+)'
$definitionPattern = 'x:Key="(Sky[A-Za-z]+)"'

$violations = New-Object System.Collections.Generic.List[string]

function Add-Violation([string]$message) {
    $script:violations.Add($message)
}

function Get-RelativePath([string]$path) {
    [System.IO.Path]::GetRelativePath($RepositoryRoot, $path).Replace('\', '/')
}

function Test-LiteralInset([string]$value) {
    if ($value -notmatch '^[-0-9.,\s]+$') {
        return $false
    }

    return ($value -replace '[^0-9]', '') -match '[1-9]'
}

if (-not (Test-Path -LiteralPath $tokenFile)) {
    Add-Violation "src/SkyCD.App/Styles/DesignTokens.axaml is missing."
    Write-Host "Design token violations:" -ForegroundColor Red
    Write-Host "  - src/SkyCD.App/Styles/DesignTokens.axaml is missing." -ForegroundColor Red
    exit 1
}

$tokenText = Get-Content -LiteralPath $tokenFile -Raw
$definedTokens = @{}
foreach ($match in [regex]::Matches($tokenText, $definitionPattern)) {
    $definedTokens[$match.Groups[1].Value] = $true
}

$files = Get-ChildItem -Path (Join-Path $RepositoryRoot "src"), (Join-Path $RepositoryRoot "Plugins") `
    -Recurse -Filter "*.axaml" -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

foreach ($file in $files) {
    $relative = Get-RelativePath $file.FullName
    $text = Get-Content -LiteralPath $file.FullName -Raw

    if ($file.FullName -ne (Resolve-Path -LiteralPath $tokenFile).Path) {
        foreach ($match in [regex]::Matches($text, $literalPattern)) {
            Add-Violation "$relative uses a literal value '$($match.Value)'; use a Sky token instead."
        }

        foreach ($pattern in @($insetPattern, $insetSetterPattern)) {
            foreach ($match in [regex]::Matches($text, $pattern)) {
                $inset = $match.Groups['inset'].Value
                if (Test-LiteralInset $inset) {
                    Add-Violation "$relative uses a literal inset value '$inset'; use a Sky inset or gap token instead."
                }
            }
        }
    }

    foreach ($match in [regex]::Matches($text, $colorAttributePattern)) {
        Add-Violation "$relative paints a surface with hard-coded color '$($match.Value)'; use a theme brush or a Sky brush token instead."
    }

    foreach ($match in [regex]::Matches($text, $colorSetterPattern)) {
        Add-Violation "$relative paints a surface with hard-coded color '$($match.Value)'; use a theme brush or a Sky brush token instead."
    }

    foreach ($match in [regex]::Matches($text, $cornerAttributePattern)) {
        Add-Violation "$relative uses a literal CornerRadius value '$($match.Value)'; use a SkyCornerRadius token instead."
    }

    foreach ($match in [regex]::Matches($text, $cornerSetterPattern)) {
        Add-Violation "$relative uses a literal CornerRadius value '$($match.Value)'; use a SkyCornerRadius token instead."
    }

    foreach ($match in [regex]::Matches($text, $columnAttributePattern)) {
        Add-Violation "$relative opens a column list with a literal width '$($match.Value)'; use a Sky label-width token in element form instead."
    }

    foreach ($match in [regex]::Matches($text, $columnElementPattern)) {
        Add-Violation "$relative opens a column list with a literal Width; use a Sky label-width token instead."
    }

    foreach ($match in [regex]::Matches($text, $referencePattern)) {
        $key = $match.Groups[1].Value
        if (-not $definedTokens.ContainsKey($key)) {
            Add-Violation "$relative references undefined design token '$key'."
        }
    }

    foreach ($match in [regex]::Matches($text, $staticReferencePattern)) {
        $key = $match.Groups[1].Value
        # Value converters live beside their consumer and are keyed by convention, not by token.
        if ($key -notlike '*Converter' -and -not $definedTokens.ContainsKey($key)) {
            Add-Violation "$relative references undefined design token '$key'."
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Design token violations:" -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Host "  - $violation" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Design token check passed ($($definedTokens.Count) tokens, $($files.Count) XAML files)." -ForegroundColor Green
exit 0
