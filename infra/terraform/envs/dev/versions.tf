terraform {
  required_version = ">= 1.11.0, < 1.14.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "= 4.40.0"
    }
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy = false
    }
  }
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "core"
}
