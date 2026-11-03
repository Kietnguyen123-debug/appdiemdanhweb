@echo off
setlocal EnableExtensions EnableDelayedExpansion

for /f "tokens=2 delims=:" %%A in ('netsh wlan show interfaces ^| findstr /i "^[ ]*BSSID[ ]*:"') do set "BSSID=%%A"

set "BSSID=%BSSID: =%"
set "BSSID=%BSSID:-=:%"

if not defined BSSID (
	echo Khong tim thay BSSID. Hay ket noi Wi-Fi roi thu lai.
	exit /b 1
)

for /f "delims=" %%A in ('powershell -NoProfile -Command "$env:BSSID.ToUpperInvariant()"') do set "BSSID=%%A"

echo %BSSID%| clip
echo BSSID da duoc copy vao clipboard: %BSSID%
exit /b 0
