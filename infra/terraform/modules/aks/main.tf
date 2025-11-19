variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "kubernetes_version" {
  type    = string
  default = "1.31"
}
variable "node_vm_size" {
  type    = string
  default = "Standard_D4s_v5"
}
variable "node_count_min" {
  type    = number
  default = 2
}
variable "node_count_max" {
  type    = number
  default = 5
}
variable "log_analytics_workspace_id" { type = string }
variable "tenant_id" { type = string }
variable "admin_group_object_ids" {
  description = "Entra ID groups with cluster admin."
  type        = list(string)
}
variable "acr_id" { type = string }
variable "tags" { type = map(string) }

resource "azurerm_kubernetes_cluster" "this" {
  name                      = var.name
  location                  = var.location
  resource_group_name       = var.resource_group_name
  dns_prefix                = var.name
  kubernetes_version        = var.kubernetes_version
  sku_tier                  = "Standard"
  oidc_issuer_enabled       = true
  workload_identity_enabled = true
  local_account_disabled    = true
  azure_policy_enabled      = true

  default_node_pool {
    name                 = "system"
    vm_size              = var.node_vm_size
    auto_scaling_enabled = true
    min_count            = var.node_count_min
    max_count            = var.node_count_max
    os_disk_type         = "Ephemeral"
    zones                = ["1", "2", "3"]
    upgrade_settings {
      max_surge = "33%"
    }
  }

  identity {
    type = "SystemAssigned"
  }

  azure_active_directory_role_based_access_control {
    azure_rbac_enabled     = true
    tenant_id              = var.tenant_id
    admin_group_object_ids = var.admin_group_object_ids
  }

  # NetworkPolicies in the Helm chart need a policy engine
  network_profile {
    network_plugin    = "azure"
    network_policy    = "azure"
    load_balancer_sku = "standard"
  }

  oms_agent {
    log_analytics_workspace_id = var.log_analytics_workspace_id
  }

  key_vault_secrets_provider {
    secret_rotation_enabled = true
  }

  tags = var.tags
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                            = var.acr_id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
  skip_service_principal_aad_check = true
}

output "id" { value = azurerm_kubernetes_cluster.this.id }
output "oidc_issuer_url" { value = azurerm_kubernetes_cluster.this.oidc_issuer_url }
output "kubelet_object_id" { value = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id }
output "secrets_provider_object_id" {
  value = azurerm_kubernetes_cluster.this.key_vault_secrets_provider[0].secret_identity[0].object_id
}
