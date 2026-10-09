---
name: transcrever-youtube
description: Baixar o áudio (MP3) de vídeo do YouTube, TikTok ou Instagram e transcrever com o Whisper local deste PC (Transcritor em E:\Programas desenvolvidos\Transcritor, GPU). Usar quando o usuário mandar link do YouTube, TikTok ou Instagram pedindo transcrição, resumo, "o que ele fala", passo a passo ou anotações do vídeo, inclusive de longe pelo celular.
---

# YouTube / TikTok / Instagram → MP3 → transcrição no PC

Roda tudo local: yt-dlp baixa só o áudio, e o `worker.py` do Transcritor
(o mesmo do `E:\Programas desenvolvidos\Transcritor\Transcritor.exe`) transcreve com o Whisper turbo na RTX 4070 Ti.
O turbo é o único modelo baixado e em uso; não passe `--modelo`.

## Passo a passo

1. **Rode o script** (Bash, timeout 600000; se o vídeo tiver mais de ~40 min,
   rode com `run_in_background` e avise o usuário que está processando):

   ```bash
   PYTHONIOENCODING=utf-8 /c/Python314/python.exe "E:/Programas desenvolvidos/Transcritor/youtube.py" "URL" [URL2 ...] [opções]
   ```

   A última linha do stdout é um JSON:
   `{"ok": true, "itens": [{"url", "titulo", "mp3", "txt"}], "segundos": ...}`.
   Item com problema traz `"erro"` no lugar de `"txt"`. O progresso vai para stderr.

2. **Leia o `.txt`** de cada item e entregue o que o usuário pediu:
   - só "transcreve": mande o `.txt` com `SendUserFile` (`display: "attach"`)
     e diga em uma linha do que o vídeo trata.
   - resumo, passo a passo, "o que ele fala sobre X": responda no chat, em
     português, mesmo que o vídeo seja em outro idioma. Cite trechos curtos
     quando ajudar.
   - se ele quiser o texto traduzido inteiro, traduza e mande como arquivo.

3. Diga onde ficaram os arquivos (pasta em `E:\Programas desenvolvidos\Transcritor\videos\`).

## Opções

| opção | padrão | quando mudar |
|---|---|---|
| `--idioma pt\|en\|es...` | auto | se souber o idioma, fixar melhora a pontuação (o worker dá um texto inicial pontuado em pt/en/es) |
| `--pasta nome` | título do vídeo | para juntar vários vídeos de um assunto na mesma pasta (ex.: `qwen-image-2.1`) |

Vários links na mesma chamada: baixa todos e transcreve em sequência carregando o modelo uma vez só.

Tempo: com o turbo na GPU, a transcrição leva bem menos que a duração do vídeo
(vídeo de 20 min fica pronto em 1 a 2 min). O download depende da internet.

## Problemas comuns

- **Instagram pedindo login** ("login required", "cookies"): precisa de um `cookies.txt`
  exportado do navegador (extensão Get cookies.txt LOCALLY) na pasta do Transcritor.
  Peça para o usuário exportar; nunca peça a senha dele.

- **"yt-dlp falhou"** (HTTP 403, "Sign in to confirm", "Requested format is not
  available"): o YouTube muda com frequência. Atualize e tente de novo uma vez:
  `/c/Python314/python.exe -m pip install -U yt-dlp`. Se o vídeo for privado,
  com restrição de idade ou só para membros, diga ao usuário que não dá sem login.
- **"ffmpeg não encontrado"**: está instalado via winget em
  `%LOCALAPPDATA%\Microsoft\WinGet\Links`; o worker já procura lá.
- **Transcrição sem pontuação ou em outro idioma**: rode de novo com `--idioma` fixo.
- **Placa de vídeo ocupada** (ComfyUI gerando imagem ao mesmo tempo): pode dar
  falta de memória. Espere a imagem terminar e rode de novo.

## Onde fica cada coisa

- Script: `E:\Programas desenvolvidos\Transcritor\youtube.py`
- Transcritor (janela e worker): `E:\Programas desenvolvidos\Transcritor\Transcritor.exe`, `E:\Programas desenvolvidos\Transcritor\src\worker.py`
- Saída: `E:\Programas desenvolvidos\Transcritor\videos\<pasta>\<título>.mp3` e `.txt` ao lado
- Modelos do Whisper: `%USERPROFILE%\.cache\whisper`
