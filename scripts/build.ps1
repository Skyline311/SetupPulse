param(
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$OutputDirectory = Join-Path $projectRoot 'artifacts\win-x64'
$exePath = Join-Path $OutputDirectory 'SetupPulse.exe'
if (Get-Process -Name SetupPulse -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath }) {
    throw 'Close SetupPulse using Tray > Beenden before building. The EXE is always written to artifacts\win-x64\SetupPulse.exe.'
}
$resultsDirectory = Join-Path $projectRoot 'dev\test-results\core'

Push-Location (Join-Path $projectRoot 'ui')
try {
    npm ci
    if ($LASTEXITCODE) { throw 'npm ci failed' }
    npm run build
    if ($LASTEXITCODE) { throw 'UI build failed' }
} finally {
    Pop-Location
}

node (Join-Path $projectRoot 'scripts\collect-licenses.mjs')
if ($LASTEXITCODE) { throw 'License collection failed' }
& $Dotnet test (Join-Path $projectRoot 'tests\SetupPulse.Tests') -c Release --logger trx --results-directory $resultsDirectory
if ($LASTEXITCODE) { throw 'Tests failed' }
& $Dotnet publish (Join-Path $projectRoot 'src\SetupPulse.App') -c Release -o $OutputDirectory -p:DebugType=None -p:DebugSymbols=false -p:PublishReferencesDocumentationFiles=false
if ($LASTEXITCODE) { throw 'Publish failed' }

$packageNames = @(
    'SetupPulse.exe',
    'LICENSE',
    'THIRD-PARTY-LICENSES.txt',
    'THIRD-PARTY-NOTICES.md',
    'README.txt',
    'CHANGELOG.md'
)
foreach ($name in $packageNames | Where-Object { $_ -ne 'SetupPulse.exe' }) {
    $sourceName = if ($name -eq 'README.txt') { 'docs\PORTABLE-README.txt' } else { $name }
    Copy-Item -LiteralPath (Join-Path $projectRoot $sourceName) -Destination (Join-Path $OutputDirectory $name) -Force
}
$packageFiles = @($packageNames | ForEach-Object { Join-Path $OutputDirectory $_ })
$checksums = @($packageFiles | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    '{0} *{1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $_ -Leaf)
})
$checksumPath = Join-Path $OutputDirectory 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksumPath, $checksums)
$packageFiles += $checksumPath

[xml]$appProject = Get-Content -LiteralPath (Join-Path $projectRoot 'src\SetupPulse.App\SetupPulse.App.csproj') -Raw
$version = (Select-Xml -Xml $appProject -XPath '/Project/PropertyGroup/Version').Node.InnerText
$zipPath = Join-Path (Split-Path $OutputDirectory -Parent) ("SetupPulse-{0}-win-x64.zip" -f $version)
Compress-Archive -LiteralPath $packageFiles -DestinationPath $zipPath -Force -ErrorAction Stop
$zipHash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
[System.IO.File]::WriteAllText($zipPath + '.sha256', ('{0} *{1}' -f $zipHash.Hash.ToLowerInvariant(), (Split-Path $zipPath -Leaf)) + [Environment]::NewLine)
Write-Output ("Package: {0}" -f $zipPath)
Write-Output ("SHA256: {0}" -f $zipHash.Hash)
