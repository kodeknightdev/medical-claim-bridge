from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="", env_file=".env", extra="ignore")

    database_url: str = "sqlite:///./denial-predictor.db"
    model_dir: str = "./models"
    kafka_bootstrap_servers: str = ""
    # Azurite's published development account; real deployments inject a connection string
    blob_connection_string: str = (
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"
    )
    auth_issuer: str = "http://localhost:5000/"
    auth_authority: str = "http://localhost:5101/"
    auth_audience: str = "claim-bridge-api"
    auth_disabled: bool = False
    otel_exporter_otlp_endpoint: str = ""
    service_name: str = "denial-predictor"
    min_training_rows: int = 500


@lru_cache
def get_settings() -> Settings:
    return Settings()
