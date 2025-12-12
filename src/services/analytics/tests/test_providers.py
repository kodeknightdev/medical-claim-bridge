import pytest
from pydantic import ValidationError

from app.config import Settings, get_settings


def test_kafka_is_the_default_and_adds_no_sasl():
    conf = Settings(kafka_bootstrap_servers="kafka:9092").kafka_config(**{"group.id": "g"})
    assert conf == {"bootstrap.servers": "kafka:9092", "group.id": "g"}


def test_event_hubs_uses_sasl_plain_with_the_connection_string():
    conf = Settings(kafka_bootstrap_servers="cb.servicebus.windows.net:9093", messaging_provider="eventhubs",
                    eventhubs_connection_string="Endpoint=sb://cb.servicebus.windows.net/;SharedAccessKey=x").kafka_config()
    assert conf["security.protocol"] == "SASL_SSL"
    assert conf["sasl.username"] == "$ConnectionString"
    assert conf["sasl.password"].startswith("Endpoint=sb://")


@pytest.mark.parametrize("settings", [
    {"messaging_provider": "eventhubs"},
    {"messaging_provider": "rabbit"},
    {"storage_provider": "azure"},
    {"storage_provider": "nfs"},
])
def test_misconfigured_providers_fail_fast(settings):
    with pytest.raises(ValidationError):
        Settings(**settings)


def test_key_vault_secrets_come_from_the_mounted_files(tmp_path, monkeypatch):
    (tmp_path / "database_url").write_text("sqlite:///from-vault.db")
    monkeypatch.delenv("DATABASE_URL", raising=False)
    monkeypatch.setenv("SECRETS_PROVIDER", "keyvault")
    monkeypatch.setenv("SECRETS_MOUNT_PATH", str(tmp_path))
    get_settings.cache_clear()
    try:
        assert get_settings().database_url == "sqlite:///from-vault.db"
    finally:
        get_settings.cache_clear()
