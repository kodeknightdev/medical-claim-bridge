variable "namespace_name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "capacity" {
  type    = number
  default = 1
}
variable "topics" {
  description = "Kafka topics; each gets .retry and .dlq companions."
  type        = list(string)
}
variable "partitions" {
  type    = number
  default = 3
}
variable "tags" { type = map(string) }

locals {
  hubs = merge(
    { for t in var.topics : t => var.partitions },
    { for t in var.topics : "${t}.retry" => 1 },
    { for t in var.topics : "${t}.dlq" => 1 },
  )
}

# Standard tier exposes the Kafka endpoint on 9093, so the services keep using Confluent.Kafka unchanged
resource "azurerm_eventhub_namespace" "this" {
  name                          = var.namespace_name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku                           = "Standard"
  capacity                      = var.capacity
  minimum_tls_version           = "1.2"
  local_authentication_enabled  = false
  public_network_access_enabled = true
  tags                          = var.tags
}

resource "azurerm_eventhub" "topic" {
  for_each          = local.hubs
  name              = each.key
  namespace_id      = azurerm_eventhub_namespace.this.id
  partition_count   = each.value
  message_retention = endswith(each.key, ".dlq") ? 7 : 3
}

output "namespace_id" { value = azurerm_eventhub_namespace.this.id }
output "kafka_bootstrap" { value = "${azurerm_eventhub_namespace.this.name}.servicebus.windows.net:9093" }
