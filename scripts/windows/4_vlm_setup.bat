@echo off
rem VLM planner setup. Writes everything to logs_vlm\ so the result can be read later:
rem   logs_vlm\env.txt       what this PC has (cmd only, before PowerShell starts)
rem   logs_vlm\console.txt   the whole PowerShell output
setlocal
set ROOT=%~dp0..\..
if not exist "%ROOT%\logs_vlm" mkdir "%ROOT%\logs_vlm"
set ENV=%ROOT%\logs_vlm\env.txt
echo %date% %time% > "%ENV%"
echo ROOT=%ROOT% >> "%ENV%"
ver >> "%ENV%" 2>&1
echo --- git >> "%ENV%"
git --version >> "%ENV%" 2>&1
echo --- py launcher >> "%ENV%"
py -0p >> "%ENV%" 2>&1
echo --- python on PATH >> "%ENV%"
where python >> "%ENV%" 2>&1
echo --- winget >> "%ENV%"
winget --version >> "%ENV%" 2>&1
echo --- nvidia-smi >> "%ENV%"
nvidia-smi --query-gpu=name,memory.total,memory.used,driver_version --format=csv >> "%ENV%" 2>&1
echo --- powershell >> "%ENV%"
powershell -NoProfile -Command "$PSVersionTable.PSVersion.ToString(); Get-ExecutionPolicy -List | Out-String" >> "%ENV%" 2>&1
echo --- disk >> "%ENV%"
powershell -NoProfile -Command "Get-PSDrive C | ForEach-Object { '{0:N1} GB free' -f ($_.Free / 1GB) }" >> "%ENV%" 2>&1
type "%ENV%"
echo.
echo Running vlm_setup.ps1 (output also goes to logs_vlm\console.txt) ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0vlm_setup.ps1' -Yes %* 2>&1 | Tee-Object -FilePath '%ROOT%\logs_vlm\console.txt'"
echo exit code %errorlevel% >> "%ROOT%\logs_vlm\console.txt"
echo.
pause
