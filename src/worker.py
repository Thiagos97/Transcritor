"""Transcreve audios com o Whisper e fala com a janela do Transcritor pelo stdout.

Cada linha do protocolo comeca com "@@" e tem campos separados por TAB:
  @@STATUS  msg
  @@DEVICE  descricao
  @@FILE    indice  total  caminho  duracao_s
  @@SEG     inicio_s  fim_s  texto
  @@FILEDONE indice  caminho_txt
  @@FILEERR indice  mensagem
  @@ALLDONE segundos
  @@FATAL   mensagem
"""

import argparse
import os
import re
import shutil
import sys
import time
import traceback

REAL_STDOUT = sys.stdout
REAL_STDOUT.reconfigure(encoding="utf-8", line_buffering=True)

# Quando o idioma e fixo, um texto inicial pontuado faz o Whisper pontuar;
# sem isso ele as vezes devolve tudo em minusculas e sem virgula.
PROMPTS = {
    "pt": "Olá, tudo bem? Segue o áudio, com a explicação.",
    "es": "Hola, ¿qué tal? Aquí va el audio, con la explicación.",
    "en": "Hello, how are you? Here is the audio, with the explanation.",
}


def out(*fields):
    clean = [str(f).replace("\t", " ").replace("\r", " ").replace("\n", " ") for f in fields]
    REAL_STDOUT.write("@@" + "\t".join(clean) + "\n")
    REAL_STDOUT.flush()


TIMESTAMP_LINE = re.compile(r"^\[((?:\d+:)?\d+:\d+\.\d+) --> ((?:\d+:)?\d+:\d+\.\d+)\]\s?(.*)$")


def to_seconds(stamp):
    seconds = 0.0
    for part in stamp.split(":"):
        seconds = seconds * 60 + float(part)
    return seconds


class SegmentTap:
    """Captura o que o whisper imprime com verbose=True e repassa como @@SEG.

    O whisper nao tem callback por segmento; ele so imprime cada um assim
    que decodifica. Interceptar o print e o que deixa o texto aparecer ao vivo.
    """

    def __init__(self):
        self.buffer = ""

    def write(self, text):
        self.buffer += text
        while "\n" in self.buffer:
            line, self.buffer = self.buffer.split("\n", 1)
            self.handle(line)
        return len(text)

    def handle(self, line):
        match = TIMESTAMP_LINE.match(line.strip())
        if match:
            out("SEG", "%.2f" % to_seconds(match.group(1)), "%.2f" % to_seconds(match.group(2)), match.group(3).strip())
        elif line.strip():
            out("STATUS", line.strip())

    def flush(self):
        pass


def ensure_ffmpeg():
    if shutil.which("ffmpeg"):
        return True
    links = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "WinGet", "Links")
    os.environ["PATH"] = links + os.pathsep + os.environ.get("PATH", "")
    return shutil.which("ffmpeg") is not None


def model_is_cached(whisper, name):
    url = whisper._MODELS.get(name)
    if not url:
        return True
    cache = os.path.join(os.getenv("XDG_CACHE_HOME", os.path.join(os.path.expanduser("~"), ".cache")), "whisper")
    return os.path.exists(os.path.join(cache, os.path.basename(url)))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default="turbo")
    parser.add_argument("--language", default="pt", help="codigo do idioma, ou 'auto'")
    parser.add_argument("--list", required=True, help="arquivo UTF-8 com um caminho de audio por linha")
    args = parser.parse_args()

    with open(args.list, encoding="utf-8-sig") as f:
        files = [line.strip() for line in f if line.strip()]
    if not files:
        out("FATAL", "Nenhum arquivo para transcrever.")
        return 2

    if not ensure_ffmpeg():
        out("FATAL", "ffmpeg não encontrado. Instale com: winget install Gyan.FFmpeg")
        return 2

    out("STATUS", "Carregando o Whisper…")
    try:
        import torch
        import whisper
    except ImportError as e:
        out("FATAL", "Não consegui importar o Whisper/PyTorch neste Python (%s): %s" % (sys.executable, e))
        return 2

    if torch.cuda.is_available():
        device = "cuda"
        out("DEVICE", "GPU " + torch.cuda.get_device_name(0))
    else:
        device = "cpu"
        out("DEVICE", "CPU (PyTorch sem CUDA — vai demorar)")

    if not model_is_cached(whisper, args.model):
        out("STATUS", "Baixando o modelo %s (só na primeira vez, pode levar alguns minutos)…" % args.model)
    else:
        out("STATUS", "Carregando o modelo %s…" % args.model)
    try:
        model = whisper.load_model(args.model, device=device)
    except RuntimeError as e:
        if device != "cuda":
            raise
        # placas com pouca memoria (ex.: GTX 1050 Ti, 4 GB): volta para o processador
        out("STATUS", "Pouca memória na placa (%s); usando o processador." % str(e).splitlines()[0][:80])
        torch.cuda.empty_cache()
        device = "cpu"
        out("DEVICE", "CPU (a placa não coube o modelo)")
        model = whisper.load_model(args.model, device=device)

    language = None if args.language == "auto" else args.language
    prompt = PROMPTS.get(language)

    started = time.time()
    total = len(files)
    for index, path in enumerate(files):
        try:
            audio = whisper.load_audio(path)
            duration = len(audio) / whisper.audio.SAMPLE_RATE
            out("FILE", index, total, path, "%.2f" % duration)

            def rodar():
                sys.stdout = SegmentTap()
                try:
                    return model.transcribe(
                        audio,
                        language=language,
                        fp16=(device == "cuda"),
                        verbose=True,
                        initial_prompt=prompt,
                    )
                finally:
                    sys.stdout = REAL_STDOUT

            try:
                result = rodar()
            except torch.cuda.OutOfMemoryError:
                out("STATUS", "Faltou memória na placa; continuando no processador.")
                del model
                torch.cuda.empty_cache()
                device = "cpu"
                out("DEVICE", "CPU (a placa ficou sem memória)")
                model = whisper.load_model(args.model, device=device)
                result = rodar()

            txt_path = os.path.splitext(path)[0] + ".txt"
            with open(txt_path, "w", encoding="utf-8") as f:
                for segment in result["segments"]:
                    f.write(segment["text"].strip() + "\n")
            out("FILEDONE", index, txt_path)
        except Exception as e:
            sys.stdout = REAL_STDOUT
            traceback.print_exc()
            out("FILEERR", index, "%s: %s" % (type(e).__name__, e))

    out("ALLDONE", "%.1f" % (time.time() - started))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        traceback.print_exc()
        out("FATAL", "%s: %s" % (type(e).__name__, e))
        sys.exit(1)
