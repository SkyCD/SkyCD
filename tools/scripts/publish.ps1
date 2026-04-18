param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/publish"
)

$ErrorActionPreference = "Stop"

$targets = @(
    @{ Runtime = "win-x64"; SelfContained = "false" },
    @{ Runtime = "linux-x64"; SelfContained = "false" },
    @{ Runtime = "osx-x64"; SelfContained = "false" }
)

foreach ($target in $targets) {
    $runtime = $target.Runtime
    $output = Join-Path $OutputRoot $runtime

    dotnet publish src/SkyCD.App/SkyCD.App.csproj `
        --configuration $Configuration `
        --runtime $runtime `
        --self-contained $target.SelfContained `
        --output $output
}

Write-Host "Publish artifacts created under $OutputRoot"
