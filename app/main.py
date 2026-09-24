"""
Rôle    : point d'entrée FastAPI - orchestrateur AfricAIsoft Portable Studio.
          Assemble : configuration, détection plateforme, gestion llama-server,
          middlewares (CORS + auth optionnelle), routeurs OpenAI-compat et studio,
          service UI statique.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
import logging
import os
import signal
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

from app.api.auth import build_auth_middleware
from app.api.router_studio import build_studio_router
from app.api.router_v1 import build_v1_router
from app.config.loader import STUDIO_ROOT, load_settings
from app.llama_manager.manager import LlamaManager
from app.logging_setup.setup import configure_logging
from app.mcp.registry import MCPRegistry
from app.platform_utils.backend_detector import detect_backend
from app.platform_utils.detect import detect_platform
from app.security.api_key import ensure_api_key
from app.security.manifest_verifier import verify_manifest

logger = logging.getLogger("studio.main")

# Préfixe global des routes : vide en mode portable (production),
# "/api" dans l'environnement de développement Emergent (ingress Kubernetes).
API_PREFIX = os.environ.get("STUDIO_API_PREFIX", "").rstrip("/")


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Cycle de vie : chargement config → détection → boot llama-server → arrêt propre."""
    # ── Startup ───────────────────────────────────────────────────────────────
    configure_logging()
    logger.info("=== AfricAIsoft Portable Studio - démarrage ===")

    settings = load_settings()
    app.state.settings = settings
    logger.info("Configuration chargée (bind=%s:%s)",
                settings.server.bind_host, settings.server.port)

    # Vérification manifest (non bloquant en dev)
    manifest_ok, manifest_msg = verify_manifest(
        STUDIO_ROOT / "release.json",
        require_signature=settings.security.require_signature,
    )
    if not manifest_ok:
        logger.warning("Manifest : %s", manifest_msg)
    app.state.manifest_warning = None if manifest_ok else manifest_msg

    # Détection plateforme + backend GPU
    app.state.platform = detect_platform()
    logger.info("Plateforme : %s", app.state.platform)
    app.state.backend = detect_backend(app.state.platform, settings)
    logger.info("Backend sélectionné : %s (raison=%s)",
                app.state.backend["backend"], app.state.backend["reason"])

    # Clé API (générée au 1er lancement, jamais loguée)
    app.state.api_key = ensure_api_key(STUDIO_ROOT / "config" / "api_key.txt")
    logger.info("API key : présente (fingerprint=%s...)", app.state.api_key[:6])

    # llama-server (peut être désactivé via env pour tests unitaires)
    manager = LlamaManager(
        settings=settings,
        platform=app.state.platform,
        backend=app.state.backend,
        studio_root=STUDIO_ROOT,
    )
    app.state.llama = manager
    if os.environ.get("STUDIO_SKIP_LLAMA_BOOT", "0") != "1":
        try:
            await manager.start()
            logger.info("llama-server prêt sur %s:%d",
                        manager.host, manager.port)
        except Exception as e:  # noqa: BLE001
            logger.error("llama-server KO : %s", e)
            # On continue quand même : /health signalera l'état dégradé.

    # Registre MCP - découverte + spawn des skills après llama.
    registry = MCPRegistry()
    app.state.mcp = registry
    if settings.mcp.enabled and os.environ.get("STUDIO_SKIP_MCP", "0") != "1":
        try:
            report = await registry.start_all()
            logger.info("MCP : %d skill(s) spawned, %d KO",
                        len(report.get("spawned", [])),
                        len(report.get("failed", [])))
        except Exception as e:  # noqa: BLE001
            logger.error("MCP registry startup failed: %s", e)

    # Handler signaux explicite (uvicorn le fait déjà mais on sécurise)
    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            signal.signal(sig, lambda *_: None)  # uvicorn gère via lifespan
        except (ValueError, OSError):
            pass  # main thread only, ignore in workers

    logger.info("=== Prêt ===")
    yield

    # ── Shutdown ──────────────────────────────────────────────────────────────
    logger.info("=== Arrêt en cours ===")
    if getattr(app.state, "mcp", None):
        try:
            await app.state.mcp.stop_all()
        except Exception as e:  # noqa: BLE001
            logger.warning("Arrêt MCP : %s", e)
    if getattr(app.state, "llama", None):
        try:
            await app.state.llama.stop()
        except Exception as e:  # noqa: BLE001
            logger.warning("Arrêt llama-server : %s", e)
    logger.info("=== Arrêt propre ===")


def create_app() -> FastAPI:
    """Fabrique l'application FastAPI et enregistre routeurs/middlewares/UI."""
    app = FastAPI(
        title="AfricAIsoft Portable Studio",
        description="Distribution portable USB 100% offline — API OpenAI-compatible + skills MCP + UI FR/EN.",
        version="1.0.3",
        lifespan=lifespan,
        openapi_url=f"{API_PREFIX}/openapi.json" if API_PREFIX else "/openapi.json",
        docs_url=f"{API_PREFIX}/docs" if API_PREFIX else "/docs",
        redoc_url=None,
    )

    # CORS (paramétrable par config/settings.json)
    # On charge une première fois pour appliquer les origines dès la construction.
    _settings = load_settings()
    app.add_middleware(
        CORSMiddleware,
        allow_origins=_settings.server.cors.allow_origins,
        allow_credentials=_settings.server.cors.allow_credentials,
        allow_methods=_settings.server.cors.allow_methods,
        allow_headers=_settings.server.cors.allow_headers,
    )

    # Auth optionnelle
    app.middleware("http")(build_auth_middleware())

    # ─── Health (inline, référence app.state) ─────────────────────────────────
    @app.get(f"{API_PREFIX}/health", tags=["studio"], summary="État global du studio")
    async def health(request: Request):
        st = request.app.state
        settings = st.settings
        llama_status = (
            await st.llama.health() if getattr(st, "llama", None)
            else {"status": "disabled"}
        )
        return {
            "status": "ok",
            "version": "1.0.3",
            "platform": st.platform,
            "backend": st.backend,
            "components": {
                "llama": llama_status,
                "mcp": (st.mcp.status_summary() if getattr(st, "mcp", None)
                        else {"status": "disabled", "reason": "not initialized"}),
                "api": {"status": "ok",
                        "auth_enabled": settings.security.require_api_key},
            },
            "warnings": [w for w in [getattr(st, "manifest_warning", None)] if w],
            "api_prefix": API_PREFIX or "/",
        }

    # ─── Routeurs métier ──────────────────────────────────────────────────────
    app.include_router(build_v1_router(), prefix=f"{API_PREFIX}/v1", tags=["openai"])
    app.include_router(build_studio_router(), prefix=API_PREFIX, tags=["studio"])

    # ─── UI statique ──────────────────────────────────────────────────────────
    ui_dir = STUDIO_ROOT / "ui"
    if (ui_dir / "assets").exists():
        # Cache-Control no-cache : force le navigateur à revalider les assets
        # à chaque F5 (essentiel pour app.js / i18n / styles). Sans ça,
        # une ancienne version bogguée cachée empêche les correctifs de prendre.
        class _NoCacheStaticFiles(StaticFiles):
            async def get_response(self, path: str, scope):  # noqa: D401
                resp = await super().get_response(path, scope)
                resp.headers["Cache-Control"] = "no-cache, must-revalidate"
                return resp
        app.mount(
            f"{API_PREFIX}/assets" if API_PREFIX else "/assets",
            _NoCacheStaticFiles(directory=str(ui_dir / "assets")),
            name="assets",
        )

    @app.get(f"{API_PREFIX}/" if API_PREFIX else "/", include_in_schema=False)
    async def _root():
        idx = ui_dir / "index.html"
        if idx.exists():
            resp = FileResponse(str(idx), media_type="text/html")
            # Idem : l'index doit toujours être revérifié pour que les
            # cache-busters ?v=… sur les scripts prennent effet.
            resp.headers["Cache-Control"] = "no-cache, must-revalidate"
            return resp
        return JSONResponse({
            "message": "AfricAIsoft Portable Studio",
            "docs": f"{API_PREFIX}/docs" if API_PREFIX else "/docs",
            "health": f"{API_PREFIX}/health" if API_PREFIX else "/health",
        })

    return app


# Objet ASGI exporté (utilisé par uvicorn et par backend/server.py en dev).
app = create_app()
