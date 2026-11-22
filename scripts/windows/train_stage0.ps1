# Smoke test + stage 0 PPO training against the Windows build.
#   -Seed N          seed number (default 1)
#   -Mode ask        ask what to do when results already exist (default)
#   -Mode resume     continue the existing run without asking
#   -Mode force      overwrite the existing run without asking
#   -SkipSmoke       skip the random-policy smoke test
param([int]$Seed = 1, [string]$Mode = 'ask', [switch]$SkipSmoke)
$ErrorActionPreference = 'Continue'
$Root  = 'C:\MosquitoSim'
$VPy   = Join-Path $Root '.venv\Scripts\python.exe'
$Learn = Join-Path $Root '.venv\Scripts\mlagents-learn.exe'
$Build = Join-Path $Root 'builds\Main\MosquitoSim.exe'
$LogDir = Join-Path $Root 'logs_setup'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$StatusFile = Join-Path $LogDir 'train_status.txt'

function Note($m) {
  $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m
  Add-Content -Path $StatusFile -Value $line -Encoding ascii
  Write-Host $line -ForegroundColor Cyan
}
function Fail($m) { Note "STOPPED: $m"; Write-Host "STOPPED: $m" -ForegroundColor Red; exit 1 }

Note "script started (seed $Seed, mode $Mode, pid $PID)"
if (-not (Test-Path $VPy))   { Fail 'Python environment not found. Run 1_setup.bat first.' }
if (-not (Test-Path $Build)) { Fail "Build not found at $Build. Build the player in Unity first." }
Set-Location $Root
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'

# Keep the environment in step with requirements.txt (quick when nothing changed).
Note 'pip install -r requirements.txt'
$pipLog = Join-Path $LogDir 'pip_install.txt'
cmd /c "$VPy -m pip install -r requirements.txt > $pipLog 2>&1"
if ($LASTEXITCODE -ne 0) { Fail 'pip install failed. See logs_setup\pip_install.txt' }
$tv = & $VPy -c "import torch; print(torch.__version__)"
Note "torch $tv"

if (-not $SkipSmoke) {
  Note 'smoke test started (random policy, 5000 steps)'
  $smokeLog = Join-Path $LogDir 'smoke_test.txt'
  cmd /c "$VPy python\training\smoke_test.py --env $Build --steps 5000 > $smokeLog 2>&1"
  $smokeExit = $LASTEXITCODE
  Get-Content $smokeLog | Select-Object -Last 15
  if ($smokeExit -ne 0) { Fail "smoke test failed (exit $smokeExit). See logs_setup\smoke_test.txt" }
  Note 'smoke test OK'
}

$RunId = "stage0_s$Seed"
$Extra = @()
if (Test-Path (Join-Path $Root "results\$RunId")) {
  if ($Mode -eq 'ask') {
    $a = Read-Host "results\$RunId already exists. [R]esume it, [O]verwrite it, or [Q]uit? (R/o/q)"
    if ($a -match '^[Qq]') { exit 0 } elseif ($a -match '^[Oo]') { $Mode = 'force' } else { $Mode = 'resume' }
  }
  if ($Mode -eq 'force') { $Extra += '--force' } else { $Extra += '--resume' }
}

Note "training started: $RunId $Extra"
Write-Host '   Ctrl+C stops and saves a checkpoint. Progress: 3_tensorboard.bat'
$trainLog = Join-Path $LogDir "train_$RunId.txt"
$port = 5005 + 10 * $Seed
$argLine = "configs\stage0.yaml --run-id $RunId --seed $Seed --env $Build --no-graphics --time-scale 20 --num-envs 4 --base-port $port --torch-device cpu $($Extra -join ' ')"
# Console shows the live output; the same text is appended to the log through PowerShell's transcript-free tee.
cmd /c "$Learn $argLine 2>&1" | ForEach-Object { $_; Add-Content -Path $trainLog -Value $_ -Encoding utf8 }
Note "training process ended (exit $LASTEXITCODE)"
