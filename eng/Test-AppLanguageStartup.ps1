param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath,

    [ValidateSet("system", "zh-CN", "en-US")]
    [string] $Language = "zh-CN",

    [ValidateSet("system", "light", "dark")]
    [string] $Theme = "dark"
)

$ErrorActionPreference = "Stop"

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$settingsDirectory = Join-Path $env:LOCALAPPDATA "KeyRadar"
$settingsPath = Join-Path $settingsDirectory "settings.json"
$originalSettings = if (Test-Path -LiteralPath $settingsPath) {
    [System.IO.File]::ReadAllBytes($settingsPath)
} else {
    $null
}
$keyRadarProcess = $null

if (Get-Process -Name "KeyRadar" -ErrorAction SilentlyContinue) {
    throw "Close the running KeyRadar instance before executing the language startup test."
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class KeyRadarLanguageTestWindow
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@

try {
    [System.IO.Directory]::CreateDirectory($settingsDirectory) | Out-Null
    $settings = [ordered]@{ Language = $Language; Theme = $Theme } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllBytes($settingsPath, [System.Text.UTF8Encoding]::new($false).GetBytes($settings))

    $keyRadarProcess = Start-Process `
        -FilePath $resolvedExecutable `
        -WorkingDirectory (Split-Path -Parent $resolvedExecutable) `
        -PassThru

    Start-Sleep -Seconds 5
    if ($keyRadarProcess.HasExited) {
        throw "KeyRadar exited during localized startup with code $($keyRadarProcess.ExitCode)."
    }

    $keyRadarProcess.Refresh()
    if (-not $keyRadarProcess.Responding -or $keyRadarProcess.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "KeyRadar did not expose a responding main window during localized startup."
    }

    $posted = [KeyRadarLanguageTestWindow]::PostMessage(
        $keyRadarProcess.MainWindowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    if (-not $posted -or -not $keyRadarProcess.WaitForExit(2000)) {
        throw "KeyRadar did not close cleanly after the localized startup test."
    }

    [pscustomobject]@{
        Language = $Language
        Theme = $Theme
        Startup = "Passed"
        GracefulExit = "Passed"
    } | Format-List
}
finally {
    if ($keyRadarProcess -and -not $keyRadarProcess.HasExited) {
        Stop-Process -Id $keyRadarProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if ($null -eq $originalSettings) {
        Remove-Item -LiteralPath $settingsPath -Force -ErrorAction SilentlyContinue
    } else {
        [System.IO.File]::WriteAllBytes($settingsPath, $originalSettings)
    }
}
