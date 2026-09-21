"""
Rôle    : routeur /v1/* OpenAI-compatible. Proxy vers llama-server avec
          injection du system prompt résolu (voir app/system_prompt/resolver.py).
          Supporte streaming SSE et non-streaming.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Any

import httpx
from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import JSONResponse, StreamingResponse

from app.config.loader import STUDIO_ROOT
from app.system_prompt.resolver import resolve_system_prompt

logger = logging.getLogger("studio.v1")


def build_v1_router() -> APIRouter:
    router = APIRouter()

    @router.get("/models", summary="Liste des modèles disponibles")
    async def list_models(request: Request):
        """Format OpenAI : {object:'list', data:[{id, object:'model', ...}, ...]}."""
        models_dir = STUDIO_ROOT / "models"
        items: list[dict[str, Any]] = []
        if models_dir.exists():
            for p in sorted(models_dir.glob("*.gguf")):
                items.append({
                    "id": p.name,
                    "object": "model",
                    "created": int(p.stat().st_mtime),
                    "owned_by": "local",
                    "size_bytes": p.stat().st_size,
                    "context_length": request.app.state.settings.model.context_length,
                })
        return {"object": "list", "data": items}

    @router.post("/chat/completions", summary="Complétion chat (OpenAI-compat)")
    async def chat_completions(request: Request):
        """Proxy vers llama-server /v1/chat/completions avec system prompt injecté."""
        try:
            payload = await request.json()
        except json.JSONDecodeError:
            raise HTTPException(status_code=400, detail="Corps JSON invalide")

        if not isinstance(payload, dict):
            raise HTTPException(status_code=400, detail="Payload doit être un objet JSON")

        messages = payload.get("messages")
        if not isinstance(messages, list):
            raise HTTPException(status_code=400,
                                detail="`messages` doit être une liste")

        # Résolution du system prompt (règles §6 de l'architecture).
        override = payload.pop("system", None)
        settings = request.app.state.settings
        resolved_sp = resolve_system_prompt(
            settings=settings,
            request_override=override if isinstance(override, str) else None,
        )

        # Injecte / remplace le message system.
        if resolved_sp is not None:
            has_system = messages and messages[0].get("role") == "system"
            if has_system:
                messages[0] = {"role": "system", "content": resolved_sp}
            else:
                messages.insert(0, {"role": "system", "content": resolved_sp})
        payload["messages"] = messages

        stream = bool(payload.get("stream", False))

        llama = request.app.state.llama
        client = llama.client() if llama else None
        if client is None:
            raise HTTPException(
                status_code=503,
                detail="llama-server non prêt (voir /health pour le détail)",
            )

        if stream:
            return StreamingResponse(
                _stream_proxy(client, payload),
                media_type="text/event-stream",
                headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
            )

        try:
            r = await client.post("/v1/chat/completions", json=payload)
        except httpx.HTTPError as e:
            raise HTTPException(status_code=502,
                                detail=f"llama-server injoignable : {e}") from e
        return JSONResponse(status_code=r.status_code, content=r.json())

    @router.post("/completions", summary="Complétion legacy (OpenAI-compat)")
    async def completions(request: Request):
        """Proxy simple vers llama-server /v1/completions (pas d'injection system)."""
        try:
            payload = await request.json()
        except json.JSONDecodeError:
            raise HTTPException(status_code=400, detail="Corps JSON invalide")

        llama = request.app.state.llama
        client = llama.client() if llama else None
        if client is None:
            raise HTTPException(status_code=503, detail="llama-server non prêt")

        if payload.get("stream"):
            return StreamingResponse(
                _stream_proxy(client, payload, path="/v1/completions"),
                media_type="text/event-stream",
            )
        r = await client.post("/v1/completions", json=payload)
        return JSONResponse(status_code=r.status_code, content=r.json())

    return router


async def _stream_proxy(client: httpx.AsyncClient, payload: dict,
                        path: str = "/v1/chat/completions"):
    """Générateur asynchrone qui retransmet les événements SSE de llama-server."""
    payload["stream"] = True
    try:
        async with client.stream("POST", path, json=payload) as resp:
            if resp.status_code != 200:
                body = (await resp.aread()).decode(errors="ignore")
                err = {"error": {"message": f"llama-server {resp.status_code}: {body[:400]}",
                                 "type": "upstream_error"}}
                yield f"data: {json.dumps(err)}\n\n".encode()
                yield b"data: [DONE]\n\n"
                return
            async for chunk in resp.aiter_raw():
                if chunk:
                    yield chunk
    except httpx.HTTPError as e:
        err = {"error": {"message": f"stream error: {e}", "type": "upstream_error"}}
        yield f"data: {json.dumps(err)}\n\n".encode()
        yield b"data: [DONE]\n\n"
