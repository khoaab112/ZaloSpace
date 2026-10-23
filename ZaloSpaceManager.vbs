Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "powershell.exe -Sta -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""c:\Users\khoaz\Downloads\tesst\ZaloSpace\Launch.ps1""", 0, False
