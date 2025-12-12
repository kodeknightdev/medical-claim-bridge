import os
from functools import lru_cache

from pydantic import model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="", env_file=".env", extra="ignore")

    database_url: str = "sqlite:///./denial-predictor.db"
    model_dir: str = "./models"
    kafka_bootstrap_servers: str = ""
    # kafka (default) or eventhubs; Event Hubs takes its namespace connection string as the SASL password
    messaging_provider: str = "kafka"
    eventhubs_connection_string: str = ""
    # local (default, Azurite allowed) or azure (a real storage account is required)
    storage_provider: str = "local"
    # Azurite's published development account; real deployments inject a connection string
    blob_connection_string: str = (
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"
    )
    auth_issuer: str = "http://localhost:5080/"
    auth_authority: str = "http://localhost:5101/"
    auth_audience: str = "claim-bridge-api"
    auth_disabled: bool = False
    otel_exporter_otlp_endpoint: str = ""
    service_name: str = "denial-predictor"
    min_training_rows: int = 500

    @model_validator(mode="after")
    def _check_providers(self) -> "Settings":
        if self.messaging_provider not in ("kafka", "eventhubs"):
            raise ValueError(f"unknown MESSAGING_PROVIDER {self.messaging_provider!r}; use kafka or eventhubs")
        if self.messaging_provider == "eventhubs" and not self.eventhubs_connection_string:
            raise ValueError("MESSAGING_PROVIDER is eventhubs but EVENTHUBS_CONNECTION_STRING is empty")
        if self.storage_provider not in ("local", "azure"):
            raise ValueError(f"unknown STORAGE_PROVIDER {self.storage_provider!r}; use local or azure")
        if self.storage_provider == "azure" and "devstoreaccount1" in self.blob_connection_string:
            raise ValueError("STORAGE_PROVIDER is azure but BLOB_CONNECTION_STRING points at Azurite")
        return self

    def kafka_config(self, **extra) -> dict:
        conf = {"bootstrap.servers": self.kafka_bootstrap_servers, **extra}
        if self.messaging_provider == "eventhubs":
            conf.update({"security.protocol": "SASL_SSL", "sasl.mechanism": "PLAIN",
                         "sasl.username": "$ConnectionString", "sasl.password": self.eventhubs_connection_string})
        return conf


@lru_cache
def get_settings() -> Settings:
    # SECRETS_PROVIDER=keyvault reads one file per setting from the Key Vault CSI mount (e.g. database_url)
    provider = os.getenv("SECRETS_PROVIDER", "local").lower()
    if provider == "keyvault":
        path = os.getenv("SECRETS_MOUNT_PATH", "/mnt/secrets-store")
        if not os.path.isdir(path):
            raise RuntimeError(f"SECRETS_PROVIDER is keyvault but nothing is mounted at {path}")
        return Settings(_secrets_dir=path)
    if provider != "local":
        raise RuntimeError(f"unknown SECRETS_PROVIDER {provider!r}; use local or keyvault")
    return Settings()
