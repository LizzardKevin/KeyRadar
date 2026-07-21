[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RulesDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$FingerprintSourcePath
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ($Version -notmatch "^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$") {
    throw "Version '$Version' is not a supported semantic version."
}

$resolvedRulesDirectory = [System.IO.Path]::GetFullPath($RulesDirectory)
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $resolvedRulesDirectory -PathType Container)) {
    throw "Rules directory '$resolvedRulesDirectory' does not exist."
}

if (-not $resolvedOutputPath.EndsWith(".krpack", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Debug rule pack output must use the .krpack extension."
}

$ruleFiles = @(Get-ChildItem -LiteralPath $resolvedRulesDirectory -File -Filter "*.json" |
    Sort-Object Name)
if ($ruleFiles.Count -eq 0) {
    throw "Rules directory '$resolvedRulesDirectory' does not contain any JSON rules."
}

$manifestFiles = @($ruleFiles | ForEach-Object {
    $content = [System.IO.File]::ReadAllBytes($_.FullName)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($content)
    }
    finally {
        $sha256.Dispose()
    }
    [pscustomobject][ordered]@{
        path = "rules/$($_.Name)"
        sha256 = ([System.BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
    }
})
$manifest = [pscustomobject][ordered]@{
    schemaVersion = 2
    packId = "keyradar.local"
    version = $Version
    source = "local"
    files = $manifestFiles
}
$utf8 = [System.Text.UTF8Encoding]::new($false)
$manifestBytes = $utf8.GetBytes(($manifest | ConvertTo-Json -Depth 4 -Compress))
$zipTimestamp = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)

function Test-ByteEqual([string]$firstPath, [string]$secondPath) {
    if (-not (Test-Path -LiteralPath $firstPath -PathType Leaf) -or -not (Test-Path -LiteralPath $secondPath -PathType Leaf)) {
        return $false
    }
    $first = [System.IO.FileInfo]::new($firstPath)
    $second = [System.IO.FileInfo]::new($secondPath)
    if ($first.Length -ne $second.Length) { return $false }

    $firstStream = [System.IO.File]::OpenRead($firstPath)
    $secondStream = [System.IO.File]::OpenRead($secondPath)
    $firstBuffer = [byte[]]::new(81920)
    $secondBuffer = [byte[]]::new(81920)
    try {
        while (($count = $firstStream.Read($firstBuffer, 0, $firstBuffer.Length)) -gt 0) {
            $otherCount = $secondStream.Read($secondBuffer, 0, $secondBuffer.Length)
            if ($count -ne $otherCount) { return $false }
            for ($index = 0; $index -lt $count; $index++) {
                if ($firstBuffer[$index] -ne $secondBuffer[$index]) { return $false }
            }
        }
        return $secondStream.ReadByte() -eq -1
    }
    finally {
        $firstStream.Dispose()
        $secondStream.Dispose()
    }
}

function Write-TextIfChanged([string]$path, [string]$content) {
    if ((Test-Path -LiteralPath $path -PathType Leaf) -and [System.IO.File]::ReadAllText($path, $utf8) -ceq $content) {
        return
    }
    [System.IO.File]::WriteAllText($path, $content, $utf8)
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$temporaryOutputPath = "$resolvedOutputPath.$([Guid]::NewGuid().ToString('N')).tmp"
$temporaryBackupPath = "$temporaryOutputPath.bak"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $archive = [System.IO.Compression.ZipFile]::Open(
        $temporaryOutputPath,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $manifestEntry = $archive.CreateEntry("manifest.json", [System.IO.Compression.CompressionLevel]::NoCompression)
        $manifestEntry.LastWriteTime = $zipTimestamp
        $manifestStream = $manifestEntry.Open()
        try {
            $manifestStream.Write($manifestBytes, 0, $manifestBytes.Length)
        }
        finally {
            $manifestStream.Dispose()
        }

        foreach ($ruleFile in $ruleFiles) {
            $entry = $archive.CreateEntry("rules/$($ruleFile.Name)", [System.IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = $zipTimestamp
            $entryStream = $entry.Open()
            try {
                $content = [System.IO.File]::ReadAllBytes($ruleFile.FullName)
                $entryStream.Write($content, 0, $content.Length)
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    if (Test-ByteEqual $temporaryOutputPath $resolvedOutputPath) {
        Remove-Item -LiteralPath $temporaryOutputPath -Force
    }
    elseif (Test-Path -LiteralPath $resolvedOutputPath) {
        [System.IO.File]::Replace($temporaryOutputPath, $resolvedOutputPath, $temporaryBackupPath, $true)
    }
    else {
        [System.IO.File]::Move($temporaryOutputPath, $resolvedOutputPath)
    }

    if (-not [string]::IsNullOrWhiteSpace($FingerprintSourcePath)) {
        $resolvedFingerprintPath = [System.IO.Path]::GetFullPath($FingerprintSourcePath)
        $fingerprintDirectory = Split-Path -Parent $resolvedFingerprintPath
        [System.IO.Directory]::CreateDirectory($fingerprintDirectory) | Out-Null
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $packHash = $sha256.ComputeHash([System.IO.File]::ReadAllBytes($resolvedOutputPath))
        }
        finally {
            $sha256.Dispose()
        }
        $packFingerprint = ([System.BitConverter]::ToString($packHash)).Replace("-", "").ToLowerInvariant()
        $source = @"
// <auto-generated />
namespace KeyRadar;

internal static class DevelopmentRulePackFingerprint
{
    internal const string Sha256 = "$packFingerprint";
}
"@
        Write-TextIfChanged $resolvedFingerprintPath $source
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryOutputPath) {
        Remove-Item -LiteralPath $temporaryOutputPath -Force
    }
    if (Test-Path -LiteralPath $temporaryBackupPath) {
        Remove-Item -LiteralPath $temporaryBackupPath -Force
    }
}

Write-Output "Created Debug-only unsigned development rule pack: $resolvedOutputPath"
