"""
Rôle    : routeur endpoints studio (system prompt, config).
          - GET/PUT /system-prompt   (respect du flag locked)
          - GET     /system-prompt/presets
          - POST    /system-prompt/reset
          - GET/PUT /config
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import logging
from typing import Any

from fastapi import APIRouter, HTTPException, Request
from pydantic import BaseModel, ConfigDict, Field, ValidationError

from app.config.loader import STUDIO_ROOT, load_settings, save_settings
from app.config.models import Settings
from app.system_prompt.presets import list_presets, load_preset
from app.system_prompt.resolver import (
    SYSTEM_PROMPT_FILE,
    DEFAULT_SYSTEM_PROMPT_FILE,
    approximate_token_count,
    read_current_prompt,
    write_prompt,
)

logger = logging.getLogger("studio.studio")


class SystemPromptPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    content: str = Field(..., min_length=0, max_length=200_000)


def build_studio_router() -> APIRouter:
    router = APIRouter()

    # ── System prompt ────────────────────────────────────────────────────────
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
        # Écrit config/system_prompt.txt et désactive le preset actif.
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
        # Suppression du fichier custom → fallback automatique sur le défaut.
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

    # ── Config ───────────────────────────────────────────────────────────────
    @router.get("/config", summary="Lit la configuration effective")
    async def get_config(request: Request):
        settings = request.app.state.settings
        return settings.model_dump(mode="json")

    @router.put("/config", summary="Remplace la configuration (validation stricte)")
    async def put_config(request: Request, body: dict[str, Any]):
        try:
            new_settings = Settings.model_validate(body)
        except ValidationError as e:
            raise HTTPException(status_code=422,
                                detail={"error": "validation", "issues": e.errors()})
        save_settings(new_settings)
        # Recharge et met à jour l'état.
        request.app.state.settings = load_settings(force_reload=True)
        return {"status": "saved", "requires_restart": [
            "server.bind_host", "server.port", "model.path", "platform.backend"
        ]}

    return router


def _source_of(settings, content: str) -> str:
    if settings.system_prompt.active_preset:
        return f"preset:{settings.system_prompt.active_preset}"
    if SYSTEM_PROMPT_FILE.exists():
        return "custom_file"
    return "default_file"
