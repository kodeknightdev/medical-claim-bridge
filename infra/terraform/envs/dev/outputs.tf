output "resource_group" { value = azurerm_resource_group.this.name }
output "acr_login_server" { value = module.acr.login_server }
output "aks_oidc_issuer" { value = module.aks.oidc_issuer_url }
output "sql_server_fqdn" { value = module.mssql.server_fqdn }
output "redis_hostname" { value = module.redis.hostname }
output "kafka_bootstrap" { value = module.eventhubs.kafka_bootstrap }
output "key_vault_uri" { value = module.keyvault.uri }
