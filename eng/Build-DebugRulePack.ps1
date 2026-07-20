[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RulesDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [string]$Version
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

$outputDirectory = Split-Path -Parent $resolvedOutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$temporaryOutputPath = "$resolvedOutputPath.$([Guid]::NewGuid().ToString('N')).tmp"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $archive = [System.IO.Compression.ZipFile]::Open(
        $temporaryOutputPath,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $manifestEntry = $archive.CreateEntry("manifest.json", [System.IO.Compression.CompressionLevel]::NoCompression)
        $manifestStream = $manifestEntry.Open()
        try {
            $manifestStream.Write($manifestBytes, 0, $manifestBytes.Length)
        }
        finally {
            $manifestStream.Dispose()
        }

        foreach ($ruleFile in $ruleFiles) {
            $entry = $archive.CreateEntry("rules/$($ruleFile.Name)", [System.IO.Compression.CompressionLevel]::NoCompression)
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

    if (Test-Path -LiteralPath $resolvedOutputPath) {
        Remove-Item -LiteralPath $resolvedOutputPath -Force
    }
    [System.IO.File]::Move($temporaryOutputPath, $resolvedOutputPath)
}
finally {
    if (Test-Path -LiteralPath $temporaryOutputPath) {
        Remove-Item -LiteralPath $temporaryOutputPath -Force
    }
}

Write-Output "Created Debug-only unsigned development rule pack: $resolvedOutputPath"
