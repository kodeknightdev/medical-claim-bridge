variable "subscription_id" {
  description = "Target subscription (placeholder in the example tfvars)."
  type        = string
}

variable "tenant_id" {
  type = string
}

variable "location" {
  type    = string
  default = "eastus2"
}

variable "environment" {
  type = string
}

variable "sql_admin_group_name" {
  description = "Entra ID group that administers Azure SQL."
  type        = string
}

variable "sql_admin_group_object_id" {
  type = string
}

variable "payer_sftp_public_keys" {
  description = "SSH public keys per payer folder on the SFTP storage account."
  type        = map(string)
}

variable "aks_node_count_max" {
  type    = number
  default = 4
}

variable "sql_sku" {
  type    = string
  default = "S1"
}

variable "aks_admin_group_object_id" {
  description = "Entra ID group with cluster admin rights."
  type        = string
}
