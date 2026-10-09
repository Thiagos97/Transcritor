# Transcritor

Transforma áudio em texto no seu PC, com o Whisper (modelo "turbo") rodando na placa de vídeo. Aceita arquivos de áudio ou vídeo e links do YouTube, TikTok, Instagram (e outros sites que o yt-dlp aceita). Nada é enviado para a internet, a não ser o download do próprio vídeo.

## Como abrir

Atalho **Transcritor** na pasta **IA** da área de trabalho (ou `Transcritor.exe`, nesta pasta).

## Como usar

**Arquivos do PC:**
1. Clique em **Escolher áudios…** ou **arraste os arquivos** para a janela. Dá para colocar vários de uma vez.
2. Escolha o **Idioma**: Português, Detectar sozinho, Inglês ou Espanhol. Fixar o idioma melhora a pontuação.
3. Clique em **Transcrever**. A barra mostra o progresso, e o texto aparece embaixo.
4. O `.txt` é salvo **ao lado de cada áudio**, com o mesmo nome.

**Vídeo do YouTube, TikTok ou Instagram:**
1. Cole o link na caixa do topo.
2. Aperte **Enter** ou clique em **Baixar e transcrever**.
3. O áudio (MP3) e o texto vão para `videos\<site>\<título do vídeo>\`, nesta pasta (ex.: `videos\TikTok\...`).

**Instagram (e vídeos que pedem login):** o Instagram quase sempre exige estar logado. Exporte os cookies do seu navegador com a extensão **Get cookies.txt LOCALLY** (Chrome/Edge/Firefox), estando logado no Instagram, e salve como `cookies.txt` nesta pasta, ao lado do `Transcritor.exe`. O arquivo é o seu login: não compartilhe e não suba para o GitHub (o `.gitignore` já o ignora). Se parar de funcionar, exporte de novo.

**Depois de transcrever:**
- **Copiar texto:** copia a transcrição.
- **Abrir pasta do .txt:** abre a pasta onde o arquivo foi salvo.
- **Cancelar:** para no meio.

## Atalhos

| Tecla | Onde | O que faz |
|---|---|---|
| **Enter** | caixa do link | Baixar e transcrever o vídeo |
| **Delete** | lista de arquivos | Tirar os arquivos selecionados da lista |

## Como funciona

A janela (`Transcritor.exe`, feita em C#) chama um programa em Python embutido nela (`src\worker.py`), que usa o Whisper com a placa de vídeo. O tempo de transcrição fica bem abaixo da duração do áudio: um vídeo de 35 minutos fica pronto em cerca de 2 minutos.

Para instalar tudo, dê dois cliques em `INSTALAR.bat` (uma vez só). O log fica em `instalacao.log`.

## Pela linha de comando ou pelo Claude

`youtube.py` faz o mesmo sem janela, e é o que a skill **transcrever-youtube** do Claude usa (inclusive pedindo pelo celular):

```bash
python "C:\Users\Usuario\Documents\Transcritor\youtube.py" "https://www.youtube.com/watch?v=..." --idioma pt
```

`--pasta nome` junta vários vídeos de um assunto na mesma pasta.

## Problemas comuns

- **"yt-dlp falhou" (403, "Sign in to confirm"):** o YouTube mudou. Atualize com `python -m pip install -U yt-dlp` e tente de novo. Vídeo privado, com restrição de idade ou só para membros não dá.
- **Falta de memória na placa:** o ComfyUI ou a Central de Voz estão usando a placa. Espere terminar ou feche-os.
- **Texto sem pontuação ou em outro idioma:** escolha o idioma em vez de "Detectar sozinho".

## Para mexer no programa

`src\build.ps1` recompila o `Transcritor.exe` a partir de `src\Transcritor.cs`.
