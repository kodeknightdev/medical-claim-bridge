locals {
  name  = "claim-bridge-${var.environment}"
  short = "cb${var.environment}"
  tags = {
    project             = "claim-bridge"
    environment         = var.environment
    data-classification = "phi"
    managed-by          = "terraform"
    cost-center         = "clearinghouse-demo"
  }

  databases = {
    for svc in ["identity", "providers", "claims", "scrubber", "payers", "remittance", "notifications", "denial_predictor", "analytics"] :
    "cb_${svc}" => { sku_name = var.sql_sku, max_size_gb = 32 }
  }

  topics = [
    "claims.batch-uploaded", "claims.claim-received", "claims.claim-scrubbed", "claims.claim-ready", "claims.status-changed",
    "payers.claim-submitted", "payers.ack-received", "payers.status-updated", "payers.remittance-received",
    "remittance.payment-posted", "remittance.claim-denied", "remittance.reversal-posted",
    "providers.enrollment-changed", "providers.webhook-subscription-changed", "denial-predictor.model-trained",
  ]
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.tags
}

module "log_analytics" {
  source              = "../../modules/log-analytics"
  name                = "log-${local.name}"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  retention_in_days   = var.environment == "prod" ? 90 : 30
  tags                = local.tags
}

module "acr" {
  source              = "../../modules/acr"
  name                = "acr${local.short}"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = var.environment == "prod" ? "Premium" : "Standard"
  tags                = local.tags
}

module "aks" {
  source                     = "../../modules/aks"
  name                       = "aks-${local.name}"
  location                   = var.location
  resource_group_name        = azurerm_resource_group.this.name
  node_count_max             = var.aks_node_count_max
  log_analytics_workspace_id = module.log_analytics.id
  acr_id                     = module.acr.id
  tenant_id                  = var.tenant_id
  admin_group_object_ids     = [var.aks_admin_group_object_id]
  tags                       = local.tags
}

module "mssql" {
  source              = "../../modules/mssql"
  server_name         = "sql-${local.name}"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  aad_admin_login     = var.sql_admin_group_name
  aad_admin_object_id = var.sql_admin_group_object_id
  databases           = local.databases
  tags                = local.tags
}

module "redis" {
  source              = "../../modules/redis"
  name                = "redis-${local.name}"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  sku_name            = var.environment == "prod" ? "Premium" : "Standard"
  tags                = local.tags
}

module "eventhubs" {
  source              = "../../modules/eventhubs"
  namespace_name      = "evh-${local.name}"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  capacity            = var.environment == "prod" ? 2 : 1
  topics              = local.topics
  tags                = local.tags
}

module "storage" {
  source              = "../../modules/storage"
  account_name        = "st${local.short}data"
  sftp_account_name   = "st${local.short}sftp"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  containers          = ["claim-batches", "x12-archive", "remittances", "remittance-pdfs", "appeal-letters"]
  sftp_payers         = { for payer, key in var.payer_sftp_public_keys : payer => { ssh_public_key = key } }
  tags                = local.tags
}

module "keyvault" {
  source              = "../../modules/keyvault"
  name                = "kv-${local.short}"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  tenant_id           = var.tenant_id
  secret_reader_object_ids = {
    aks-secrets-provider = module.aks.secrets_provider_object_id
  }
  tags = local.tags
}

# The cluster's workloads read and write claim-check blobs and use Event Hubs with Entra ID (no shared keys)
resource "azurerm_role_assignment" "blob_data" {
  scope                = module.storage.data_account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = module.aks.kubelet_object_id
}

resource "azurerm_role_assignment" "sftp_data" {
  scope                = module.storage.sftp_account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = module.aks.kubelet_object_id
}

resource "azurerm_role_assignment" "eventhubs" {
  for_each             = toset(["Azure Event Hubs Data Sender", "Azure Event Hubs Data Receiver"])
  scope                = module.eventhubs.namespace_id
  role_definition_name = each.value
  principal_id         = module.aks.kubelet_object_id
}
