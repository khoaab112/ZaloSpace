$scriptDir = $PSScriptRoot
$srcDir = Join-Path $scriptDir "src"
$outExe = Join-Path $scriptDir "..\ZaloSpaceManager.exe"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
$icoPath = Join-Path $scriptDir "resources\app_icon.ico"

$csFiles = Get-ChildItem -Path $srcDir -Filter "*.cs" -Recurse | ForEach-Object { $_.FullName }
Write-Output "Found $($csFiles.Count) source files to compile."

$refs = @(
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Xaml.dll",
    "/r:$wpfDir\WindowsBase.dll",
    "/r:$wpfDir\PresentationCore.dll",
    "/r:$wpfDir\PresentationFramework.dll"
)

$args = @("/target:winexe", "/out:$outExe", "/optimize+")
if (Test-Path $icoPath) {
    $args += "/win32icon:$icoPath"
}
$args += $refs + $csFiles

if (Test-Path $outExe) {
    Remove-Item $outExe -Force -ErrorAction SilentlyContinue
}

Write-Output "Compiling ZaloSpaceManager.exe with Cat Soldier icon..."
& $csc $args

if (Test-Path $outExe) {
    Write-Output "========================================"
    Write-Output "BUILD SUCCESSFUL!"
    Write-Output "Output: $outExe"
    Write-Output "Size: $((Get-Item $outExe).Length) bytes"
    Write-Output "========================================"
} else {
    Write-Output "BUILD FAILED!"
}
