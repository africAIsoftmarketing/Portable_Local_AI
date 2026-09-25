"""
Rôle    : routeur /v1/* OpenAI-compatible. Proxy vers llama-server avec
          injection du system prompt résolu (voir app/system_prompt/resolver.py).
          Supporte streaming SSE et non-streaming.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
Version : 1.0.6 (2026-09-25) — extension propriétaire `rag` :
          `"rag": {"sources": ["fichier.pdf", ...]}` (ou `"rag": true` pour
          toute la base) → les passages BM25 pertinents pour le dernier
          message utilisateur sont ajoutés au system prompt. Le champ est
          retiré avant l'envoi à llama-server ; streaming inchangé.
"""
from __future__ import annotations

import asyncio
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
                    "context_size": request.app.state.settings.model.context_size,
                })
        return {"object": "list", "data": items}

    @router.post(
        "/chat/completions",
        summary="Complétion chat (OpenAI-compat)",
        description=(
            "Proxy OpenAI-compatible vers `llama-server` en loopback.\n\n"
            "**Comportement auto-injection des tools MCP (Phase 3)** :\n"
            "- Si le champ `tools` est **absent ou vide** et `tool_choice != \"none\"`, "
            "l'orchestrateur injecte automatiquement tous les outils MCP découverts "
            "(cybersec, accounting, rag, general) au format function calling.\n"
            "- Si le client fournit explicitement `tools: [...]`, **SA liste est "
            "utilisée telle quelle** (aucune injection MCP). L'orchestrateur retrouve "
            "malgré tout les skills MCP correspondants par nom préfixé (`<skill>__<tool>`) "
            "lors de l'exécution ; les autres noms sont retournés au modèle en `error`.\n"
            "- Si `tool_choice == \"none\"`, aucun tool n'est jamais offert.\n\n"
            "**Override du context au niveau requête** : le champ `context_size` "
            "(int, optionnel) est passé tel quel à llama-server (borné par la valeur "
            "chargée en mémoire au démarrage — un override supérieur est ignoré).\n\n"
            "Streaming SSE supporté (`stream: true`). Voir aussi `/api/events` pour la "
            "trace agentique parallèle."
        ),
    )
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
        # Trois canaux d'override possibles, tous ignorés si locked=true :
        #   (a) champ 'system' top-level (extension propriétaire)
        #   (b) premier message avec role='system' dans la liste `messages`
        # Priorité (a) > (b) si les deux présents.
        top_override = payload.pop("system", None)
        inline_override = None
        if messages and isinstance(messages[0], dict) \
                and messages[0].get("role") == "system":
            inline_override = messages[0].get("content")

        effective_override = None
        if isinstance(top_override, str) and top_override.strip():
            effective_override = top_override
        elif isinstance(inline_override, str) and inline_override.strip():
            effective_override = inline_override

        settings = request.app.state.settings
        resolved_sp = resolve_system_prompt(
            settings=settings,
            request_override=effective_override,
        )

        # Reconstruit la liste des messages sans DOUBLONNER de system :
        # on retire toute occurrence de role='system' puis on préfixe avec resolved_sp.
        non_system = [m for m in messages
                      if isinstance(m, dict) and m.get("role") != "system"]
        if resolved_sp is not None:
            payload["messages"] = [{"role": "system", "content": resolved_sp}] + non_system
        else:
            payload["messages"] = non_system

        # ── Fichiers joints (RAG) : contexte injecté dans le system prompt ──
        rag_opt = payload.pop("rag", None)
        if rag_opt:
            sources = None
            if isinstance(rag_opt, dict):
                srcs = rag_opt.get("sources")
                if isinstance(srcs, list):
                    sources = [str(x) for x in srcs if isinstance(x, str)][:50] or None
            query = next((m.get("content") for m in reversed(non_system)
                          if isinstance(m, dict) and m.get("role") == "user"
                          and isinstance(m.get("content"), str)), "")
            if query.strip():
                from app.rag.local_search import build_context
                ctx = await asyncio.to_thread(build_context, query, sources)
                if ctx:
                    msgs = payload["messages"]
                    if msgs and msgs[0].get("role") == "system":
                        msgs[0] = {"role": "system",
                                   "content": (msgs[0]["content"] or "") + "\n\n" + ctx}
                    else:
                        payload["messages"] = [{"role": "system", "content": ctx}] + msgs
                    logger.info("RAG : contexte injecté (%d car., sources=%s)",
                                len(ctx), sources or "toutes")

        stream = bool(payload.get("stream", False))

        llama = request.app.state.llama
        client = llama.client() if llama else None
        if client is None:
            raise HTTPException(
                status_code=503,
                detail="llama-server non prêt (voir /health pour le détail)",
            )

        # ── Boucle agentique si tools présents ou tool_choice explicite ─────
        # Correctif Phase 3 : si le client fournit explicitement `tools: [...]`,
        # on respecte SA liste (pas d'injection MCP). Auto-injection MCP
        # uniquement quand `tool_choice` est explicite (auto/required/function).
        # Sans tools ni tool_choice → chat classique (streaming préservé).
        registry = getattr(request.app.state, "mcp", None)
        request_tools = payload.get("tools")
        client_specified_tools = isinstance(request_tools, list) and len(request_tools) > 0
        tool_choice = payload.get("tool_choice")
        # tool_choice="none" → jamais de boucle agentique.
        if tool_choice == "none":
            wants_agent = False
        elif client_specified_tools:
            # Le client a explicité ses outils → on entre en boucle sans injecter les MCP.
            wants_agent = True
        elif tool_choice is not None:
            # tool_choice explicite (auto / required / {function}) → on autorise
            # l'injection MCP par défaut.
            wants_agent = (registry is not None
                           and bool(registry.all_openai_tools()))
        else:
            # Ni tools ni tool_choice → chat classique, aucune injection.
            wants_agent = False
        if wants_agent and not stream:
            from app.agent.loop import AgentLoop
            settings_obj = request.app.state.settings
            loop = AgentLoop(
                llama_client=client,
                registry=registry,
                max_rounds=settings_obj.agentic.max_tool_rounds,
                total_timeout=float(settings_obj.agentic.total_timeout),
                allow_parallel=settings_obj.agentic.allow_parallel_tools,
            )
            # Si client_specified_tools : on passe ses outils à la boucle
            # SANS ajouter les MCP. Sinon on injecte les MCP automatiquement.
            result = await loop.run(
                payload,
                client_tools=request_tools if client_specified_tools else None,
                auto_inject_mcp=not client_specified_tools,
            )
            return JSONResponse(content=result)

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
