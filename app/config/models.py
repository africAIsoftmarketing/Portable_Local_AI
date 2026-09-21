"""
Rôle    : modèles Pydantic pour la validation stricte de config/settings.json.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

from typing import List, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field


class CorsConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    allow_origins: List[str] = Field(default_factory=lambda: ["http://127.0.0.1:8080",
                                                              "http://localhost:8080"])
    allow_credentials: bool = False
    allow_methods: List[str] = Field(default_factory=lambda: ["GET", "POST", "PUT",
                                                              "DELETE", "OPTIONS"])
    allow_headers: List[str] = Field(default_factory=lambda: ["Content-Type",
                                                              "Authorization"])


class RateLimitConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    enabled: bool = False
    requests_per_minute: int = 60


class ServerConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    # 127.0.0.1 par défaut ; passage à 0.0.0.0 doit être explicite (opt-in LAN).
    bind_host: str = "127.0.0.1"
    port: int = 8080
    # Port loopback strict pour llama-server (jamais exposé au LAN).
    llama_host: str = "127.0.0.1"
    llama_port: int = 8090
    cors: CorsConfig = Field(default_factory=CorsConfig)
    rate_limit: RateLimitConfig = Field(default_factory=RateLimitConfig)
    log_level: Literal["DEBUG", "INFO", "WARNING", "ERROR"] = "INFO"


class ModelConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    # null = auto-sélection (le plus gros .gguf de models/).
    path: Optional[str] = None
    context_length: int = 4096
    threads: Optional[int] = None  # null = n_cores - 1
    gpu_layers: Optional[int] = None  # null = auto (dépend backend)


class McpConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    enabled: bool = False  # Phase 3
    skills_dir: str = "skills"
    skill_timeout: int = 30
    skill_stderr_capture: bool = True
    auto_restart_on_crash: bool = True


class AgenticConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    # Ces paramètres seront exploités en Phase 3. Présents ici pour geler le schéma.
    max_tool_rounds: int = 5
    skill_timeout: int = 30
    total_timeout: int = 120
    allow_parallel_tools: bool = True
    tool_calling_mode: Literal["auto", "native", "fallback_json"] = "auto"
    on_tool_error: Literal["return_to_model", "abort", "retry_once"] = "return_to_model"
    expose_trace_to_ui: bool = True


class SystemPromptConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    active_preset: Optional[str] = None  # id du preset actif (ex. "cybersec")
    locked: bool = False  # si vrai : ni lecture ni écriture via API


class PlatformConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    backend: Literal["auto", "cpu", "cuda", "rocm", "vulkan", "metal"] = "auto"
    gpu_layers: Optional[int] = None
    force_binary_variant: Optional[str] = None


class LoggingConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    persistent: bool = False
    retention_days: int = 7
    startup_history: int = 5


class SecurityConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    require_api_key: bool = False  # défaut OFF (première utilisation friction-less)
    require_signature: bool = False  # dev : off + warning (voir docs/ARCHITECTURE.md §12.2)
    allowed_log_roots: List[str] = Field(default_factory=list)
    allowed_data_roots: List[str] = Field(default_factory=list)


class UiConfig(BaseModel):
    model_config = ConfigDict(extra="forbid")
    default_language: Literal["fr", "en"] = "fr"
    theme: Literal["auto", "light", "dark"] = "auto"


class Settings(BaseModel):
    """Racine du schéma de configuration."""
    model_config = ConfigDict(extra="forbid")
    server: ServerConfig = Field(default_factory=ServerConfig)
    model: ModelConfig = Field(default_factory=ModelConfig)
    mcp: McpConfig = Field(default_factory=McpConfig)
    agentic: AgenticConfig = Field(default_factory=AgenticConfig)
    system_prompt: SystemPromptConfig = Field(default_factory=SystemPromptConfig)
    platform: PlatformConfig = Field(default_factory=PlatformConfig)
    logging: LoggingConfig = Field(default_factory=LoggingConfig)
    security: SecurityConfig = Field(default_factory=SecurityConfig)
    ui: UiConfig = Field(default_factory=UiConfig)
