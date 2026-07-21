[CmdletBinding()]
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio Installer vswhere.exe was not found."
}

$installationPath = & $vswhere -latest -products * -property installationPath
if ([string]::IsNullOrWhiteSpace($installationPath)) {
    throw "Visual Studio with MSBuild and C++ build tools is required."
}

$msbuild = Join-Path $installationPath "MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild.exe was not found in the selected Visual Studio installation."
}

$projects = @(
    "native\KeyRadar.Native\KeyRadar.Native.vcxproj",
    "native\KeyRadar.Native.Host\KeyRadar.Native.Host.vcxproj",
    "native\KeyRadar.Native.TestApp\KeyRadar.Native.TestApp.vcxproj"
)

Push-Location $repositoryRoot
try {
    foreach ($platform in @("x64", "Win32")) {
        foreach ($project in $projects) {
            & $msbuild $project /m /nologo /v:minimal /p:Configuration=$Configuration /p:Platform=$platform
            if ($LASTEXITCODE -ne 0) {
                throw "Native build failed for $project ($platform)."
            }
        }
    }
}
finally {
    Pop-Location
}

$nativeRoot = Join-Path $repositoryRoot "artifacts\native"
foreach ($architecture in @(
    @{ Platform = "x64"; Suffix = "x64" },
    @{ Platform = "Win32"; Suffix = "x86" }
)) {
    $directory = Join-Path $nativeRoot "$($architecture.Platform)\$Configuration"
    $hostPath = Join-Path $directory "KeyRadar.Native.Host.$($architecture.Suffix).exe"
    $testAppPath = Join-Path $directory "KeyRadar.Native.TestApp.$($architecture.Suffix).exe"

    & $hostPath --self-test
    if ($LASTEXITCODE -ne 0) {
        throw "$($architecture.Suffix) native self-test failed."
    }

    $occupiedResultPath = Join-Path $directory "occupied-probe.result"
    $availableResultPath = Join-Path $directory "available-probe.result"
    foreach ($path in @($occupiedResultPath, $availableResultPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    $testApp = Start-Process `
        -FilePath $testAppPath `
        -ArgumentList "--vk", "135", "--mod", "6", "--hold-ms", "1500" `
        -WorkingDirectory $directory `
        -WindowStyle Hidden `
        -PassThru
    Start-Sleep -Milliseconds 250
    & $hostPath --vk 135 --mod 6 --result $occupiedResultPath
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $occupiedResultPath) -or
        (Get-Content -LiteralPath $occupiedResultPath -Raw) -notin @("1,1409", "1,0")) {
        if (-not $testApp.HasExited) { Stop-Process -Id $testApp.Id -Force }
        throw "$($architecture.Suffix) occupied RegisterHotKey probe test failed."
    }

    if (-not $testApp.WaitForExit(3000) -or $testApp.ExitCode -ne 0) {
        if (-not $testApp.HasExited) { Stop-Process -Id $testApp.Id -Force }
        throw "$($architecture.Suffix) test registration did not release cleanly."
    }

    & $hostPath --vk 135 --mod 6 --result $availableResultPath
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $availableResultPath) -or
        (Get-Content -LiteralPath $availableResultPath -Raw) -ne "0,0") {
        throw "$($architecture.Suffix) released RegisterHotKey probe test failed."
    }

    Remove-Item -LiteralPath $occupiedResultPath, $availableResultPath -Force
}
