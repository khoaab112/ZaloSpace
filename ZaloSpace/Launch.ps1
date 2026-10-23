$scriptDir = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $scriptDir "ZaloSpaceManager.exe"
if (-not (Test-Path $exePath)) {
    $exePath = "C:\Users\khoaz\Downloads\tesst\ZaloSpaceManager.exe"
}

if (Test-Path $exePath) {
    try {
        $rawBytes = [System.IO.File]::ReadAllBytes($exePath)
        $assembly = [System.Reflection.Assembly]::Load($rawBytes)
        $entryPoint = $assembly.EntryPoint
        if ($entryPoint -ne $null) {
            $entryPoint.Invoke($null, @(,[string[]]$args))
        }
    } catch {
        [System.Windows.MessageBox]::Show($_.Exception.ToString(), "Lỗi khởi động", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Error)
    }
}
