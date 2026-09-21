"""
Rôle    : middleware d'authentification par clé API (optionnel).
          Activation via security.require_api_key=true dans settings.json.
          Header attendu : Authorization: Bearer <api_key>.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import hmac
import logging
import os

from fastapi import Request
from fastapi.responses import JSONResponse

logger = logging.getLogger("studio.auth")

# Routes toujours ouvertes (health/docs/UI) même en auth activée : permet à l'UI
# de récupérer la config avant d'envoyer le header, et à l'ingress de sonder.
API_PREFIX = os.environ.get("STUDIO_API_PREFIX", "").rstrip("/")


def _public_paths() -> set[str]:
    p = API_PREFIX
    base = {
        f"{p}/health",
        f"{p}/openapi.json",
        f"{p}/docs",
        f"{p}/",
    }
    # Racine sans prefix aussi (mode portable).
    base.update({"/", "/health", "/openapi.json", "/docs"})
    return base


def build_auth_middleware():
    """Retourne un callable middleware conforme à FastAPI/Starlette."""
    public = _public_paths()

    async def _mw(request: Request, call_next):
        settings = getattr(request.app.state, "settings", None)
        if settings is None or not settings.security.require_api_key:
            return await call_next(request)

        path = request.url.path
        # Assets statiques toujours ouverts.
        if path in public or path.startswith(f"{API_PREFIX}/assets/") \
                or path.startswith("/assets/"):
            return await call_next(request)

        expected = getattr(request.app.state, "api_key", None)
        if not expected:
            return JSONResponse(status_code=500,
                                content={"error": "api_key manquante côté serveur"})

        header = request.headers.get("authorization", "")
        if not header.lower().startswith("bearer "):
            return JSONResponse(status_code=401,
                                content={"error": "en-tête Authorization manquant"})
        provided = header.split(" ", 1)[1].strip()
        if not hmac.compare_digest(provided, expected):
            return JSONResponse(status_code=401,
                                content={"error": "api_key invalide"})
        return await call_next(request)

    return _mw
