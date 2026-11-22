@echo off
cd /d C:\MosquitoSim
start "" http://localhost:6006
.venv\Scripts\tensorboard.exe --logdir results
pause
