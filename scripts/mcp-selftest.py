"""
Rôle    : diagnostic des skills MCP, à lancer SUR LA CLÉ quand des skills
          sont « indisponibles ». Pour chaque skill de mcp-servers/, mesure le
          handshake (initialize + tools/list) avec les DEUX transports :
            - threads  : subprocess.Popen + threads lecteurs (transport 1.0.6)
            - asyncio  : pipes asyncio (transport ≤ 1.0.5)
          et affiche le temps, le résultat et la fin du stderr du skill.
          Usage (Windows) : scripts\\mcp-selftest.bat
          Usage (direct)  : <python> scripts/mcp-selftest.py [timeout_s]
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-09-25
Version : 1.0.6
"""
from __future__ import annotations

import asyncio
import json
import os
import subprocess
import sys
import threading
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MCP_DIR = ROOT / "mcp-servers"
TIMEOUT = float(sys.argv[1]) if len(sys.argv) > 1 else 20.0

INIT = {"jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": {"protocolVersion": "0.1", "clientInfo": {"name": "selftest"}}}
LIST = {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}}


def _env() -> dict:
    env = dict(os.environ)
    env.update(PYTHONUTF8="1", PYTHONIOENCODING="utf-8", PYTHONUNBUFFERED="1",
               PYTHONDONTWRITEBYTECODE="1")
    return env


def _cmd(server: Path) -> list[str]:
    return [sys.executable, "-X", "utf8", "-u", str(server)]


def test_threads(server: Path) -> tuple[str, float, str]:
    t0 = time.perf_counter()
    flags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
    p = subprocess.Popen(_cmd(server), cwd=str(server.parent), stdin=subprocess.PIPE,
                         stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0,
                         env=_env(), creationflags=flags)
    err: list[str] = []
    threading.Thread(target=lambda: err.extend(
        l.decode("utf-8", "replace").rstrip() for l in iter(p.stderr.readline, b"")),
        daemon=True).start()
    got: dict = {}
    done = threading.Event()

    def reader():
        for raw in iter(p.stdout.readline, b""):
            try:
                m = json.loads(raw)
            except ValueError:
                continue
            got[m.get("id")] = m
            if 2 in got:
                break
        done.set()

    threading.Thread(target=reader, daemon=True).start()
    try:
        p.stdin.write((json.dumps(INIT) + "\n").encode())
        p.stdin.write((json.dumps(LIST) + "\n").encode())
        p.stdin.flush()
    except OSError as e:
        return f"ÉCHEC écriture stdin : {e}", time.perf_counter() - t0, "\n".join(err[-8:])
    done.wait(TIMEOUT)
    dt = time.perf_counter() - t0
    p.kill()
    if 2 in got:
        n = len(got[2].get("result", {}).get("tools", []))
        return f"OK ({n} outils)", dt, "\n".join(err[-8:])
    step = "tools/list" if 1 in got else "initialize"
    rc = p.poll()
    return f"ÉCHEC : pas de réponse à {step} (rc={rc})", dt, "\n".join(err[-8:])


async def _asyncio_case(server: Path) -> tuple[str, float, str]:
    t0 = time.perf_counter()
    p = await asyncio.create_subprocess_exec(
        *_cmd(server), cwd=str(server.parent), stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE, env=_env())
    p.stdin.write((json.dumps(INIT) + "\n" + json.dumps(LIST) + "\n").encode())
    await p.stdin.drain()
    got: dict = {}

    async def reader():
        while 2 not in got:
            raw = await p.stdout.readline()
            if not raw:
                return
            try:
                m = json.loads(raw)
            except ValueError:
                continue
            got[m.get("id")] = m

    try:
        await asyncio.wait_for(reader(), TIMEOUT)
    except asyncio.TimeoutError:
        pass
    dt = time.perf_counter() - t0
    p.kill()
    try:
        await asyncio.wait_for(p.wait(), 5)
    except Exception:  # noqa: BLE001
        pass
    try:
        err = (await asyncio.wait_for(p.stderr.read(), 2)).decode("utf-8", "replace")
    except Exception:  # noqa: BLE001
        err = ""
    if 2 in got:
        n = len(got[2].get("result", {}).get("tools", []))
        return f"OK ({n} outils)", dt, err[-600:]
    step = "tools/list" if 1 in got else "initialize"
    return f"ÉCHEC : pas de réponse à {step}", dt, err[-600:]


def test_asyncio(server: Path) -> tuple[str, float, str]:
    try:
        return asyncio.run(_asyncio_case(server))
    except Exception as e:  # noqa: BLE001
        return f"ÉCHEC transport asyncio : {type(e).__name__}: {e}", 0.0, ""


def main() -> int:
    print("=" * 66)
    print(" AfricAIsoft Portable Studio — diagnostic des skills MCP")
    print("=" * 66)
    print(f" Python  : {sys.executable} ({sys.version.split()[0]})")
    print(f" Racine  : {ROOT}")
    print(f" Timeout : {TIMEOUT:.0f} s par test")
    if os.name == "nt":
        print(f" Boucle  : {type(asyncio.new_event_loop()).__name__}")
    print()
    servers = sorted(d / "server.py" for d in MCP_DIR.iterdir()
                     if d.is_dir() and not d.name.startswith("_")
                     and (d / "server.py").exists())
    ko = 0
    for srv in servers:
        print(f"── {srv.parent.name}")
        for label, fn in (("threads (1.0.6)", test_threads),
                          ("asyncio (≤1.0.5)", test_asyncio)):
            res, dt, err = fn(srv)
            print(f"   {label:<17} {dt:6.2f} s  {res}")
            if not res.startswith("OK"):
                ko += label.startswith("threads")
                for line in (err or "").strip().splitlines()[-8:]:
                    print(f"      stderr> {line}")
        print()
    print("Résultat :", "tous les skills répondent (transport 1.0.6)." if not ko
          else f"{ko} skill(s) en échec avec le transport 1.0.6 — envoyez cette sortie au support.")
    return 1 if ko else 0


if __name__ == "__main__":
    sys.exit(main())
