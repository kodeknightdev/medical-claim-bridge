variable "account_name" { type = string }
variable "sftp_account_name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "containers" {
  description = "Claim-check containers: batches, X12 archive, remittances and PDFs."
  type        = list(string)
}
variable "sftp_payers" {
  description = "Payer folders on the SFTP account, each with a local user (public key placeholder)."
  type = map(object({
    ssh_public_key = string
  }))
}
variable "tags" { type = map(string) }

resource "azurerm_storage_account" "data" {
  name                            = var.account_name
  location                        = var.location
  resource_group_name             = var.resource_group_name
  account_tier                    = "Standard"
  account_replication_type        = "ZRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  blob_properties {
    versioning_enabled = true
    delete_retention_policy {
      days = 30
    }
  }
  tags = var.tags
}

resource "azurerm_storage_container" "data" {
  for_each              = toset(var.containers)
  name                  = each.value
  storage_account_id    = azurerm_storage_account.data.id
  container_access_type = "private"
}

# SFTP needs hierarchical namespace; payers exchange 837/999/277CA/835 files here
resource "azurerm_storage_account" "sftp" {
  name                            = var.sftp_account_name
  location                        = var.location
  resource_group_name             = var.resource_group_name
  account_tier                    = "Standard"
  account_replication_type        = "ZRS"
  min_tls_version                 = "TLS1_2"
  is_hns_enabled                  = true
  sftp_enabled                    = true
  allow_nested_items_to_be_public = false
  tags                            = var.tags
}

resource "azurerm_storage_container" "payer" {
  for_each              = var.sftp_payers
  name                  = each.key
  storage_account_id    = azurerm_storage_account.sftp.id
  container_access_type = "private"
}

resource "azurerm_storage_account_local_user" "payer" {
  for_each             = var.sftp_payers
  name                 = replace(each.key, "-", "")
  storage_account_id   = azurerm_storage_account.sftp.id
  ssh_key_enabled      = true
  ssh_password_enabled = false
  home_directory       = each.key

  ssh_authorized_key {
    description = "${each.key} exchange key (placeholder)"
    key         = each.value.ssh_public_key
  }

  permission_scope {
    service       = "blob"
    resource_name = azurerm_storage_container.payer[each.key].name
    permissions {
      read   = true
      write  = true
      create = true
      list   = true
      delete = true
    }
  }
}

output "data_account_id" { value = azurerm_storage_account.data.id }
output "sftp_account_id" { value = azurerm_storage_account.sftp.id }
output "data_blob_endpoint" { value = azurerm_storage_account.data.primary_blob_endpoint }
