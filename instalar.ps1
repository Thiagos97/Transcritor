# Instala tudo o que o Transcritor precisa neste PC e compila o Transcritor.exe.
# Pode rodar de novo sem problema: pula o que ja estiver instalado.
$ErrorActionPreference = 'Continue'
$aqui = $PSScriptRoot
$log = Join-Path $aqui 'instalacao.log'
Start-Transcript -Path $log -Force | Out-Null

function Passo($t) { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan }

Passo "Placa de video"
$temNvidia = $false
try { nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader; if ($LASTEXITCODE -eq 0) { $temNvidia = $true } } catch { }
Get-CimInstance Win32_VideoController | ForEach-Object { Write-Host "Placa: $($_.Name)" }
if ($temNvidia) { Write-Host "NVIDIA encontrada: vai usar a GPU (rapido)." } else { Write-Host "Sem NVIDIA: vai rodar no processador (funciona, mas mais devagar)." -ForegroundColor Yellow }

Passo "winget"
$winget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $winget) { Write-Host "ERRO: winget nao encontrado. Instale o 'Instalador de Aplicativo' pela Microsoft Store."; Stop-Transcript; exit 1 }
winget --version

Passo "Python 3.12"
$py = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
if (-not (Test-Path $py)) {
    winget install --id Python.Python.3.12 -e --scope user --silent --accept-source-agreements --accept-package-agreements
}
if (-not (Test-Path $py)) { Write-Host "ERRO: Python 3.12 nao ficou em $py"; Stop-Transcript; exit 1 }
& $py --version
[Environment]::SetEnvironmentVariable('TRANSCRITOR_PYTHON', $py, 'User')
Write-Host "TRANSCRITOR_PYTHON = $py"

Passo "ffmpeg"
$links = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links'
$env:PATH = "$links;$env:PATH"
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    winget install --id Gyan.FFmpeg -e --silent --accept-source-agreements --accept-package-agreements
}
$env:PATH = "$links;$env:PATH"
if (Get-Command ffmpeg -ErrorAction SilentlyContinue) { Write-Host "ffmpeg OK" } else { Write-Host "AVISO: ffmpeg nao achado no PATH ainda" }

Passo "Bibliotecas Python (PyTorch, Whisper, yt-dlp)"
& $py -m pip install --upgrade pip
if ($temNvidia) { & $py -m pip install torch --index-url https://download.pytorch.org/whl/cu128 }
else { & $py -m pip install torch --index-url https://download.pytorch.org/whl/cpu }
& $py -m pip install -U openai-whisper "yt-dlp[default,curl-cffi]"

Passo "Teste da GPU"
& $py -c "import torch; print('torch', torch.__version__); print('CUDA disponivel:', torch.cuda.is_available()); print('GPU:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else '-')"

Passo "Baixando o modelo turbo do Whisper (~1,5 GB, so na primeira vez)"
& $py -c "import whisper; whisper.load_model('turbo', device='cpu'); print('modelo turbo OK')"

Passo "Compilando Transcritor.exe a partir do codigo"
$exe = Join-Path $aqui 'Transcritor.exe'
if (Test-Path $exe) { Remove-Item $exe -Force }
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $aqui 'src\build.ps1')
if (Test-Path $exe) { Write-Host "Transcritor.exe OK" } else { Write-Host "ERRO: compilacao falhou" }

Passo "Atalho na area de trabalho"
try {
    $desk = [Environment]::GetFolderPath('Desktop')
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut((Join-Path $desk 'Transcritor.lnk'))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $aqui
    $lnk.Save()
    Write-Host "Atalho criado em $desk"
} catch { Write-Host "AVISO: nao consegui criar o atalho: $_" }

Passo "FIM"
Write-Host "INSTALACAO_CONCLUIDA"
Stop-Transcript | Out-Null
