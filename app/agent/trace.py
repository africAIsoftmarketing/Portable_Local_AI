"""
Rôle    : bus d'événements SSE pour la trace agentique. Chaque session_id a
          sa queue asyncio ; l'endpoint /api/events itère la queue et pousse
          les événements horodatés à l'UI (lecture seule).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import asyncio
import json
import time
import uuid
from typing import AsyncIterator, Optional


class TraceBus:
    """Bus in-memory. Un dict de queues par session_id, capacité limitée."""

    def __init__(self, max_queue_size: int = 100):
        self._queues: dict[str, asyncio.Queue] = {}
        self._max = max_queue_size

    def _queue(self, session_id: str) -> asyncio.Queue:
        q = self._queues.get(session_id)
        if q is None:
            q = asyncio.Queue(maxsize=self._max)
            self._queues[session_id] = q
        return q

    def emit(self, session_id: str, event_type: str, data: dict) -> dict:
        """Push un événement, retourne l'événement construit (pour inclusion dans metadata)."""
        evt = {
            "ts": _now_iso(),
            "session_id": session_id,
            "type": event_type,
            "data": data,
        }
        q = self._queue(session_id)
        try:
            q.put_nowait(evt)
        except asyncio.QueueFull:
            # Purge le plus vieux et retente.
            try:
                q.get_nowait()
                q.put_nowait(evt)
            except Exception:  # noqa: BLE001
                pass
        return evt

    async def subscribe(self, session_id: str) -> AsyncIterator[dict]:
        """Consomme la queue jusqu'à réception d'un événement `close`."""
        q = self._queue(session_id)
        try:
            while True:
                evt = await q.get()
                yield evt
                if evt.get("type") == "close":
                    return
        finally:
            # Nettoie la queue quand plus personne n'écoute.
            self._queues.pop(session_id, None)

    def close(self, session_id: str) -> None:
        self.emit(session_id, "close", {})


def _now_iso() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def new_session_id() -> str:
    return uuid.uuid4().hex[:12]


# Instance globale (une par process, orchestrateur mono-worker).
BUS = TraceBus()
