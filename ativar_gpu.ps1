# Troca o PyTorch de processador pelo de placa NVIDIA.
# GTX 10xx (Pascal) so funciona ate o PyTorch 2.14 com CUDA 12.6, entao a versao fica fixa.
$ErrorActionPreference = 'Continue'
$aqui = $PSScriptRoot
Start-Transcript -Path (Join-Path $aqui 'ativar_gpu.log') -Force | Out-Null
$py = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'

Write-Host "=== Driver NVIDIA ===" -ForegroundColor Cyan
Get-CimInstance Win32_VideoController | ForEach-Object { Write-Host "Placa: $($_.Name)  driver $($_.DriverVersion)" }
$nv = Get-CimInstance Win32_VideoController | Where-Object { $_.Name -like '*NVIDIA*' } | Select-Object -First 1
if (-not $nv) { Write-Host "Nenhuma placa NVIDIA encontrada." -ForegroundColor Yellow; Stop-Transcript | Out-Null; exit 1 }
# 30.0.14.7219 -> 472.19
$d = ($nv.DriverVersion -replace '\.', '')
$versao = [double]($d.Substring($d.Length - 5, 3) + '.' + $d.Substring($d.Length - 2))
Write-Host "Driver NVIDIA: $versao"
if ($versao -lt 528) {
    Write-Host ""
    Write-Host "O driver da NVIDIA ($versao) e antigo demais; precisa ser 528 ou mais novo." -ForegroundColor Yellow
    Write-Host "Atualize em https://www.nvidia.com/pt-br/drivers/ (escolha GeForce GTX 1050), reinicie o PC e rode este arquivo de novo."
    Stop-Transcript | Out-Null
    exit 1
}
$env:PATH = "$env:PATH;C:\Program Files\NVIDIA Corporation\NVSMI"
nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader

Write-Host "=== PyTorch para NVIDIA (2.14 + CUDA 12.6) ===" -ForegroundColor Cyan
& $py -m pip uninstall -y torch
& $py -m pip install torch==2.14.0 --index-url https://download.pytorch.org/whl/cu126

Write-Host "=== Teste da placa ===" -ForegroundColor Cyan
& $py -c "import torch; ok=torch.cuda.is_available(); print('CUDA:', ok); print('GPU:', torch.cuda.get_device_name(0) if ok else '-'); x=torch.ones(1000,1000,device='cuda'); print('conta na placa OK', float((x@x).sum()))"

Write-Host "=== Recompilando Transcritor.exe ===" -ForegroundColor Cyan
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $aqui 'src\build.ps1')

Write-Host "GPU_ATIVADA"
Stop-Transcript | Out-Null
