"""
Rôle    : routeur endpoints studio (system prompt, config, skills, conversations,
          modèles, trace SSE).
          - GET/PUT /system-prompt   (respect du flag locked)
          - GET     /system-prompt/presets
          - POST    /system-prompt/reset
          - POST    /system-prompt/activate/{preset_id}
          - GET/PUT /config
          - GET     /skills
          - POST    /skills/{skill_name}/invoke  (schema SkillInvokePayload)
          - POST    /skills/rag/reindex
          - GET     /skills/rag/documents   ← nouveau Phase 3
          - GET/POST/PUT/DELETE /conversations[...]      ← Phase 4
          - POST    /models/switch                        ← Phase 4
          - GET     /events (SSE)
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import logging
from datetime import datetime, timezone
from typing import Any, Optional

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, ConfigDict, Field, ValidationError

from app.config.loader import STUDIO_ROOT, load_settings, save_settings
from app.config.models import Settings
from app.conversations.store import ConversationStore
from app.agent.trace import BUS
from app.system_prompt.presets import list_presets, load_preset
from app.system_prompt.resolver import (
    SYSTEM_PROMPT_FILE,
    DEFAULT_SYSTEM_PROMPT_FILE,
    approximate_token_count,
    read_current_prompt,
    write_prompt,
)

logger = logging.getLogger("studio.studio")


# ── Schémas Pydantic pour OpenAPI ────────────────────────────────────────────

class SystemPromptPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    content: str = Field(..., min_length=0, max_length=200_000,
                         description="Contenu textuel du system prompt.")


class SkillInvokePayload(BaseModel):
    """Corps de POST /skills/{skill_name}/invoke — schéma explicite pour l'OpenAPI."""
    model_config = ConfigDict(extra="forbid")
    tool: str = Field(..., min_length=1, max_length=128,
                      description="Nom local de l'outil à appeler (sans préfixe skill).")
    arguments: dict[str, Any] = Field(
        default_factory=dict,
        description="Arguments JSON conformes à l'inputSchema déclaré par l'outil.",
    )


class ConversationCreatePayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    title: str = Field(default="Nouvelle conversation", min_length=1, max_length=200)


class ConversationRenamePayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    title: str = Field(..., min_length=1, max_length=200)


class ConversationMessage(BaseModel):
    model_config = ConfigDict(extra="allow")
    role: str = Field(..., pattern=r"^(user|assistant|system|tool)$")
    content: str = ""


class ConversationAppendPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    messages: list[ConversationMessage] = Field(..., min_length=1, max_length=200)


class ModelSwitchPayload(BaseModel):
    model_config = ConfigDict(extra="forbid", protected_namespaces=())
    model_id: str = Field(..., min_length=1, max_length=512,
                          description="Nom de fichier .gguf présent dans models/ (ex: 'qwen2.5-3b-instruct-q4_k_m.gguf').")


def build_studio_router() -> APIRouter:
    router = APIRouter()

    # ═════════════════════════════════════════════════════════════════════════
    # System prompt
    # ═════════════════════════════════════════════════════════════════════════
    @router.get("/system-prompt", summary="Lit le system prompt courant")
    async def get_system_prompt(request: Request):
        settings = request.app.state.settings
        if settings.system_prompt.locked:
            raise HTTPException(status_code=403,
                                detail="System prompt verrouillé (locked=true).")
        content = read_current_prompt()
        return {
            "content": content,
            "active_preset": settings.system_prompt.active_preset,
            "source": _source_of(settings, content),
            "token_count_approx": approximate_token_count(content),
            "locked": False,
        }

    @router.put("/system-prompt", summary="Remplace le system prompt courant")
    async def put_system_prompt(request: Request, payload: SystemPromptPayload):
        settings = request.app.state.settings
        if settings.system_prompt.locked:
            raise HTTPException(status_code=403,
                                detail="System prompt verrouillé (locked=true).")
        write_prompt(payload.content)
        settings.system_prompt.active_preset = None
        save_settings(settings)
        return {"status": "updated",
                "token_count_approx": approximate_token_count(payload.content)}

    @router.post("/system-prompt/reset",
                 summary="Restaure le system prompt par défaut fabricant")
    async def reset_system_prompt(request: Request):
        settings = request.app.state.settings
        if settings.system_prompt.locked:
            raise HTTPException(status_code=403,
                                detail="System prompt verrouillé (locked=true).")
        if SYSTEM_PROMPT_FILE.exists():
            SYSTEM_PROMPT_FILE.unlink()
        settings.system_prompt.active_preset = None
        save_settings(settings)
        return {"status": "reset",
                "content": DEFAULT_SYSTEM_PROMPT_FILE.read_text(encoding="utf-8")
                if DEFAULT_SYSTEM_PROMPT_FILE.exists() else ""}

    @router.get("/system-prompt/presets", summary="Liste les presets embarqués")
    async def get_presets(request: Request):
        settings = request.app.state.settings
        if settings.system_prompt.locked:
            raise HTTPException(status_code=403,
                                detail="System prompt verrouillé (locked=true).")
        return {"presets": list_presets()}

    @router.post("/system-prompt/activate/{preset_id}",
                 summary="Active un preset (devient la source principale)")
    async def activate_preset(preset_id: str, request: Request):
        settings = request.app.state.settings
        if settings.system_prompt.locked:
            raise HTTPException(status_code=403,
                                detail="System prompt verrouillé (locked=true).")
        content = load_preset(preset_id)
        if content is None:
            raise HTTPException(status_code=404,
                                detail=f"Preset introuvable : {preset_id}")
        settings.system_prompt.active_preset = preset_id
        save_settings(settings)
        return {"status": "activated", "preset_id": preset_id,
                "content_preview": content[:200]}

    # ═════════════════════════════════════════════════════════════════════════
    # Config
    # ═════════════════════════════════════════════════════════════════════════
    @router.get("/config", summary="Lit la configuration effective")
    async def get_config(request: Request):
        return request.app.state.settings.model_dump(mode="json")

    @router.get("/config/schema", summary="Retourne le JSON Schema de la configuration")
    async def get_config_schema():
        import json
        path = STUDIO_ROOT / "config" / "settings.schema.json"
        if not path.exists():
            raise HTTPException(status_code=404, detail="Schema absent.")
        return json.loads(path.read_text(encoding="utf-8"))

    @router.put("/config", summary="Remplace la configuration (validation stricte)")
    async def put_config(request: Request, body: dict[str, Any]):
        try:
            new_settings = Settings.model_validate(body)
        except ValidationError as e:
            raise HTTPException(status_code=422,
                                detail={"error": "validation", "issues": e.errors()})
        save_settings(new_settings)
        request.app.state.settings = load_settings(force_reload=True)
        return {"status": "saved", "requires_restart": [
            "server.bind_host", "server.port", "model.path", "model.context_size",
            "platform.backend"
        ]}

    # ═════════════════════════════════════════════════════════════════════════
    # Skills MCP
    # ═════════════════════════════════════════════════════════════════════════
    @router.get("/skills", summary="Liste des skills MCP et leur état")
    async def list_skills(request: Request):
        reg = getattr(request.app.state, "mcp", None)
        if reg is None:
            return {"enabled": False, "skills": []}
        return {"enabled": True, "skills": reg.snapshot(),
                "status": reg.status_summary()}

    @router.post(
        "/skills/{skill_name}/invoke",
        summary="Invocation directe d'un outil d'un skill (debug/tests)",
        description=(
            "Appelle un outil MCP en direct sans passer par le modèle. "
            "Utile pour le debug, les tests, ou une intégration UI en lecture."
        ),
    )
    async def invoke_skill(skill_name: str, request: Request,
                           payload: SkillInvokePayload):
        reg = getattr(request.app.state, "mcp", None)
        if reg is None or skill_name not in reg.clients:
            raise HTTPException(status_code=404, detail=f"Skill inconnu : {skill_name}")
        client = reg.clients[skill_name]
        try:
            result = await client.call(payload.tool, payload.arguments)
        except Exception as e:  # noqa: BLE001
            raise HTTPException(status_code=502,
                                detail={"error": "mcp_error", "message": str(e)})
        return {"skill": skill_name, "tool": payload.tool, "result": result}

    @router.post("/skills/rag/reindex",
                 summary="Force la réindexation du RAG (raccourci)")
    async def rag_reindex(request: Request):
        reg = getattr(request.app.state, "mcp", None)
        if reg is None or "rag" not in reg.clients:
            raise HTTPException(status_code=404, detail="Skill rag indisponible")
        result = await reg.clients["rag"].call("reindex_knowledge_base", {})
        return {"status": "ok", "result": result}

    @router.get("/skills/rag/documents",
                summary="Liste les documents indexés dans la base RAG")
    async def rag_documents():
        """Scan direct du dossier knowledge/documents/ (txt/md/pdf)."""
        docs_dir = STUDIO_ROOT / "mcp-servers" / "rag" / "knowledge" / "documents"
        items: list[dict] = []
        if docs_dir.exists():
            for f in sorted(docs_dir.rglob("*")):
                if not f.is_file():
                    continue
                if f.suffix.lower() not in (".txt", ".md", ".pdf"):
                    continue
                st = f.stat()
                items.append({
                    "path": str(f.relative_to(docs_dir)),
                    "size_bytes": st.st_size,
                    "modified_at": datetime.fromtimestamp(
                        st.st_mtime, tz=timezone.utc).isoformat(),
                    "type": f.suffix.lower().lstrip("."),
                })
        # État de l'index (fichier bm25.pkl)
        index_file = STUDIO_ROOT / "mcp-servers" / "rag" / "knowledge" / "index" / "bm25.pkl"
        index_info = None
        if index_file.exists():
            st = index_file.stat()
            index_info = {
                "size_bytes": st.st_size,
                "modified_at": datetime.fromtimestamp(
                    st.st_mtime, tz=timezone.utc).isoformat(),
            }
        return {
            "documents_dir": str(docs_dir),
            "documents": items,
            "count": len(items),
            "index": index_info,
        }

    # ═════════════════════════════════════════════════════════════════════════
    # Modèles - liste + switch
    # ═════════════════════════════════════════════════════════════════════════
    @router.get("/models/available",
                summary="Liste des .gguf disponibles avec métadonnées parsées")
    async def list_available_models():
        models_dir = STUDIO_ROOT / "models"
        items: list[dict] = []
        if models_dir.exists():
            for p in sorted(models_dir.glob("*.gguf")):
                st = p.stat()
                items.append({
                    "id": p.name,
                    "path": str(p),
                    "size_bytes": st.st_size,
                    "modified_at": datetime.fromtimestamp(
                        st.st_mtime, tz=timezone.utc).isoformat(),
                    "quantization": _parse_quant(p.name),
                    "parameters_hint": _parse_params(p.name),
                })
        return {"models": items, "count": len(items),
                "models_dir": str(models_dir)}

    @router.post("/models/switch", summary="Bascule le modèle actif (unload+load)")
    async def switch_model(request: Request, payload: ModelSwitchPayload):
        models_dir = STUDIO_ROOT / "models"
        target = models_dir / payload.model_id
        if not target.exists() or not target.is_file():
            raise HTTPException(status_code=404,
                                detail=f"Modèle absent : {payload.model_id}")
        settings = request.app.state.settings
        # Persistance de la sélection.
        settings.model.path = str(target.relative_to(STUDIO_ROOT))
        save_settings(settings)
        # Recharge llama-server.
        llama = getattr(request.app.state, "llama", None)
        if llama is None:
            return {"status": "config_saved", "reload": "skipped",
                    "note": "LlamaManager indisponible dans ce contexte."}
        try:
            await llama.reload_model()
        except Exception as e:  # noqa: BLE001
            raise HTTPException(status_code=500,
                                detail=f"Échec du rechargement : {e}")
        return {"status": "switched", "model_id": payload.model_id,
                "size_bytes": target.stat().st_size}

    # ═════════════════════════════════════════════════════════════════════════
    # Conversations (persistance JSON dans data/conversations.json)
    # ═════════════════════════════════════════════════════════════════════════
    def _store() -> ConversationStore:
        # Instance par requête : lecture atomique, thread-safe via file lock.
        return ConversationStore(STUDIO_ROOT / "data" / "conversations.json")

    @router.get("/conversations", summary="Liste toutes les conversations (métadonnées)")
    async def list_conversations():
        return {"conversations": _store().list_summaries()}

    @router.post("/conversations", summary="Crée une nouvelle conversation vide")
    async def create_conversation(payload: ConversationCreatePayload):
        conv = _store().create(payload.title)
        return conv

    @router.get("/conversations/{conv_id}",
                summary="Récupère une conversation complète (avec messages)")
    async def get_conversation(conv_id: str):
        conv = _store().get(conv_id)
        if conv is None:
            raise HTTPException(status_code=404, detail="Conversation inconnue.")
        return conv

    @router.put("/conversations/{conv_id}",
                summary="Renomme une conversation")
    async def rename_conversation(conv_id: str, payload: ConversationRenamePayload):
        ok = _store().rename(conv_id, payload.title)
        if not ok:
            raise HTTPException(status_code=404, detail="Conversation inconnue.")
        return {"status": "renamed", "id": conv_id, "title": payload.title}

    @router.delete("/conversations/{conv_id}",
                   summary="Supprime définitivement une conversation")
    async def delete_conversation(conv_id: str):
        ok = _store().delete(conv_id)
        if not ok:
            raise HTTPException(status_code=404, detail="Conversation inconnue.")
        return {"status": "deleted", "id": conv_id}

    @router.post("/conversations/{conv_id}/messages",
                 summary="Ajoute un ou plusieurs messages à une conversation")
    async def append_messages(conv_id: str, payload: ConversationAppendPayload):
        conv = _store().append_messages(
            conv_id, [m.model_dump() for m in payload.messages]
        )
        if conv is None:
            raise HTTPException(status_code=404, detail="Conversation inconnue.")
        return {"status": "appended", "id": conv_id,
                "message_count": len(conv["messages"])}

    # ═════════════════════════════════════════════════════════════════════════
    # Trace SSE agentique
    # ═════════════════════════════════════════════════════════════════════════
    @router.get("/events", summary="Flux SSE des événements agentiques (lecture seule)")
    async def sse_events(request: Request, session_id: str):
        import json as _json
        from fastapi.responses import StreamingResponse

        async def _gen():
            async for evt in BUS.subscribe(session_id):
                if await request.is_disconnected():
                    break
                yield f"event: {evt['type']}\ndata: {_json.dumps(evt, ensure_ascii=False)}\n\n"
            yield "data: [DONE]\n\n"

        return StreamingResponse(_gen(), media_type="text/event-stream",
                                 headers={"Cache-Control": "no-cache",
                                          "X-Accel-Buffering": "no"})

    return router


def _source_of(settings, content: str) -> str:
    if settings.system_prompt.active_preset:
        return f"preset:{settings.system_prompt.active_preset}"
    if SYSTEM_PROMPT_FILE.exists():
        return "custom_file"
    return "default_file"


# ── Helpers parsing modèles ─────────────────────────────────────────────────

_QUANT_TOKENS = ("q2_k", "q3_k_s", "q3_k_m", "q3_k_l", "q4_0", "q4_k_s",
                 "q4_k_m", "q5_0", "q5_k_s", "q5_k_m", "q6_k", "q8_0", "f16", "f32")


def _parse_quant(fname: str) -> Optional[str]:
    """Détecte la quantification depuis le nom de fichier .gguf (best-effort)."""
    low = fname.lower()
    for tok in _QUANT_TOKENS:
        if tok in low:
            return tok.upper()
    return None


def _parse_params(fname: str) -> Optional[str]:
    """Devine la taille (nb paramètres) depuis le nom de fichier (best-effort)."""
    import re
    m = re.search(r"[-_](\d+(?:\.\d+)?)\s*b\b", fname.lower())
    if m:
        return f"{m.group(1)}B"
    return None
