"""Baixa o audio (MP3) de um video do YouTube e transcreve com o Whisper local.

Usa o mesmo worker.py do Transcritor.exe (src\\worker.py), na GPU.
Feito para ser chamado pelo Claude (skill "transcrever-youtube").

Uso:
  python youtube.py URL [URL ...] [--modelo turbo] [--idioma auto] [--pasta nome]

Cada video vai para C:\\Users\\Usuario\\Documents\\Transcritor\\videos\\<pasta>\\ com o .mp3 e o .txt ao lado.
Progresso em stderr; na ultima linha de stdout, um JSON:
  {"ok": true, "itens": [{"url": ..., "titulo": ..., "mp3": ..., "txt": ...}], "segundos": ...}
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time

AQUI = os.path.dirname(os.path.abspath(__file__))
WORKER = os.path.join(AQUI, "src", "worker.py")
PASTA_VIDEOS = os.path.join(AQUI, "videos")


def log(msg):
    print(msg, file=sys.stderr, flush=True)


def nome_pasta(titulo):
    s = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "", titulo).strip().rstrip(".")
    s = re.sub(r"\s+", "-", s.lower())
    return s[:60] or "video"


def baixar(url, pasta_nome):
    """Baixa so o audio em MP3. Devolve (titulo, caminho_mp3)."""
    info = json.loads(subprocess.run(
        [sys.executable, "-m", "yt_dlp", "--no-playlist", "-J", url],
        capture_output=True, text=True, encoding="utf-8", check=True).stdout)
    titulo = info.get("title") or info.get("id") or "video"
    destino = os.path.join(PASTA_VIDEOS, pasta_nome or nome_pasta(titulo))
    os.makedirs(destino, exist_ok=True)
    log(f"baixando: {titulo}")
    r = subprocess.run(
        [sys.executable, "-m", "yt_dlp", "--no-playlist", "-x", "--audio-format", "mp3",
         "-o", os.path.join(destino, "%(title)s.%(ext)s"),
         "--print", "after_move:filepath", url],
        capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 0:
        raise RuntimeError("yt-dlp falhou: " + (r.stderr.strip().splitlines() or ["?"])[-1])
    mp3 = r.stdout.strip().splitlines()[-1]
    return titulo, mp3


def transcrever(mp3s, modelo, idioma):
    """Roda o worker.py do Transcritor e devolve {mp3: txt ou erro}."""
    with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8") as f:
        f.write("\n".join(mp3s))
        lista = f.name
    resultado = {}
    try:
        p = subprocess.Popen([sys.executable, "-u", WORKER, "--model", modelo,
                              "--language", idioma, "--list", lista],
                             stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                             text=True, encoding="utf-8", errors="replace")
        for linha in p.stdout:
            if not linha.startswith("@@"):
                continue
            campos = linha[2:].rstrip("\n").split("\t")
            tipo = campos[0]
            if tipo in ("STATUS", "DEVICE") and not campos[-1].startswith("Detect"):
                log(campos[-1])
            elif tipo == "FILE":
                log(f"transcrevendo {int(campos[1]) + 1}/{campos[2]} "
                    f"({float(campos[4]) / 60:.0f} min de audio)...")
            elif tipo == "FILEDONE":
                resultado[mp3s[int(campos[1])]] = {"txt": campos[2]}
            elif tipo == "FILEERR":
                resultado[mp3s[int(campos[1])]] = {"erro": campos[2]}
            elif tipo == "FATAL":
                raise RuntimeError(campos[1])
        p.wait()
    finally:
        os.remove(lista)
    return resultado


def main():
    ap = argparse.ArgumentParser(description="YouTube -> MP3 -> transcricao (Whisper local)")
    ap.add_argument("urls", nargs="+")
    ap.add_argument("--modelo", default="turbo",
                    help="modelo do Whisper (so o turbo esta em uso)")
    ap.add_argument("--idioma", default="auto", help="pt, en, es... ou auto")
    ap.add_argument("--pasta", default=None,
                    help="nome da subpasta em videos\\ (padrao: titulo do video)")
    a = ap.parse_args()

    t0 = time.time()
    itens = []
    for url in a.urls:
        try:
            titulo, mp3 = baixar(url, a.pasta)
            itens.append({"url": url, "titulo": titulo, "mp3": mp3})
        except Exception as e:
            itens.append({"url": url, "erro": f"download: {e}"})

    mp3s = [i["mp3"] for i in itens if "mp3" in i]
    if mp3s:
        feitos = transcrever(mp3s, a.modelo, a.idioma)
        for i in itens:
            if "mp3" in i:
                i.update(feitos.get(i["mp3"], {"erro": "transcricao nao terminou"}))

    ok = any("txt" in i for i in itens)
    print(json.dumps({"ok": ok, "itens": itens, "segundos": round(time.time() - t0, 1)},
                     ensure_ascii=False))
    return 0 if ok else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        print(json.dumps({"ok": False, "erro": f"{type(e).__name__}: {e}"}, ensure_ascii=False))
        sys.exit(1)
