$p = Get-Process -Name "powershell" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -ne "" }
foreach ($item in $p) {
    Write-Output "PID: $($item.Id) | Title: $($item.MainWindowTitle)"
}
