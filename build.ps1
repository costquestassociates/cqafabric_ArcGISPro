[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'src\CqaFabricPro\CqaFabricPro.csproj'
$artifactDirectory = Join-Path $projectRoot 'artifacts'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

dotnet build $project --configuration $Configuration --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$outputDirectory = Join-Path $projectRoot "src\CqaFabricPro\bin\$Configuration\net10.0-windows"
$packageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("CqaFabricPro-" + [guid]::NewGuid())
$packagePath = Join-Path $artifactDirectory 'CqaFabricPro.esriAddInX'

try {
    New-Item -ItemType Directory -Path $packageDirectory | Out-Null
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $installDirectory = Join-Path $packageDirectory 'Install'
    New-Item -ItemType Directory -Path $installDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\CqaFabricPro\Config.daml') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $outputDirectory 'CqaFabricPro.dll') -Destination $installDirectory
    Copy-Item -LiteralPath (Join-Path $outputDirectory 'CqaFabricPro.deps.json') -Destination $installDirectory
    Copy-Item -LiteralPath (Join-Path $outputDirectory 'CqaFabricPro.pdb') -Destination $installDirectory -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath ($packagePath + '.zip') -Force
    Move-Item -LiteralPath ($packagePath + '.zip') -Destination $packagePath -Force
}
finally {
    if (Test-Path -LiteralPath $packageDirectory) {
        Remove-Item -LiteralPath $packageDirectory -Recurse -Force
    }
}

Write-Host "Created $packagePath"
