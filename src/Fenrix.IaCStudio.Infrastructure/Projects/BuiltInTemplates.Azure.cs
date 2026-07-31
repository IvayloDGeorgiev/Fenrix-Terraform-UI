using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

internal static partial class BuiltInTemplates
{
    static partial void AddAzure(List<ProjectTemplate> list)
    {
        // ── Azure · Static Web App (Free tier) ──────────────────────────────────────────────────────────
        list.Add(T(
            Info("azure-static-web-app", "Azure · Static Web App (Free)",
                "Azure Static Web Apps on the Free SKU: global CDN, free HTTPS, and an optional managed functions API — all at no cost. Ideal for demos, docs sites and SPAs.",
                TemplateProvider.Azure, TemplateCategory.StaticSite, TemplateCostTier.Free,
                "Free SKU: $0. (Custom domains + SLA need the Standard SKU.)",
                ["static-web-app", "swa", "spa", "cdn", "free-tier"],
                teardownHint: "Free while it exists; terraform destroy removes it entirely."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    azurerm = {
                      source  = "hashicorp/azurerm"
                      version = "~> 4.0"
                    }
                  }
                }

                provider "azurerm" {
                  features {}
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "azurerm_resource_group" "rg" {
                  name     = "${var.project_name}-rg"
                  location = var.location
                  tags     = local.tags
                }

                resource "azurerm_static_web_app" "site" {
                  name                = var.project_name
                  resource_group_name = azurerm_resource_group.rg.name
                  location            = azurerm_resource_group.rg.location
                  sku_tier            = "Free"
                  sku_size            = "Free"
                  tags                = local.tags
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "location" {
                  description = "Azure region (Static Web Apps is available in a subset — e.g. westeurope, eastus2)."
                  type        = string
                  default     = "westeurope"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "site_url" {
                  description = "Public HTTPS URL of the site."
                  value       = "https://${azurerm_static_web_app.site.default_host_name}"
                }

                output "deployment_token" {
                  description = "Token for the SWA CLI / GitHub Action to publish content."
                  value       = azurerm_static_web_app.site.api_key
                  sensitive   = true
                }
                """),
            F("terraform.tfvars", """
                project_name = "my-site"
                location     = "westeurope"
                """),
            F("README.md", """
                # Azure Static Web App (Free)

                A Free-SKU Static Web App: global distribution and free HTTPS at no cost.

                ## Use
                1. Plan & apply.
                2. Publish content with the SWA CLI using the `deployment_token` output:
                   `swa deploy ./dist --deployment-token <token>`
                3. Open the `site_url` output.

                ## Cost
                $0 on the Free SKU. `terraform destroy` when done.
                """)));

        // ── Azure · Container App (Consumption, scale to zero) ──────────────────────────────────────────
        list.Add(T(
            Info("azure-container-app", "Azure · Container App (scale to zero)",
                "A containerized web app on Azure Container Apps (Consumption plan): public HTTPS ingress, min_replicas = 0 so it costs nothing when idle, auto-scales on traffic. ~40% cheaper per vCPU than Fargate.",
                TemplateProvider.Azure, TemplateCategory.Containers, TemplateCostTier.Low,
                "Consumption plan bills per request + active vCPU-seconds; ~$0 idle thanks to scale-to-zero. Generous monthly free grant.",
                ["container-apps", "aca", "web", "scale-to-zero"],
                teardownHint: "Scales to zero when idle; terraform destroy removes it fully."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    azurerm = {
                      source  = "hashicorp/azurerm"
                      version = "~> 4.0"
                    }
                  }
                }

                provider "azurerm" {
                  features {}
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "azurerm_resource_group" "rg" {
                  name     = "${var.project_name}-rg"
                  location = var.location
                  tags     = local.tags
                }

                resource "azurerm_log_analytics_workspace" "law" {
                  name                = "${var.project_name}-law"
                  resource_group_name = azurerm_resource_group.rg.name
                  location            = azurerm_resource_group.rg.location
                  sku                 = "PerGB2018"
                  retention_in_days   = 30
                  tags                = local.tags
                }

                resource "azurerm_container_app_environment" "env" {
                  name                       = "${var.project_name}-env"
                  resource_group_name        = azurerm_resource_group.rg.name
                  location                   = azurerm_resource_group.rg.location
                  log_analytics_workspace_id = azurerm_log_analytics_workspace.law.id
                  tags                       = local.tags
                }

                resource "azurerm_container_app" "app" {
                  name                         = var.project_name
                  resource_group_name          = azurerm_resource_group.rg.name
                  container_app_environment_id = azurerm_container_app_environment.env.id
                  revision_mode                = "Single"
                  tags                         = local.tags

                  template {
                    min_replicas = 0 # scale to zero: no cost when idle
                    max_replicas = 3

                    container {
                      name   = "app"
                      image  = var.container_image
                      cpu    = 0.25
                      memory = "0.5Gi"
                    }
                  }

                  ingress {
                    external_enabled = true
                    target_port      = var.container_port
                    traffic_weight {
                      latest_revision = true
                      percentage      = 100
                    }
                  }
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "location" {
                  description = "Azure region."
                  type        = string
                  default     = "westeurope"
                }

                variable "container_image" {
                  description = "Container image to run (public registry, or wire up ACR)."
                  type        = string
                  default     = "mcr.microsoft.com/k8se/quickstart:latest"
                }

                variable "container_port" {
                  description = "Port your container listens on."
                  type        = number
                  default     = 80
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "app_url" {
                  description = "Public HTTPS URL of the app."
                  value       = "https://${azurerm_container_app.app.ingress[0].fqdn}"
                }
                """),
            F("terraform.tfvars", """
                project_name    = "my-app"
                location        = "westeurope"
                container_image = "mcr.microsoft.com/k8se/quickstart:latest"
                container_port  = 80
                """),
            F("README.md", """
                # Azure Container App (scale to zero)

                A containerized web app on the Consumption plan. `min_replicas = 0` means it costs nothing when
                idle and scales up on traffic.

                ## Use
                1. Set `container_image` / `container_port`.
                2. Plan & apply, then open the `app_url` output.

                ## Cost
                ~$0 idle. Billed per request + active vCPU-seconds with a monthly free grant. `terraform destroy` when done.
                """)));

        // ── Azure · Linux VM + Docker (B-series) ────────────────────────────────────────────────────────
        list.Add(T(
            Info("azure-vm-docker", "Azure · VM + Docker (B-series)",
                "One burstable B1s Linux VM with its own VNet, NSG and public IP that installs Docker via cloud-init and runs your container. The cheapest way to host a full app on Azure.",
                TemplateProvider.Azure, TemplateCategory.VirtualMachine, TemplateCostTier.Low,
                "~$8–10/mo for a Standard_B1s + small managed disk. (B1s is free for 12 months on a new account.)",
                ["vm", "docker", "b-series", "cheap"]),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    azurerm = {
                      source  = "hashicorp/azurerm"
                      version = "~> 4.0"
                    }
                  }
                }

                provider "azurerm" {
                  features {}
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "azurerm_resource_group" "rg" {
                  name     = "${var.project_name}-rg"
                  location = var.location
                  tags     = local.tags
                }

                resource "azurerm_virtual_network" "vnet" {
                  name                = "${var.project_name}-vnet"
                  resource_group_name = azurerm_resource_group.rg.name
                  location            = azurerm_resource_group.rg.location
                  address_space       = ["10.10.0.0/16"]
                  tags                = local.tags
                }

                resource "azurerm_subnet" "subnet" {
                  name                 = "default"
                  resource_group_name  = azurerm_resource_group.rg.name
                  virtual_network_name = azurerm_virtual_network.vnet.name
                  address_prefixes     = ["10.10.1.0/24"]
                }

                resource "azurerm_public_ip" "pip" {
                  name                = "${var.project_name}-pip"
                  resource_group_name = azurerm_resource_group.rg.name
                  location            = azurerm_resource_group.rg.location
                  allocation_method   = "Static"
                  sku                 = "Standard"
                  tags                = local.tags
                }

                resource "azurerm_network_security_group" "nsg" {
                  name                = "${var.project_name}-nsg"
                  resource_group_name = azurerm_resource_group.rg.name
                  location            = azurerm_resource_group.rg.location
                  tags                = local.tags

                  security_rule {
                    name                       = "SSH"
                    priority                   = 100
                    direction                  = "Inbound"
                    access                     = "Allow"
                    protocol                   = "Tcp"
                    source_port_range          = "*"
                    destination_port_range     = "22"
                    source_address_prefix      = var.ssh_source
                    destination_address_prefix = "*"
                  }
                  security_rule {
                    name                       = "HTTP"
                    priority                   = 110
                    direction                  = "Inbound"
                    access                     = "Allow"
                    protocol                   = "Tcp"
                    source_port_range          = "*"
                    destination_port_range     = "80"
                    source_address_prefix      = "*"
                    destination_address_prefix = "*"
                  }
                }

                resource "azurerm_network_interface" "nic" {
                  name                = "${var.project_name}-nic"
                  resource_group_name = azurerm_resource_group.rg.name
                  location            = azurerm_resource_group.rg.location
                  tags                = local.tags

                  ip_configuration {
                    name                          = "internal"
                    subnet_id                     = azurerm_subnet.subnet.id
                    private_ip_address_allocation = "Dynamic"
                    public_ip_address_id          = azurerm_public_ip.pip.id
                  }
                }

                resource "azurerm_network_interface_security_group_association" "nic_nsg" {
                  network_interface_id      = azurerm_network_interface.nic.id
                  network_security_group_id = azurerm_network_security_group.nsg.id
                }

                resource "azurerm_linux_virtual_machine" "vm" {
                  name                  = var.project_name
                  resource_group_name   = azurerm_resource_group.rg.name
                  location              = azurerm_resource_group.rg.location
                  size                  = var.vm_size
                  admin_username        = var.admin_username
                  network_interface_ids = [azurerm_network_interface.nic.id]
                  tags                  = local.tags

                  admin_ssh_key {
                    username   = var.admin_username
                    public_key = var.ssh_public_key
                  }

                  os_disk {
                    caching              = "ReadWrite"
                    storage_account_type = "Standard_LRS"
                  }

                  source_image_reference {
                    publisher = "Canonical"
                    offer     = "0001-com-ubuntu-server-jammy"
                    sku       = "22_04-lts-gen2"
                    version   = "latest"
                  }

                  custom_data = base64encode(<<-EOT
                    #!/bin/bash
                    apt-get update
                    apt-get install -y docker.io
                    systemctl enable --now docker
                    docker run -d --restart always -p 80:80 ${var.container_image}
                  EOT
                  )
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "location" {
                  description = "Azure region."
                  type        = string
                  default     = "westeurope"
                }

                variable "vm_size" {
                  description = "VM size. Standard_B1s is the cheapest burstable option."
                  type        = string
                  default     = "Standard_B1s"
                }

                variable "admin_username" {
                  description = "Admin username for SSH."
                  type        = string
                  default     = "azureuser"
                }

                variable "ssh_public_key" {
                  description = "Your SSH public key (contents of ~/.ssh/id_ed25519.pub)."
                  type        = string
                }

                variable "ssh_source" {
                  description = "CIDR allowed to SSH. Lock to your IP/32 in production."
                  type        = string
                  default     = "*"
                }

                variable "container_image" {
                  description = "Docker image to run on port 80."
                  type        = string
                  default     = "nginxdemos/hello:latest"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "public_ip" {
                  value = azurerm_public_ip.pip.ip_address
                }

                output "url" {
                  value = "http://${azurerm_public_ip.pip.ip_address}"
                }
                """),
            F("terraform.tfvars", """
                project_name    = "my-app"
                location        = "westeurope"
                vm_size         = "Standard_B1s"
                container_image = "nginxdemos/hello:latest"
                # ssh_public_key = "ssh-ed25519 AAAA... you@host"
                # ssh_source     = "203.0.113.10/32"
                """),
            F("README.md", """
                # Azure VM + Docker

                A B1s Linux VM that installs Docker on boot and runs your container. Set `ssh_public_key`, then
                plan & apply and open the `url` output.
                """)));

        // ── Azure · PostgreSQL Flexible Server (Burstable B1ms) ─────────────────────────────────────────
        list.Add(T(
            Info("azure-postgres-flexible", "Azure · PostgreSQL (Flexible B1ms)",
                "A managed PostgreSQL Flexible Server on the cheapest burstable tier (B_Standard_B1ms) with a generated password and an initial database. Public endpoint with an 'allow Azure services' rule for easy app access.",
                TemplateProvider.Azure, TemplateCategory.Database, TemplateCostTier.Low,
                "~$12–15/mo for B1ms + 32GB. Stop the server when unused to pause compute charges.",
                ["postgres", "flexible-server", "database", "burstable"]),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    azurerm = {
                      source  = "hashicorp/azurerm"
                      version = "~> 4.0"
                    }
                    random = {
                      source  = "hashicorp/random"
                      version = "~> 3.6"
                    }
                  }
                }

                provider "azurerm" {
                  features {}
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "azurerm_resource_group" "rg" {
                  name     = "${var.project_name}-rg"
                  location = var.location
                  tags     = local.tags
                }

                resource "random_password" "db" {
                  length  = 24
                  special = false
                }

                resource "azurerm_postgresql_flexible_server" "db" {
                  name                          = "${var.project_name}-pg"
                  resource_group_name           = azurerm_resource_group.rg.name
                  location                      = azurerm_resource_group.rg.location
                  version                       = "16"
                  administrator_login           = var.admin_login
                  administrator_password        = random_password.db.result
                  sku_name                      = "B_Standard_B1ms" # cheapest burstable
                  storage_mb                    = 32768
                  auto_grow_enabled             = true
                  backup_retention_days         = 7
                  public_network_access_enabled = true
                  zone                          = "1"
                  tags                          = local.tags
                }

                resource "azurerm_postgresql_flexible_server_database" "app" {
                  name      = var.db_name
                  server_id = azurerm_postgresql_flexible_server.db.id
                  charset   = "UTF8"
                  collation = "en_US.utf8"
                }

                # Allow other Azure services (e.g. your Container App) to connect.
                resource "azurerm_postgresql_flexible_server_firewall_rule" "azure" {
                  name             = "allow-azure-services"
                  server_id        = azurerm_postgresql_flexible_server.db.id
                  start_ip_address = "0.0.0.0"
                  end_ip_address   = "0.0.0.0"
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "location" {
                  description = "Azure region."
                  type        = string
                  default     = "westeurope"
                }

                variable "admin_login" {
                  description = "Administrator login."
                  type        = string
                  default     = "pgadmin"
                }

                variable "db_name" {
                  description = "Initial database name."
                  type        = string
                  default     = "app"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "db_fqdn" {
                  value = azurerm_postgresql_flexible_server.db.fqdn
                }

                output "db_password" {
                  value     = random_password.db.result
                  sensitive = true
                }
                """),
            F("terraform.tfvars", """
                project_name = "my-db"
                location     = "westeurope"
                db_name      = "app"
                """),
            F("README.md", """
                # Azure PostgreSQL (Flexible B1ms)

                A cheap burstable managed Postgres. Read the password with `terraform output -raw db_password`.
                Stop the server in the portal when idle to pause compute billing; `terraform destroy` to remove.
                """)));

        // ── Azure · Functions (Consumption / serverless) ────────────────────────────────────────────────
        list.Add(T(
            Info("azure-functions", "Azure · Functions (Consumption)",
                "A serverless Azure Functions app on the Consumption (Y1) plan: scales to zero, generous monthly free grant, backed by a storage account. Deploy your function code separately (func / CI).",
                TemplateProvider.Azure, TemplateCategory.Serverless, TemplateCostTier.Free,
                "Consumption plan free grant: 1M executions + 400,000 GB-s/mo. ~$0 idle.",
                ["functions", "serverless", "consumption", "free-tier"],
                teardownHint: "Scales to zero; terraform destroy removes the app + storage."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    azurerm = {
                      source  = "hashicorp/azurerm"
                      version = "~> 4.0"
                    }
                    random = {
                      source  = "hashicorp/random"
                      version = "~> 3.6"
                    }
                  }
                }

                provider "azurerm" {
                  features {}
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "random_string" "sa" {
                  length  = 6
                  upper   = false
                  special = false
                }

                resource "azurerm_resource_group" "rg" {
                  name     = "${var.project_name}-rg"
                  location = var.location
                  tags     = local.tags
                }

                resource "azurerm_storage_account" "sa" {
                  name                     = "${substr(replace(var.project_name, "-", ""), 0, 16)}${random_string.sa.result}"
                  resource_group_name      = azurerm_resource_group.rg.name
                  location                 = azurerm_resource_group.rg.location
                  account_tier             = "Standard"
                  account_replication_type = "LRS"
                  tags                     = local.tags
                }

                resource "azurerm_service_plan" "plan" {
                  name                = "${var.project_name}-plan"
                  resource_group_name = azurerm_resource_group.rg.name
                  location            = azurerm_resource_group.rg.location
                  os_type             = "Linux"
                  sku_name            = "Y1" # Consumption
                  tags                = local.tags
                }

                resource "azurerm_linux_function_app" "func" {
                  name                       = "${var.project_name}-func-${random_string.sa.result}"
                  resource_group_name        = azurerm_resource_group.rg.name
                  location                   = azurerm_resource_group.rg.location
                  service_plan_id            = azurerm_service_plan.plan.id
                  storage_account_name       = azurerm_storage_account.sa.name
                  storage_account_access_key = azurerm_storage_account.sa.primary_access_key
                  tags                       = local.tags

                  site_config {
                    application_stack {
                      python_version = "3.11"
                    }
                  }
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources (letters/numbers work best for storage)."
                  type        = string
                }

                variable "location" {
                  description = "Azure region."
                  type        = string
                  default     = "westeurope"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "function_app_name" {
                  value = azurerm_linux_function_app.func.name
                }

                output "function_url" {
                  value = "https://${azurerm_linux_function_app.func.default_hostname}"
                }
                """),
            F("terraform.tfvars", """
                project_name = "myfuncs"
                location     = "westeurope"
                """),
            F("README.md", """
                # Azure Functions (Consumption)

                Serverless Functions on the Y1 plan (scales to zero). Deploy code with
                `func azure functionapp publish <function_app_name>`. Free grant covers 1M executions/mo.
                """)));

        // ── Azure · Static website on Storage ($web) ────────────────────────────────────────────────────
        list.Add(T(
            Info("azure-storage-static", "Azure · Static site (Storage $web)",
                "Serves a static site straight from a Storage account's $web container — the cheapest possible static hosting on Azure. Front it with Azure CDN / Front Door later for HTTPS on a custom domain.",
                TemplateProvider.Azure, TemplateCategory.StaticSite, TemplateCostTier.Free,
                "Storage + egress only — pennies at low traffic.",
                ["storage", "static", "web", "cheap"],
                teardownHint: "terraform destroy removes the storage account and content."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    azurerm = {
                      source  = "hashicorp/azurerm"
                      version = "~> 4.0"
                    }
                    random = {
                      source  = "hashicorp/random"
                      version = "~> 3.6"
                    }
                  }
                }

                provider "azurerm" {
                  features {}
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "random_string" "sa" {
                  length  = 6
                  upper   = false
                  special = false
                }

                resource "azurerm_resource_group" "rg" {
                  name     = "${var.project_name}-rg"
                  location = var.location
                  tags     = local.tags
                }

                resource "azurerm_storage_account" "sa" {
                  name                     = "${substr(replace(var.project_name, "-", ""), 0, 16)}${random_string.sa.result}"
                  resource_group_name      = azurerm_resource_group.rg.name
                  location                 = azurerm_resource_group.rg.location
                  account_tier             = "Standard"
                  account_replication_type = "LRS"
                  account_kind             = "StorageV2"
                  tags                     = local.tags
                }

                resource "azurerm_storage_account_static_website" "site" {
                  storage_account_id = azurerm_storage_account.sa.id
                  index_document     = "index.html"
                  error_404_document = "index.html" # SPA routing
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name (letters/numbers work best for the storage account)."
                  type        = string
                }

                variable "location" {
                  description = "Azure region."
                  type        = string
                  default     = "westeurope"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "site_url" {
                  description = "Static website endpoint (HTTP). Front with CDN/Front Door for HTTPS + custom domain."
                  value       = azurerm_storage_account.sa.primary_web_endpoint
                }

                output "storage_account_name" {
                  description = "Upload to the $web container of this account."
                  value       = azurerm_storage_account.sa.name
                }
                """),
            F("terraform.tfvars", """
                project_name = "mysite"
                location     = "westeurope"
                """),
            F("README.md", """
                # Azure static site (Storage $web)

                The cheapest static hosting on Azure. Upload your build to the account's `$web` container:
                `az storage blob upload-batch -s ./dist -d '$web' --account-name <storage_account_name>`.
                Open the `site_url` output.
                """)));
    }
}
