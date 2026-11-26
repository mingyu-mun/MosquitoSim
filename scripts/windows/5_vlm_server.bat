@echo off
rem Planner server on http://127.0.0.1:8008
rem   5_vlm_server.bat          Qwen3-VL-4B (models\Qwen3-VL-4B-Instruct, transformers, GPU)
rem   5_vlm_server.bat mock     no model: answers APPROACH to the nearest skin (pipeline check)
setlocal
set ROOT=%~dp0..\..
set PY=%ROOT%\.venv-planner\Scripts\python.exe
if not exist "%PY%" (echo .venv-planner is missing: run 4_vlm_setup.bat first & pause & exit /b 1)
if /I "%1"=="mock" (
  "%PY%" "%ROOT%\python\planner_server\server.py" --backend mock
) else (
  "%PY%" "%ROOT%\python\planner_server\server.py" --backend transformers --model "%ROOT%\models\Qwen3-VL-4B-Instruct" --warmup 2
)
pause
