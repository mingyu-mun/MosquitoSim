# Waits for the stage 0 run to finish, checks its result, then trains stage 1 from it.
#   -Seed N     seed number (default 1)
#   -Gate X     minimum mean reward over the last 10 summaries of stage 0 (default 0.7)
# Design values (rewards, curriculum, configs) are not touched here.
param([int]$Seed = 1, [double]$Gate = 0.7)
$ErrorActionPreference = 'Continue'
$Root  = 'C:\MosquitoSim'
$Learn = Join-Path $Root '.venv\Scripts\mlagents-learn.exe'
$Build = Join-Path $Root 'builds\Main\MosquitoSim.exe'
$LogDir = Join-Path $Root 'logs_setup'
$StatusFile = Join-Path $LogDir 'train_status.txt'
function Note($m) {
  $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m
  Add-Content -Path $StatusFile -Value $line -Encoding ascii
  Write-Host $line -ForegroundColor Cyan
}
function Stop-Here($m) { Note "STAGE1 NOT STARTED: $m"; exit 1 }

Set-Location $Root
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
Note "stage1 runner started (seed $Seed, gate $Gate, pid $PID); waiting for stage 0 to finish"

# 1. wait until no trainer is running (at most 90 minutes)
$deadline = (Get-Date).AddMinutes(90)
while (Get-Process -Name 'mlagents-learn' -ErrorAction SilentlyContinue) {
  if ((Get-Date) -gt $deadline) { Stop-Here 'stage 0 trainer still running after 90 minutes' }
  Start-Sleep -Seconds 20
}
Start-Sleep -Seconds 10

# 2. stage 0 must have ended with a final model and a good enough reward
$s0 = "stage0_s$Seed"
$model = Join-Path $Root "results\$s0\Mosquito.onnx"
if (-not (Test-Path $model)) { Stop-Here "results\$s0\Mosquito.onnx not found (stage 0 did not finish normally)" }
$s0log = Join-Path $LogDir "train_$s0.txt"
$vals = @()
foreach ($l in (Get-Content $s0log)) {
  if ($l -match 'PyTorch: ') { $vals = @() }   # keep only the last run in the log
  if ($l -match 'Mean Reward: (-?[\d.]+)\. Std') { $vals += [double]$Matches[1] }
}
if ($vals.Count -lt 10) { Stop-Here "only $($vals.Count) reward summaries found in $s0log" }
$last = $vals[($vals.Count - 10)..($vals.Count - 1)]
$mean = ($last | Measure-Object -Average).Average
Note ("stage 0 last-10 mean reward = {0:F3} (gate {1})" -f $mean, $Gate)
if ($mean -lt $Gate) { Stop-Here 'stage 0 reward below the gate' }

# 3. stage 1, continuing from the stage 0 policy
$RunId = "stage1_s$Seed"
$extra = ''
if (Test-Path (Join-Path $Root "results\$RunId")) { $extra = '--force' }
$port = 5005 + 10 * $Seed
$trainLog = Join-Path $LogDir "train_$RunId.txt"
$argLine = "configs\stage1.yaml --run-id $RunId --seed $Seed --env $Build --no-graphics --time-scale 20 --num-envs 4 --base-port $port --torch-device cpu --initialize-from $s0 $extra"
Note "training started: $RunId (initialize-from $s0) $extra"
cmd /c "$Learn $argLine 2>&1" | ForEach-Object { $_; Add-Content -Path $trainLog -Value $_ -Encoding utf8 }
Note "training process ended: $RunId (exit $LASTEXITCODE)"
