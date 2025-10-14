from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="", env_file=".env", extra="ignore")

    database_url: str = "sqlite:///./analytics.db"
    kafka_bootstrap_servers: str = ""
    kafka_group: str = "analytics"
    blob_connection_string: str = "UseDevelopmentStorage=true"
    payers_gateway_url: str = "http://localhost:5105/"
    payer_health_poll_seconds: int = 30
    auth_issuer: str = "http://localhost:5000/"
    auth_authority: str = "http://localhost:5101/"
    auth_audience: str = "claim-bridge-api"
    auth_disabled: bool = False
    otel_exporter_otlp_endpoint: str = ""
    service_name: str = "analytics"
    background_workers: bool = True


@lru_cache
def get_settings() -> Settings:
    return Settings()
