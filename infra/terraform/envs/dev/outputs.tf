output "resource_group" { value = azurerm_resource_group.this.name }
output "acr_login_server" { value = module.acr.login_server }
output "aks_oidc_issuer" { value = module.aks.oidc_issuer_url }
output "sql_server_fqdn" { value = module.mssql.server_fqdn }
output "redis_hostname" { value = module.redis.hostname }
output "kafka_bootstrap" { value = module.eventhubs.kafka_bootstrap }
output "key_vault_uri" { value = module.keyvault.uri }

# the values the Helm chart needs (keyVault.*, config.*), whether these resources came from here or elsewhere
output "key_vault_name" { value = module.keyvault.name }
output "key_vault_identity_client_id" { value = module.aks.secrets_provider_client_id }
