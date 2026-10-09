# Transcritor

Transcritor de áudio local (Whisper turbo na GPU) e download de vídeo do YouTube, TikTok e Instagram em MP3 (qualquer site do yt-dlp).

**Para que serve:** transformar em texto uma gravação, uma reunião ou um vídeo do YouTube/TikTok/Instagram, no seu PC,
sem enviar o áudio para a internet (só o download do próprio vídeo usa a rede).

**Como funciona:** a janela (`Transcritor.exe`) recebe arquivos de áudio/vídeo ou um link; para links,
o `yt-dlp` baixa o áudio em MP3; o `src/worker.py` roda o Whisper na placa de vídeo e grava o `.txt`
ao lado do áudio. A skill `transcrever-youtube` permite pedir isso ao Claude Code só com o link.

- `Transcritor.exe` — janela para transcrever áudio (usa `src/worker.py`). Aceita arquivos ou um link do YouTube: cole o link e clique em "Baixar e transcrever" (MP3 e .txt ficam em `videos/<título>/` ao lado do exe).
- `youtube.py` — baixa o áudio de links do YouTube em MP3 (yt-dlp) e transcreve.
- `src/` — código-fonte da janela (`Transcritor.cs`, `build.ps1`) e o `worker.py` do Whisper.
- `skills/transcrever-youtube/` — skill do Claude Code que roda `youtube.py` a partir de um link.

## Requisitos

Python com `openai-whisper`, `torch` e `yt-dlp[default,curl-cffi]` (o `curl-cffi` é necessário para o TikTok); ffmpeg no PATH. Dê dois cliques em `INSTALAR.bat` uma vez: ele instala tudo (PyTorch com CUDA se houver placa NVIDIA, senão versão para processador), baixa o modelo turbo e compila o `Transcritor.exe`.

## Uso

```
python youtube.py "URL" [URL2 ...] [--idioma pt] [--pasta nome]
```

Para Instagram (e vídeos que pedem login), salve um `cookies.txt` do navegador na pasta do programa; ele é usado automaticamente e nunca vai para o git.

Saída em `videos/<site>/<título>/` (ou `videos/<pasta>/` com `--pasta`) com o `.mp3` e o `.txt` lado a lado. Para usar a skill, copie
`skills/transcrever-youtube` para `~/.claude/skills/`.


