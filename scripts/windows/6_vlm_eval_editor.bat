@echo off
rem Short evaluation against the Unity Editor (no build needed).
rem   1. Start 5_vlm_server.bat (mock or 4B) in another window.
rem   2. Run this file, then press Play in the Unity Editor within 60 s.
rem Arguments (all optional): condition split stage episodes policy
rem   6_vlm_eval_editor.bat vlm val 1 8 policies\stage1_seg_rule50\Mosquito_s1.onnx
setlocal
set ROOT=%~dp0..\..
set PY=%ROOT%\.venv\Scripts\python.exe
set COND=%1
if "%COND%"=="" set COND=vlm
set SPLIT=%2
if "%SPLIT%"=="" set SPLIT=val
set STAGE=%3
if "%STAGE%"=="" set STAGE=1
set EPS=%4
if "%EPS%"=="" set EPS=8
set POLICY=%5
if "%POLICY%"=="" set POLICY=%ROOT%\policies\stage1_seg_rule50\Mosquito_s1.onnx
if not exist "%PY%" (echo .venv is missing: run 4_vlm_setup.bat first & pause & exit /b 1)
echo Condition %COND%, split %SPLIT%, stage %STAGE%, %EPS% episodes, policy %POLICY%
echo Press Play in the Unity Editor now.
"%PY%" "%ROOT%\python\evaluation\evaluate.py" --policy "%POLICY%" --stage %STAGE% --split %SPLIT% --condition %COND% --episodes %EPS% --out "%ROOT%\results\vlm_check"
pause
