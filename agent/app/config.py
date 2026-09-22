"""Service configuration. Every value is overridable by environment variable."""

from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="AGENT_", env_file=".env", extra="ignore")

    # ── The backend, which is this service's only door to data ──────────────
    api_base_url: str = "http://localhost:5000"
    # Shared secret for /internal/tools/*. The agent holds no database credentials at all:
    # everything it knows arrives through allow-listed HTTP tools (§9.3).
    api_key: str = Field(default="", repr=False)

    # ── LLM provider ────────────────────────────────────────────────────────
    # "ollama" locally and for the viva; "hosted" in the cloud, where no GPU exists.
    llm_provider: str = "ollama"
    ollama_base_url: str = "http://localhost:11434"
    llm_model: str = "qwen2.5:7b"
    # Low but not zero: near-deterministic output, which keeps a demo repeatable.
    llm_temperature: float = 0.1
    llm_num_predict: int = 700

    # ── Limits (§9.3). Exceeding any of these ends the run safely. ──────────
    tool_timeout_seconds: float = 20.0
    llm_timeout_seconds: float = 90.0
    run_timeout_seconds: float = 300.0
    tool_retries: int = 2
    schema_repair_attempts: int = 2
    max_revisions: int = 2

    # Farmer notes are untrusted input; anything longer is truncated before the model sees it.
    max_farmer_note_chars: int = 1000


@lru_cache
def get_settings() -> Settings:
    return Settings()
