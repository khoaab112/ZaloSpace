$desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop "Zalo Space Manager.lnk"
$icoPath = "C:\Users\khoaz\Downloads\tesst\ZaloSpace\resources\app_icon.ico"
$batPath = "C:\Users\khoaz\Downloads\tesst\Mo_ZaloSpaceManager.bat"

$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($shortcutPath)
$sc.TargetPath = $batPath
$sc.WorkingDirectory = "C:\Users\khoaz\Downloads\tesst"
if (Test-Path $icoPath) {
    $sc.IconLocation = "$icoPath,0"
}
$sc.Description = "Zalo Space Manager - Quản lý nhân bản đa tài khoản Zalo PC"
$sc.Save()

Write-Output "SUCCESS: Shortcut created at $shortcutPath"
