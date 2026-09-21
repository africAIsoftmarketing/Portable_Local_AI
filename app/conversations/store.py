"""
Rôle    : store JSON persistant pour les conversations utilisateur.
          Écrit atomiquement dans data/conversations.json. Verrou de fichier
          simple (single-writer) : l'orchestrateur est mono-worker.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import json
import threading
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional


_LOCK = threading.Lock()


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


class ConversationStore:
    """Backend fichier JSON. Format : {"conversations": [ {id,title,created_at,
    updated_at,messages:[{role,content,...}]} ]}."""

    def __init__(self, path: Path):
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)

    # ── Lecture / écriture bas niveau ────────────────────────────────────────
    def _read(self) -> dict:
        if not self.path.exists():
            return {"conversations": []}
        try:
            return json.loads(self.path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return {"conversations": []}

    def _write(self, data: dict) -> None:
        tmp = self.path.with_suffix(".json.tmp")
        tmp.write_text(
            json.dumps(data, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        tmp.replace(self.path)

    # ── API publique ────────────────────────────────────────────────────────
    def list_summaries(self) -> list[dict]:
        with _LOCK:
            data = self._read()
        # Tri décroissant par updated_at pour affichage sidebar.
        out = []
        for c in data.get("conversations", []):
            out.append({
                "id": c.get("id"),
                "title": c.get("title", ""),
                "created_at": c.get("created_at", ""),
                "updated_at": c.get("updated_at", ""),
                "message_count": len(c.get("messages", [])),
            })
        out.sort(key=lambda x: x.get("updated_at", ""), reverse=True)
        return out

    def get(self, conv_id: str) -> Optional[dict]:
        with _LOCK:
            data = self._read()
        for c in data.get("conversations", []):
            if c.get("id") == conv_id:
                return c
        return None

    def create(self, title: str) -> dict:
        conv = {
            "id": uuid.uuid4().hex[:12],
            "title": title,
            "created_at": _now_iso(),
            "updated_at": _now_iso(),
            "messages": [],
        }
        with _LOCK:
            data = self._read()
            data.setdefault("conversations", []).append(conv)
            self._write(data)
        return conv

    def rename(self, conv_id: str, new_title: str) -> bool:
        with _LOCK:
            data = self._read()
            for c in data.get("conversations", []):
                if c.get("id") == conv_id:
                    c["title"] = new_title
                    c["updated_at"] = _now_iso()
                    self._write(data)
                    return True
        return False

    def delete(self, conv_id: str) -> bool:
        with _LOCK:
            data = self._read()
            before = len(data.get("conversations", []))
            data["conversations"] = [c for c in data.get("conversations", [])
                                     if c.get("id") != conv_id]
            if len(data["conversations"]) == before:
                return False
            self._write(data)
        return True

    def append_messages(self, conv_id: str, messages: list[dict]) -> Optional[dict]:
        """Ajoute une liste de messages ; retourne la conversation mise à jour."""
        with _LOCK:
            data = self._read()
            for c in data.get("conversations", []):
                if c.get("id") == conv_id:
                    c.setdefault("messages", []).extend(messages)
                    c["updated_at"] = _now_iso()
                    # Auto-titre depuis le premier message user si titre par défaut.
                    if c.get("title") in ("", "Nouvelle conversation", "New conversation"):
                        for m in messages:
                            if m.get("role") == "user" and m.get("content"):
                                c["title"] = m["content"][:60].replace("\n", " ")
                                break
                    self._write(data)
                    return c
        return None
