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

$appBuildPath = Join-Path $repositoryRoot "src\KeyRadar.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64"
$updaterPublishPath = Join-Path $artifactsRoot "publish\updater"

Push-Location $repositoryRoot
try {
    & $DotNetPath restore KeyRadar.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    & $DotNetPath test KeyRadar.sln -c Release --no-restore --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Release tests failed." }

    & .\eng\Build-Native.ps1 -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Native x86/x64 build failed." }

    & $DotNetPath build src/KeyRadar.App/KeyRadar.App.csproj -c Release -p:Platform=x64 -t:Rebuild --no-restore --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "KeyRadar.App x64 self-contained build failed." }

    foreach ($requiredAppFile in @("KeyRadar.exe", "KeyRadar.pri", "App.xbf", "MainPage.xbf", "MainWindow.xbf", "KeyRadar.ElevatedScanner.exe")) {
        if (-not (Test-Path -LiteralPath (Join-Path $appBuildPath $requiredAppFile))) {
            throw "KeyRadar.App deployable output is missing $requiredAppFile."
        }
    }

    & $DotNetPath publish src/KeyRadar.Updater/KeyRadar.Updater.csproj -c Release -r win-x64 --self-contained true --no-restore -o $updaterPublishPath
    if ($LASTEXITCODE -ne 0) { throw "KeyRadar.Updater publish failed." }

    Copy-Item -Path (Join-Path $appBuildPath "*") -Destination $stagingPath -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $stagingPath "KeyRadar.ElevatedScanner.exe"))) {
        throw "KeyRadar staging output is missing KeyRadar.ElevatedScanner.exe."
    }
    Copy-Item -Path (Join-Path $updaterPublishPath "KeyRadar.Updater*") -Destination $stagingPath -Force
    Copy-Item -LiteralPath `
        "artifacts\native\x64\Release\KeyRadar.Native.x64.dll", `
        "artifacts\native\x64\Release\KeyRadar.Native.Host.x64.exe", `
        "artifacts\native\Win32\Release\KeyRadar.Native.x86.dll", `
        "artifacts\native\Win32\Release\KeyRadar.Native.Host.x86.exe" `
        -Destination $stagingPath -Force
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
        --rules (Join-Path $repositoryRoot "rules") `
        --output $outputPath `
        --published-at $publishedAt
    if ($LASTEXITCODE -ne 0) { throw "Release asset signing failed." }

    $applicationAsset = Join-Path $outputPath "KeyRadar-v$normalizedVersion-windows-x64.zip"
    $ruleAssetName = "KeyRadar-Rules-v$normalizedVersion.krpack"
    $ruleAsset = Join-Path $outputPath $ruleAssetName
    if (-not (Test-Path -LiteralPath $applicationAsset) -or -not (Test-Path -LiteralPath $ruleAsset)) {
        throw "Release output is missing the application ZIP or official rule pack."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($applicationAsset)
    try {
        $embeddedRuleEntries = @($archive.Entries | Where-Object { $_.FullName -like "*.krpack" })
        if ($embeddedRuleEntries.Count -ne 1 -or $embeddedRuleEntries[0].FullName -ne $ruleAssetName) {
            throw "The application ZIP must contain exactly the version-matched official rule pack."
        }

        $entryStream = $embeddedRuleEntries[0].Open()
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $embeddedHash = [BitConverter]::ToString($sha256.ComputeHash($entryStream)).Replace("-", "")
        }
        finally {
            $sha256.Dispose()
            $entryStream.Dispose()
        }

        $standaloneHash = (Get-FileHash -LiteralPath $ruleAsset -Algorithm SHA256).Hash
        if ($embeddedHash -ne $standaloneHash) {
            throw "The rule pack inside the application ZIP differs from the standalone Release asset."
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    Pop-Location
}

Get-ChildItem -LiteralPath $outputPath | Sort-Object Name | Select-Object Name, Length
