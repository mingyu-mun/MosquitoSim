# MosquitoSim setup for Windows (stage 0 training on this PC).
# Run through 1_setup.bat. Safe to run again: finished steps are skipped.
$ErrorActionPreference = 'Continue'
$Root   = 'C:\MosquitoSim'          # ASCII-only path on purpose (user folder has non-ASCII characters)
$Repo   = 'https://github.com/mingyu-mun/MosquitoSim.git'
$Branch = 'stage0-scaffold'
$UnityVersion   = '6000.3.29f1'
$UnityChangeset = '96d2fe17529c'

function Have($cmd) { return [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }
function Step($m)   { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Warn($m)   { Write-Host $m -ForegroundColor Yellow }
function Ok($m)     { Write-Host $m -ForegroundColor Green }
function Fail($m)   { Write-Host ''; Write-Host "STOPPED: $m" -ForegroundColor Red; exit 1 }
function RefreshPath {
  $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}
function Ask($question) {
  $a = Read-Host "$question [Y/n]"
  return ($a -eq '' -or $a -match '^[Yy]')
}
function WingetInstall($id) {
  if (-not (Have 'winget')) { Warn "winget is not available, so $id cannot be installed automatically."; return }
  winget install --id $id -e --source winget --accept-package-agreements --accept-source-agreements
  RefreshPath
}
function FindPy310 {
  if (Have 'py') {
    $exe = & py -3.10 -c "import sys; print(sys.executable)" 2>$null
    if ($LASTEXITCODE -eq 0 -and $exe) { return ($exe | Select-Object -First 1).Trim() }
  }
  $cands = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python310\python.exe'),
    'C:\Program Files\Python310\python.exe',
    'C:\Python310\python.exe'
  )
  foreach ($c in $cands) { if (Test-Path $c) { return $c } }
  return $null
}

# ---------------------------------------------------------------- 1. Git
Step '1/6 Git'
if (-not (Have 'git')) {
  if (Ask 'Git is not installed. Install it with winget?') { WingetInstall 'Git.Git' }
}
if (-not (Have 'git')) { Fail 'Git is not available. Install Git for Windows (https://git-scm.com), then run 1_setup.bat again.' }
Ok (git --version)

# ---------------------------------------------------------------- 2. Python 3.10
# mlagents 1.1.0 needs Python 3.10.1 - 3.10.12. The last Windows installer of 3.10 is 3.10.11.
Step '2/6 Python 3.10'
$Py = FindPy310
if (-not $Py) {
  if (Ask 'Python 3.10 is not installed. Install Python 3.10.11 with winget?') { WingetInstall 'Python.Python.3.10' }
  $Py = FindPy310
}
if (-not $Py) { Fail 'Python 3.10 was not found. Install Python 3.10.11 from python.org (Windows installer 64-bit), then run again.' }
Ok ("Using " + $Py + "  (" + (& $Py --version) + ")")

# ---------------------------------------------------------------- 3. Repository
Step "3/6 Repository -> $Root (branch $Branch)"
if (Test-Path (Join-Path $Root '.git')) {
  git -C $Root fetch origin
  git -C $Root checkout $Branch
  git -C $Root pull --ff-only
  if ($LASTEXITCODE -ne 0) { Warn 'git pull did not fast-forward (local changes?). Continuing with the files that are there.' }
} elseif (Test-Path $Root) {
  Fail "$Root exists but is not a git clone. Rename or remove it, then run again."
} else {
  git clone -b $Branch $Repo $Root
  if ($LASTEXITCODE -ne 0) { Fail 'git clone failed. Check the network, or whether the repository is private (then sign in to GitHub in the window that opens).' }
}
Ok ('HEAD: ' + (git -C $Root log -1 --format='%h %ad %s' --date=short))

# ---------------------------------------------------------------- 4. Python environment
Step '4/6 Python virtual environment and packages (several minutes on the first run)'
$Venv = Join-Path $Root '.venv'
$VPy  = Join-Path $Venv 'Scripts\python.exe'
if (-not (Test-Path $VPy)) {
  & $Py -m venv $Venv
  if (-not (Test-Path $VPy)) { Fail 'Could not create the virtual environment.' }
}
& $VPy -m pip install --upgrade pip 'setuptools<82' wheel
& $VPy -m pip install -r (Join-Path $Root 'requirements.txt')
if ($LASTEXITCODE -ne 0) { Fail 'pip install failed. Copy the error text above and send it to Claude.' }
Ok 'Packages installed.'
& $VPy -c "import torch; print('torch', torch.__version__, '| CUDA available:', torch.cuda.is_available())"
Write-Host 'Note: CUDA False is fine. The policy is a small MLP and trains on the CPU.'

# ---------------------------------------------------------------- 5. Tests
Step '5/6 Python tests'
Push-Location $Root
& $VPy -m pytest 'python\tests' -q
$testExit = $LASTEXITCODE
Pop-Location
if ($testExit -ne 0) { Warn 'Some tests failed. Copy the output above and send it to Claude. Unity steps below can still be done.' } else { Ok 'All Python tests passed.' }

# ---------------------------------------------------------------- 6. Unity
Step '6/6 Unity'
$HubExe    = 'C:\Program Files\Unity Hub\Unity Hub.exe'
$EditorExe = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
if (-not (Test-Path $HubExe)) {
  if (Ask 'Unity Hub is not installed. Install it with winget?') { WingetInstall 'Unity.UnityHub' }
}
if (Test-Path $EditorExe) {
  Ok "Unity $UnityVersion is installed."
} else {
  Warn "Unity $UnityVersion is not installed in the default location."
  if ((Test-Path $HubExe) -and (Ask "Open Unity Hub to install Unity $UnityVersion now?")) {
    Start-Process "unityhub://$UnityVersion/$UnityChangeset"
    Write-Host 'In the Hub dialog keep "Windows Build Support (IL2CPP)" optional; the default Windows (Mono) build support is enough.'
  }
}

Write-Host ''
Write-Host '------------------------------------------------------------' -ForegroundColor Cyan
Write-Host 'Setup part is done. Next (by hand, in Unity):' -ForegroundColor Cyan
Write-Host "  1. Unity Hub > Add > Add project from disk > $Root\unity   (open with $UnityVersion)"
Write-Host '  2. Wait for the first import. Check the Console has no red compile errors.'
Write-Host '  3. Menu: MosquitoSim > Build Main Scene'
Write-Host '  4. Press Play once to see the rooms (V = view, H = HUD). Stop Play.'
Write-Host '  5. File > Build Profiles > Windows. Add Assets/Scenes/Main.unity to the Scene List.'
Write-Host "     Build into the folder  $Root\builds\Main   with the name  MosquitoSim.exe"
Write-Host '  6. Run 2_train_stage0.bat'
Write-Host 'Details in Korean: README_KO.txt in this folder.'
Write-Host '------------------------------------------------------------' -ForegroundColor Cyan
