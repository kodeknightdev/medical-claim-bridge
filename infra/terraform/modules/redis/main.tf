variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "sku_name" {
  type    = string
  default = "Standard"
}
variable "capacity" {
  type    = number
  default = 1
}
variable "tags" { type = map(string) }

resource "azurerm_redis_cache" "this" {
  name                 = var.name
  location             = var.location
  resource_group_name  = var.resource_group_name
  capacity             = var.capacity
  family               = var.sku_name == "Premium" ? "P" : "C"
  sku_name             = var.sku_name
  non_ssl_port_enabled = false
  minimum_tls_version  = "1.2"
  redis_configuration {
    maxmemory_policy = "allkeys-lru"
  }
  tags = var.tags
}

output "hostname" { value = azurerm_redis_cache.this.hostname }
output "ssl_port" { value = azurerm_redis_cache.this.ssl_port }
output "id" { value = azurerm_redis_cache.this.id }
