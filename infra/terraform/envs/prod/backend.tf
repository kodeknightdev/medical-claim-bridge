# Placeholder remote state. CI passes the real values with -backend-config; run locally with -backend=false.
terraform {
  backend "azurerm" {
    resource_group_name  = "rg-claim-bridge-tfstate"
    storage_account_name = "stclaimbridgetfstate"
    container_name       = "tfstate"
    key                  = "claim-bridge-prod.tfstate"
    use_azuread_auth     = true
  }
}
