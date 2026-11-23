param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "check-naming.ps1")
& (Join-Path $PSScriptRoot "check-design-tokens.ps1")

dotnet restore SkyCD.slnx
dotnet build SkyCD.slnx --configuration $Configuration --no-restore
dotnet test SkyCD.slnx --configuration $Configuration --no-build
