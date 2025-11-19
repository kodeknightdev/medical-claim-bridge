variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "retention_in_days" {
  type    = number
  default = 30
}
variable "tags" { type = map(string) }

resource "azurerm_log_analytics_workspace" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = var.retention_in_days
  tags                = var.tags
}

output "id" { value = azurerm_log_analytics_workspace.this.id }
output "workspace_id" { value = azurerm_log_analytics_workspace.this.workspace_id }
