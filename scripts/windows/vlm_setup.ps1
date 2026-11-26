# VLM planner setup for Windows (11/25). Run through 4_vlm_setup.bat from a git clone of the repository.
# Safe to run again: finished steps are skipped. Everything printed also goes to logs_vlm\setup_report.txt.
#   -Model    Hub id to download (default Qwen/Qwen3-VL-4B-Instruct, BF16, about 9 GB on disk and in VRAM)
#   -Images   optional folder with 5+ planner images (PNG/JPG) for the self-test
param(
  [string]$Model = 'Qwen/Qwen3-VL-4B-Instruct',
  [string]$Images = ''
)
$ErrorActionPreference = 'Continue'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$LogDir = Join-Path $Root 'logs_vlm'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path (Join-Path $LogDir 'setup_report.txt') -Force | Out-Null

function Have($cmd) { return [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }
function Step($m)   { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Warn($m)   { Write-Host $m -ForegroundColor Yellow }
function Ok($m)     { Write-Host $m -ForegroundColor Green }
function Fail($m)   { Write-Host ''; Write-Host "STOPPED: $m" -ForegroundColor Red; Stop-Transcript | Out-Null; exit 1 }
function RefreshPath {
  $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}
function Ask($question) { $a = Read-Host "$question [Y/n]"; return ($a -eq '' -or $a -match '^[Yy]') }
function FindPy($ver) {
  if (Have 'py') {
    $exe = & py "-$ver" -c "import sys; print(sys.executable)" 2>$null
    if ($LASTEXITCODE -eq 0 -and $exe) { return ($exe | Select-Object -First 1).Trim() }
  }
  $short = $ver -replace '\.', ''
  foreach ($c in @((Join-Path $env:LOCALAPPDATA "Programs\Python\Python$short\python.exe"),
                   "C:\Program Files\Python$short\python.exe", "C:\Python$short\python.exe")) {
    if (Test-Path $c) { return $c }
  }
  return $null
}
function NeedPy($ver, $wingetId) {
  $p = FindPy $ver
  if (-not $p -and (Have 'winget')) {
    if (Ask "Python $ver is not installed. Install it with winget?") {
      winget install --id $wingetId -e --source winget --accept-package-agreements --accept-source-agreements
      RefreshPath
      $p = FindPy $ver
    }
  }
  return $p
}

Write-Host "Repository: $Root"
Write-Host ('HEAD: ' + (git -C $Root log -1 --format='%h %ad %s' --date=short 2>$null))

# ---------------------------------------------------------------- 1. GPU
Step '1/6 GPU'
if (Have 'nvidia-smi') {
  nvidia-smi --query-gpu=name,memory.total,memory.used,driver_version --format=csv
} else { Warn 'nvidia-smi not found: is the NVIDIA driver installed?' }

# ---------------------------------------------------------------- 2. Planner venv (Python 3.12, CUDA torch)
Step '2/6 Planner environment (.venv-planner, Python 3.12, CUDA torch; several minutes the first time)'
$Py12 = NeedPy '3.12' 'Python.Python.3.12'
if (-not $Py12) { Fail 'Python 3.12 was not found. Install it from python.org (64-bit), then run again.' }
$PVenv = Join-Path $Root '.venv-planner'
$PPy = Join-Path $PVenv 'Scripts\python.exe'
if (-not (Test-Path $PPy)) { & $Py12 -m venv $PVenv }
& $PPy -m pip install --upgrade pip -q
& $PPy -m pip install torch==2.11.0 torchvision==0.26.0 --index-url https://download.pytorch.org/whl/cu128 -q
& $PPy -m pip install -r (Join-Path $Root 'requirements-planner.txt') -q
& $PPy -c "import torch, transformers; print('torch', torch.__version__, 'cuda', torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '-'); print('transformers', transformers.__version__)"
if ($LASTEXITCODE -ne 0) { Fail 'torch / transformers did not import in .venv-planner.' }

# ---------------------------------------------------------------- 3. Evaluation venv (Python 3.10, mlagents)
Step '3/6 Evaluation environment (.venv, Python 3.10, mlagents)'
$Py10 = NeedPy '3.10' 'Python.Python.3.10'
if (-not $Py10) { Fail 'Python 3.10 was not found (mlagents 1.1.0 needs 3.10). Install 3.10.11 from python.org, then run again.' }
$EVenv = Join-Path $Root '.venv'
$EPy = Join-Path $EVenv 'Scripts\python.exe'
if (-not (Test-Path $EPy)) { & $Py10 -m venv $EVenv }
& $EPy -m pip install --upgrade pip -q
& $EPy -m pip install -r (Join-Path $Root 'requirements.txt') -q
& $EPy -m pytest -q (Join-Path $Root 'python\tests')
if ($LASTEXITCODE -ne 0) { Warn 'Some Python tests failed (see above).' }

# ---------------------------------------------------------------- 4. Model weights
$ModelDir = Join-Path $Root ('models\' + ($Model -split '/')[-1])
Step "4/6 Model $Model -> $ModelDir"
& $PPy -c "from huggingface_hub import snapshot_download; snapshot_download('$Model', local_dir=r'$ModelDir')"
if ($LASTEXITCODE -ne 0) { Fail 'Model download failed (network, or disk space: about 9 GB for 4B).' }
Ok ('model files: ' + ((Get-ChildItem $ModelDir -Recurse | Measure-Object Length -Sum).Sum / 1GB).ToString('0.0') + ' GB')

# ---------------------------------------------------------------- 5. Self-test on the GPU
Step '5/6 Self-test (load the model, answer a planner request with and without images)'
$st = @((Join-Path $Root 'python\planner_server\selftest.py'), '--model', $ModelDir, '--out', (Join-Path $LogDir 'selftest.txt'))
if ($Images) { $st += @('--images', $Images) }
& $PPy @st
$selfOk = ($LASTEXITCODE -eq 0)

# ---------------------------------------------------------------- 6. Summary
Step '6/6 Summary'
if (Have 'nvidia-smi') { nvidia-smi --query-gpu=memory.used,memory.total --format=csv }
if ($selfOk) { Ok 'VLM setup finished. Next: 5_vlm_server.bat mock, then 6_vlm_eval_editor.bat (see README).' }
else { Warn 'Self-test failed: see logs_vlm\selftest.txt.' }
Stop-Transcript | Out-Null
