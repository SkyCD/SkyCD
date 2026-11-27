[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PluginProject
)

$ErrorActionPreference = 'Stop'
$pluginPath = (Resolve-Path -LiteralPath $PluginProject).Path
$pluginId = [IO.Path]::GetFileNameWithoutExtension($pluginPath)
$hostFilters = @{
    'SkyCD.Plugin.Sample.Config' = 'SampleConfigurationPluginTests'
    'SkyCD.Plugin.Sample.Menu' = 'SampleMenuPluginTests'
    'SkyCD.Plugin.Sample.Modal' = 'SampleModalPluginTests'
    'SkyCD.Plugin.Feature.WebBrowser' = 'WebBrowserMenuPluginTests'
}

if (-not $hostFilters.ContainsKey($pluginId) -or [IO.Path]::GetExtension($pluginPath) -ne '.csproj') {
    throw "No SkyCD host integration tests are mapped to '$pluginPath'."
}

$hostRoot = Resolve-Path (Join-Path $PSScriptRoot '../../..')
$properties = @("-p:SkyCdStandalonePluginId=$pluginId", "-p:SkyCdStandalonePluginProject=$pluginPath")
$testProjects = [ordered]@{
    'SkyCD.Plugin.Host.Tests' = $hostFilters[$pluginId]
}
if ($pluginId -eq 'SkyCD.Plugin.Sample.Config') {
    $testProjects['SkyCD.UI.Tests'] = 'PluginOptionsTests'
}

foreach ($test in $testProjects.GetEnumerator()) {
    $project = Join-Path $hostRoot "tests/$($test.Key)/$($test.Key).csproj"
    dotnet restore $project @properties --force-evaluate
    if ($LASTEXITCODE) { exit $LASTEXITCODE }

    dotnet test $project @properties --configuration Release --no-restore --filter "FullyQualifiedName~$($test.Value)"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
