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
& (Join-Path $nativeRoot "x64\$Configuration\KeyRadar.Native.Host.x64.exe") --self-test
if ($LASTEXITCODE -ne 0) { throw "x64 native self-test failed." }
& (Join-Path $nativeRoot "Win32\$Configuration\KeyRadar.Native.Host.x86.exe") --self-test
if ($LASTEXITCODE -ne 0) { throw "x86 native self-test failed." }

foreach ($architecture in @(
    @{ Platform = "x64"; Suffix = "x64" },
    @{ Platform = "Win32"; Suffix = "x86" }
)) {
    $directory = Join-Path $nativeRoot "$($architecture.Platform)\$Configuration"
    $resultPath = Join-Path $directory "integration-result.pid"
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
    $observer = Start-Process `
        -FilePath (Join-Path $directory "KeyRadar.Native.Host.$($architecture.Suffix).exe") `
        -ArgumentList "--vk", "135", "--mod", "6", "--timeout", "3000", "--result", "integration-result.pid" `
        -WorkingDirectory $directory `
        -WindowStyle Hidden `
        -PassThru
    Start-Sleep -Milliseconds 200
    $testApp = Start-Process `
        -FilePath (Join-Path $directory "KeyRadar.Native.TestApp.$($architecture.Suffix).exe") `
        -ArgumentList "--vk", "135", "--mod", "6" `
        -WorkingDirectory $directory `
        -WindowStyle Hidden `
        -PassThru
    $testApp.WaitForExit(3000) | Out-Null
    $observer.WaitForExit(3000) | Out-Null
    if (-not $testApp.HasExited -or $testApp.ExitCode -ne 0 -or
        -not $observer.HasExited -or $observer.ExitCode -ne 0 -or
        -not (Test-Path -LiteralPath $resultPath) -or
        [int](Get-Content -LiteralPath $resultPath -Raw) -ne $testApp.Id) {
        if (-not $observer.HasExited) { Stop-Process -Id $observer.Id -Force }
        if (-not $testApp.HasExited) { Stop-Process -Id $testApp.Id -Force }
        throw "$($architecture.Suffix) WM_HOTKEY ownership integration test failed."
    }
    Remove-Item -LiteralPath $resultPath -Force
}
