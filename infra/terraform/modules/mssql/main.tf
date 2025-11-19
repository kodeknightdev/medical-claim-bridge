variable "server_name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "aad_admin_login" { type = string }
variable "aad_admin_object_id" { type = string }
variable "databases" {
  description = "One database per service; services never share one."
  type = map(object({
    sku_name    = string
    max_size_gb = number
  }))
}
variable "tags" { type = map(string) }

resource "azurerm_mssql_server" "this" {
  name                          = var.server_name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  version                       = "12.0"
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true

  azuread_administrator {
    login_username              = var.aad_admin_login
    object_id                   = var.aad_admin_object_id
    azuread_authentication_only = true
  }

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}

resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "allow-azure-services"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_database" "service" {
  for_each    = var.databases
  name        = each.key
  server_id   = azurerm_mssql_server.this.id
  sku_name    = each.value.sku_name
  max_size_gb = each.value.max_size_gb
  # PHI at rest: transparent data encryption is on by default, the application also encrypts member ids and DOB
  transparent_data_encryption_enabled = true
  short_term_retention_policy {
    retention_days = 14
  }
  tags = var.tags
}

output "server_fqdn" { value = azurerm_mssql_server.this.fully_qualified_domain_name }
output "database_ids" { value = { for k, d in azurerm_mssql_database.service : k => d.id } }
