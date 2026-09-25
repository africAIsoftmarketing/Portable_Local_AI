"""
Rôle    : registre MCP - découverte automatique des skills sous mcp-servers/,
          lecture skills/registry.json + config/mcp.json, spawn + monitoring.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
Version : 1.0.6 (2026-09-25) — les clients sont inscrits (état « starting »)
          AVANT le handshake : le démarrage peut tourner en arrière-plan
          (main.py) et l'UI voit les skills s'allumer au fil de l'eau.
          `ready` (asyncio.Event) signale la fin du démarrage. Un server.py
          absent est désormais journalisé.
          1.0.5 (2026-09-25) — spawn PARALLÈLE des skills (au lieu de séquentiel) :
          5 skills × 30 s en série faisaient dépasser plusieurs minutes et
          chaque handshake était plafonné à 30 s. En parallèle, le coût de
          démarrage se recouvre et le cache disque se réchauffe une seule fois.
"""
from __future__ import annotations

import asyncio
import json
import logging
from pathlib import Path
from typing import Optional

from app.config.loader import STUDIO_ROOT
from app.mcp.client import MCPClient, MCPError

logger = logging.getLogger("studio.mcp.registry")


class MCPRegistry:
    """Contient tous les MCPClient actifs, indexés par nom de skill."""

    def __init__(self):
        self.clients: dict[str, MCPClient] = {}
        self._config: dict = {}
        self.ready = asyncio.Event()
        self.last_report: dict = {}

    @property
    def enabled(self) -> bool:
        return bool(self._config.get("enabled", True))

    async def start_all(self) -> dict:
        """Charge le registre + config, spawn chaque skill activé, retourne un rapport."""
        self._config = _load_mcp_config()
        if not self.enabled:
            logger.info("MCP désactivé via config/mcp.json.")
            self.last_report = {"enabled": False, "spawned": [], "failed": []}
            self.ready.set()
            return self.last_report

        entries = _discover_skills()
        skill_timeout = float(self._config.get("skill_timeout_sec", 30))
        startup_timeout = float(self._config.get("startup_timeout_sec", 120))
        max_restart = int(self._config.get("max_restart_attempts", 3))

        # Construit un client par skill activé (server.py présent).
        pending: list[MCPClient] = []
        spawned, failed = [], []
        for entry in entries:
            if not entry.get("enabled", True):
                continue
            name = entry["name"]
            server = STUDIO_ROOT / entry["path"] / "server.py"
            if not server.exists():
                logger.warning("MCP '%s' ignoré : server.py absent (%s)", name, server)
                failed.append({"name": name, "reason": f"server.py absent: {server}"})
                continue
            client = MCPClient(
                name=name,
                server_path=server,
                cwd=server.parent,
                timeout_sec=skill_timeout,
                max_restart=max_restart,
                startup_timeout_sec=startup_timeout,
            )
            client.status = "starting"
            self.clients[name] = client  # visible dans /skills dès maintenant
            pending.append(client)

        # Démarrage PARALLÈLE : le coût de spawn (Python embarqué depuis clé USB,
        # scan antivirus) se recouvre au lieu de s'additionner.
        results = await asyncio.gather(
            *[c.start() for c in pending], return_exceptions=True)

        for client, res in zip(pending, results):
            if isinstance(res, BaseException):
                logger.warning("MCP '%s' KO au démarrage : %s", client.name, res)
                failed.append({"name": client.name, "reason": str(res)})
            else:
                spawned.append({"name": client.name,
                                "tools": [t["name"] for t in client.tools]})

        logger.info("MCP: %d skill(s) OK, %d KO", len(spawned), len(failed))
        self.last_report = {"enabled": True, "spawned": spawned, "failed": failed}
        self.ready.set()
        return self.last_report

    async def stop_all(self) -> None:
        await asyncio.gather(*[c.stop() for c in self.clients.values()],
                             return_exceptions=True)

    def find_tool(self, prefixed_name: str) -> tuple[Optional[MCPClient], Optional[str]]:
        """
        À partir d'un nom d'outil préfixé `<skill>__<tool>` (format OpenAI),
        retourne (client, nom_local_de_l_outil). Si le nom n'est pas préfixé,
        cherche l'outil dans TOUS les skills (première correspondance).
        """
        if "__" in prefixed_name:
            skill, tool = prefixed_name.split("__", 1)
            return self.clients.get(skill), tool
        for c in self.clients.values():
            for t in c.tools:
                if t["name"] == prefixed_name:
                    return c, prefixed_name
        return None, None

    def all_openai_tools(self) -> list[dict]:
        """Concatène les tools de tous les skills disponibles au format OpenAI."""
        out = []
        for c in self.clients.values():
            if c.status == "running":
                out.extend(c.as_openai_tools())
        return out

    def snapshot(self) -> list[dict]:
        return [c.snapshot() for c in self.clients.values()]

    def status_summary(self) -> dict:
        running = sum(1 for c in self.clients.values() if c.status == "running")
        total = len(self.clients)
        return {
            "status": "ok" if running == total and total > 0
                     else ("degraded" if running > 0 else "disabled"),
            "running": running,
            "total": total,
            "skills": [{"name": c.name, "status": c.status,
                        "tool_count": len(c.tools)} for c in self.clients.values()],
        }


def _load_mcp_config() -> dict:
    """Lit config/mcp.json (facultatif). Défauts si absent."""
    path = STUDIO_ROOT / "config" / "mcp.json"
    defaults = {
        "enabled": True,
        "auto_discover": True,
        "skill_timeout_sec": 30,
        "startup_timeout_sec": 120,
        "spawn_wait_sec": 5,
        "max_restart_attempts": 3,
    }
    if not path.exists():
        return defaults
    try:
        d = json.loads(path.read_text(encoding="utf-8"))
        defaults.update(d)
    except json.JSONDecodeError as e:
        logger.error("config/mcp.json invalide : %s", e)
    return defaults


def _discover_skills() -> list[dict]:
    """
    Charge skills/registry.json. Si `auto_discover=true` dans mcp.json,
    complète par scan du dossier mcp-servers/ (chaque dossier != _* est un skill).
    """
    reg_path = STUDIO_ROOT / "skills" / "registry.json"
    entries: list[dict] = []
    seen = set()

    if reg_path.exists():
        try:
            data = json.loads(reg_path.read_text(encoding="utf-8"))
            for s in data.get("skills", []):
                if "name" in s and "path" in s:
                    entries.append(s)
                    seen.add(s["name"])
        except json.JSONDecodeError as e:
            logger.error("skills/registry.json invalide : %s", e)

    # Auto-discover : ajoute tous les dossiers non préfixés '_' non déjà listés.
    mcp_dir = STUDIO_ROOT / "mcp-servers"
    if mcp_dir.exists():
        for sub in sorted(mcp_dir.iterdir()):
            if not sub.is_dir() or sub.name.startswith("_"):
                continue
            if sub.name in seen:
                continue
            if (sub / "server.py").exists():
                entries.append({"name": sub.name,
                                "path": f"mcp-servers/{sub.name}",
                                "enabled": True})
    return entries
