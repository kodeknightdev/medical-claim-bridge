variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "tenant_id" { type = string }
variable "secret_reader_object_ids" {
  description = "Identities (AKS secrets provider, workload identities) that read PHI keys and webhook secrets."
  type        = map(string)
}
variable "tags" { type = map(string) }

resource "azurerm_key_vault" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  tenant_id                     = var.tenant_id
  sku_name                      = "premium"
  enable_rbac_authorization     = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = true
  tags                          = var.tags
}

resource "azurerm_role_assignment" "secrets_user" {
  for_each             = var.secret_reader_object_ids
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value
}

# Wraps the PHI data key used by IPhiProtector; the unwrapped key never lands in config files
resource "azurerm_key_vault_key" "phi" {
  name         = "phi-master-key"
  key_vault_id = azurerm_key_vault.this.id
  key_type     = "RSA-HSM"
  key_size     = 3072
  key_opts     = ["wrapKey", "unwrapKey"]
  rotation_policy {
    automatic {
      time_before_expiry = "P30D"
    }
    expire_after         = "P1Y"
    notify_before_expiry = "P29D"
  }
}

output "id" { value = azurerm_key_vault.this.id }
output "uri" { value = azurerm_key_vault.this.vault_uri }
output "name" { value = azurerm_key_vault.this.name }
