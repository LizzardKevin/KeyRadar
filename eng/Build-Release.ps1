[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputDirectory = "artifacts/release",

    [string]$DotNetPath = "dotnet"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$normalizedVersion = $Version.TrimStart("v")
if ($normalizedVersion -notmatch "^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$") {
    throw "Version '$Version' is not a supported semantic version."
}

if ([string]::IsNullOrWhiteSpace($env:KEYRADAR_ED25519_PRIVATE_KEY)) {
    throw "KEYRADAR_ED25519_PRIVATE_KEY is required to create signed release assets."
}

$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
if (-not $outputPath.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output must be a child of the repository artifacts directory."
}

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$stagingPath = Join-Path $artifactsRoot "staging\KeyRadar"
if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null

$appPublishPath = Join-Path $artifactsRoot "publish\app"
$updaterPublishPath = Join-Path $artifactsRoot "publish\updater"

Push-Location $repositoryRoot
try {
    & $DotNetPath restore KeyRadar.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    & $DotNetPath test KeyRadar.sln -c Release --no-restore --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Release tests failed." }

    & $DotNetPath publish src/KeyRadar.App/KeyRadar.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o $appPublishPath
    if ($LASTEXITCODE -ne 0) { throw "KeyRadar.App publish failed." }

    & $DotNetPath publish src/KeyRadar.Updater/KeyRadar.Updater.csproj -c Release -r win-x64 --self-contained true --no-restore -o $updaterPublishPath
    if ($LASTEXITCODE -ne 0) { throw "KeyRadar.Updater publish failed." }

    Copy-Item -Path (Join-Path $appPublishPath "*") -Destination $stagingPath -Recurse -Force
    Copy-Item -Path (Join-Path $updaterPublishPath "KeyRadar.Updater*") -Destination $stagingPath -Force
    Copy-Item -LiteralPath LICENSE, README.md -Destination $stagingPath -Force
    Get-ChildItem -LiteralPath $stagingPath -File -Recurse -Filter "*.pdb" | Remove-Item -Force

    $commit = (git rev-parse HEAD).Trim()
    $publishedAt = (git show -s --format=%cI HEAD).Trim()
    $sdkVersion = (& $DotNetPath --version).Trim()
    $buildInfo = @"
KeyRadar v$normalizedVersion
Commit: $commit
.NET SDK: $sdkVersion
Runtime: win-x64 self-contained
Source: https://github.com/LizzardKevin/KeyRadar/tree/$commit
Reproduce: ./eng/Build-Release.ps1 -Version v$normalizedVersion
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingPath "REPRODUCIBLE-BUILD.txt"),
        $buildInfo,
        [System.Text.UTF8Encoding]::new($false))

    & $DotNetPath run --project eng/KeyRadar.ReleaseTool/KeyRadar.ReleaseTool.csproj -c Release -- `
        --version $normalizedVersion `
        --staging $stagingPath `
        --output $outputPath `
        --published-at $publishedAt
    if ($LASTEXITCODE -ne 0) { throw "Release asset signing failed." }
}
finally {
    Pop-Location
}

Get-ChildItem -LiteralPath $outputPath | Sort-Object Name | Select-Object Name, Length
