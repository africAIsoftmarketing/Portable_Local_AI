"""
Rôle    : client MCP stdio (JSON-RPC 2.0 line-delimited). Chaque instance gère
          un subprocess de skill, un handshake `initialize` + `tools/list`, des
          appels `tools/call` timeoutés, la surveillance et le respawn (max N).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
Version : 1.0.5 (2026-09-25) — timeout de DÉMARRAGE (initialize + tools/list)
          distinct du timeout par appel : sur clé USB lente, le premier spawn
          du Python embarqué (lecture stdlib + site-packages, scan antivirus
          des exécutables sur média amovible) peut dépasser 30 s. Les appels
          d'outils au runtime gardent, eux, un délai court.
"""
from __future__ import annotations

import asyncio
import json
import logging
import os
import signal
import sys
from pathlib import Path
from typing import Any, Optional

logger = logging.getLogger("studio.mcp.client")


class MCPError(Exception):
    """Erreur JSON-RPC retournée par un skill."""


class MCPClient:
    """Un client = un skill = un subprocess Python."""

    def __init__(self, name: str, server_path: Path, cwd: Path,
                 timeout_sec: float = 30.0, max_restart: int = 3,
                 startup_timeout_sec: float | None = None):
        self.name = name
        self.server_path = server_path
        self.cwd = cwd
        self.timeout_sec = timeout_sec
        # Démarrage (handshake) : plus tolérant que les appels d'outils.
        self.startup_timeout_sec = startup_timeout_sec or max(timeout_sec, 120.0)
        self.max_restart = max_restart

        self.proc: Optional[asyncio.subprocess.Process] = None
        self.tools: list[dict] = []
        self.status: str = "stopped"  # stopped|starting|running|unavailable|crashed
        self.last_error: Optional[str] = None
        self.restart_count: int = 0

        self._id_counter: int = 0
        self._pending: dict[int, asyncio.Future] = {}
        self._read_task: Optional[asyncio.Task] = None
        self._stderr_task: Optional[asyncio.Task] = None
        self._start_lock = asyncio.Lock()

    async def start(self) -> None:
        """Spawn le subprocess + handshake initialize + tools/list."""
        async with self._start_lock:
            if self.status == "running":
                return
            self.status = "starting"
            self.last_error = None

            python = sys.executable  # même interpréteur que l'orchestrateur
            try:
                self.proc = await asyncio.create_subprocess_exec(
                    python, "-u", str(self.server_path),
                    cwd=str(self.cwd),
                    stdin=asyncio.subprocess.PIPE,
                    stdout=asyncio.subprocess.PIPE,
                    stderr=asyncio.subprocess.PIPE,
                )
            except Exception as e:  # noqa: BLE001
                self.status = "unavailable"
                self.last_error = f"spawn failed: {e}"
                raise

            self._read_task = asyncio.create_task(self._read_loop())
            self._stderr_task = asyncio.create_task(self._stderr_drain())

            try:
                await self._request("initialize",
                                    {"protocolVersion": "0.1",
                                     "clientInfo": {"name": "africaisoft-studio",
                                                    "version": "0.3.0"}},
                                    timeout=self.startup_timeout_sec)
                res = await self._request("tools/list", {},
                                          timeout=self.startup_timeout_sec)
                self.tools = res.get("tools", [])
                self.status = "running"
                logger.info("MCP skill '%s' prêt (%d outils).",
                            self.name, len(self.tools))
            except Exception as e:  # noqa: BLE001
                self.status = "unavailable"
                self.last_error = f"handshake failed: {e}"
                await self._hard_stop()
                raise

    async def call(self, tool_name: str, arguments: dict[str, Any]) -> Any:
        """Appelle un outil. Timeout par appel (self.timeout_sec)."""
        if self.status != "running":
            # Tentative de respawn si crashed et budget restant.
            if self.status == "crashed" and self.restart_count < self.max_restart:
                await self._try_restart()
            if self.status != "running":
                raise MCPError(
                    {"code": -32603,
                     "message": f"skill '{self.name}' indisponible ({self.status})"})
        return await self._request("tools/call",
                                   {"name": tool_name, "arguments": arguments})

    async def stop(self) -> None:
        """Arrêt propre (SIGTERM puis SIGKILL après 5 s)."""
        self.status = "stopped"
        if not self.proc or self.proc.returncode is not None:
            return
        try:
            self.proc.send_signal(signal.SIGTERM)
        except ProcessLookupError:
            return
        try:
            await asyncio.wait_for(self.proc.wait(), timeout=5.0)
        except asyncio.TimeoutError:
            try:
                self.proc.kill()
            except ProcessLookupError:
                pass
            await self.proc.wait()

    def as_openai_tools(self) -> list[dict]:
        """Convertit `tools/list` en format OpenAI (function calling)."""
        out = []
        for t in self.tools:
            out.append({
                "type": "function",
                "function": {
                    "name": f"{self.name}__{t['name']}",  # préfixe pour éviter les collisions
                    "description": t.get("description", ""),
                    "parameters": t.get("inputSchema") or {"type": "object"},
                },
            })
        return out

    def snapshot(self) -> dict:
        """État introspectable pour /api/skills."""
        return {
            "name": self.name,
            "status": self.status,
            "restart_count": self.restart_count,
            "last_error": self.last_error,
            "tools": [{"name": t["name"], "description": t.get("description", "")}
                      for t in self.tools],
        }

    # ── Interne ──────────────────────────────────────────────────────────────
    async def _request(self, method: str, params: dict,
                       timeout: float | None = None) -> Any:
        assert self.proc and self.proc.stdin
        self._id_counter += 1
        req_id = self._id_counter
        msg = {"jsonrpc": "2.0", "id": req_id,
               "method": method, "params": params}
        fut: asyncio.Future = asyncio.get_event_loop().create_future()
        self._pending[req_id] = fut
        try:
            line = (json.dumps(msg, ensure_ascii=False) + "\n").encode("utf-8")
            self.proc.stdin.write(line)
            await self.proc.stdin.drain()
        except Exception as e:  # noqa: BLE001
            self._pending.pop(req_id, None)
            raise MCPError({"code": -32603, "message": f"stdin write: {e}"})
        try:
            resp = await asyncio.wait_for(fut, timeout=timeout or self.timeout_sec)
        except asyncio.TimeoutError:
            self._pending.pop(req_id, None)
            raise MCPError({"code": -32000,
                            "message": f"timeout {timeout or self.timeout_sec}s sur {method}"})
        if "error" in resp:
            raise MCPError(resp["error"])
        return resp.get("result", {})

    async def _read_loop(self) -> None:
        assert self.proc and self.proc.stdout
        while True:
            line = await self.proc.stdout.readline()
            if not line:
                # Process fermé - notifie les pendings + marque crashed.
                for fut in list(self._pending.values()):
                    if not fut.done():
                        fut.set_exception(MCPError({"code": -32000,
                                                    "message": "process ended"}))
                self._pending.clear()
                if self.status == "running":
                    self.status = "crashed"
                    logger.warning("MCP skill '%s' terminé (rc=%s).",
                                   self.name,
                                   self.proc.returncode if self.proc else "?")
                return
            try:
                msg = json.loads(line.decode("utf-8"))
            except json.JSONDecodeError:
                logger.debug("MCP '%s' non-JSON stdout: %s", self.name, line[:120])
                continue
            mid = msg.get("id")
            if mid in self._pending:
                fut = self._pending.pop(mid)
                if not fut.done():
                    fut.set_result(msg)

    async def _stderr_drain(self) -> None:
        assert self.proc and self.proc.stderr
        while True:
            line = await self.proc.stderr.readline()
            if not line:
                return
            logger.debug("MCP[%s] stderr: %s", self.name,
                         line.decode(errors="ignore").rstrip())

    async def _try_restart(self) -> None:
        self.restart_count += 1
        logger.info("MCP '%s' respawn (%d/%d)",
                    self.name, self.restart_count, self.max_restart)
        try:
            await self._hard_stop()
            await self.start()
        except Exception as e:  # noqa: BLE001
            self.last_error = f"restart failed: {e}"
            self.status = "unavailable" if self.restart_count >= self.max_restart else "crashed"

    async def _hard_stop(self) -> None:
        if self.proc and self.proc.returncode is None:
            try:
                self.proc.kill()
            except ProcessLookupError:
                pass
            try:
                await asyncio.wait_for(self.proc.wait(), timeout=2.0)
            except asyncio.TimeoutError:
                pass
        self.proc = None
