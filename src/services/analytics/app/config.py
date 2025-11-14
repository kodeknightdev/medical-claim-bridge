from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="", env_file=".env", extra="ignore")

    database_url: str = "sqlite:///./analytics.db"
    kafka_bootstrap_servers: str = ""
    kafka_group: str = "analytics"
    # Azurite's published development account; real deployments inject a connection string
    blob_connection_string: str = (
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"
    )
    payers_gateway_url: str = "http://localhost:5105/"
    payer_health_poll_seconds: int = 30
    auth_issuer: str = "http://localhost:5080/"
    auth_authority: str = "http://localhost:5101/"
    auth_audience: str = "claim-bridge-api"
    auth_disabled: bool = False
    otel_exporter_otlp_endpoint: str = ""
    service_name: str = "analytics"
    background_workers: bool = True


@lru_cache
def get_settings() -> Settings:
    return Settings()
