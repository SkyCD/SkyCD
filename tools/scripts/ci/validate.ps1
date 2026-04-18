param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

dotnet restore SkyCD.V3.slnx
dotnet build SkyCD.V3.slnx --configuration $Configuration --no-restore
dotnet test SkyCD.V3.slnx --configuration $Configuration --no-build
