using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

internal static partial class BuiltInTemplates
{
    static partial void AddGcp(List<ProjectTemplate> list)
    {
        // ── GCP · Cloud Run (scale to zero, free-tier friendly) ─────────────────────────────────────────
        list.Add(T(
            Info("gcp-cloud-run", "GCP · Cloud Run (scale to zero)",
                "A containerized web app on Cloud Run v2: fully managed, public HTTPS, min instances = 0 so it scales to zero and costs nothing when idle. Generous always-free tier — the best value for containers.",
                TemplateProvider.Gcp, TemplateCategory.Containers, TemplateCostTier.Free,
                "Always-free: 2M requests + 360k GB-seconds/mo. ~$0 idle (scale to zero).",
                ["cloud-run", "containers", "serverless", "scale-to-zero", "free-tier"],
                teardownHint: "Costs nothing when idle; terraform destroy removes it."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    google = {
                      source  = "hashicorp/google"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "google" {
                  project = var.project_id
                  region  = var.region
                }
                """),
            F("main.tf", """
                locals {
                  labels = merge(var.labels, { project = var.project_name, managed-by = "terraform" })
                }

                # Enable the Cloud Run API (safe to leave enabled).
                resource "google_project_service" "run" {
                  service            = "run.googleapis.com"
                  disable_on_destroy = false
                }

                resource "google_cloud_run_v2_service" "app" {
                  name                = var.project_name
                  location            = var.region
                  deletion_protection = false
                  labels              = local.labels
                  depends_on          = [google_project_service.run]

                  template {
                    scaling {
                      min_instance_count = 0 # scale to zero
                      max_instance_count = 3
                    }
                    containers {
                      image = var.container_image
                      ports {
                        container_port = var.container_port
                      }
                      resources {
                        limits = {
                          cpu    = "1"
                          memory = "512Mi"
                        }
                      }
                    }
                  }
                }

                # Make the service publicly reachable.
                resource "google_cloud_run_v2_service_iam_member" "public" {
                  name     = google_cloud_run_v2_service.app.name
                  location = google_cloud_run_v2_service.app.location
                  role     = "roles/run.invoker"
                  member   = "allUsers"
                }
                """),
            F("variables.tf", """
                variable "project_id" {
                  description = "Your GCP project id."
                  type        = string
                }

                variable "project_name" {
                  description = "Short name used for the Cloud Run service."
                  type        = string
                }

                variable "region" {
                  description = "GCP region (us-central1 / us-east1 / us-west1 include the free tier)."
                  type        = string
                  default     = "us-central1"
                }

                variable "container_image" {
                  description = "Container image to run (e.g. from Artifact Registry or a public registry)."
                  type        = string
                  default     = "us-docker.pkg.dev/cloudrun/container/hello"
                }

                variable "container_port" {
                  description = "Port your container listens on."
                  type        = number
                  default     = 8080
                }

                variable "labels" {
                  description = "Extra labels applied to resources."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "app_url" {
                  description = "Public HTTPS URL of the service."
                  value       = google_cloud_run_v2_service.app.uri
                }
                """),
            F("terraform.tfvars", """
                project_id      = "my-gcp-project"
                project_name    = "my-app"
                region          = "us-central1"
                container_image = "us-docker.pkg.dev/cloudrun/container/hello"
                container_port  = 8080
                """),
            F("README.md", """
                # GCP Cloud Run (scale to zero)

                A managed container service that scales to zero when idle. The generous always-free tier makes it
                the cheapest way to host a container.

                ## Use
                1. Set `project_id` and `container_image` / `container_port`.
                2. Plan & apply, then open the `app_url` output.

                ## Cost
                ~$0 idle. Always-free covers 2M requests/mo. `terraform destroy` when done.
                """)));

        // ── GCP · Compute Engine e2-micro + Docker (free tier) ──────────────────────────────────────────
        list.Add(T(
            Info("gcp-vm-docker", "GCP · VM + Docker (free tier)",
                "One e2-micro Compute Engine VM (free tier in us-west1/us-central1/us-east1) that installs Docker on boot and runs your container, behind a minimal firewall. Great for always-on demos at ~$0.",
                TemplateProvider.Gcp, TemplateCategory.VirtualMachine, TemplateCostTier.Free,
                "Free tier: 1 e2-micro/mo in eligible US regions + 30GB disk. ~$0.",
                ["compute-engine", "gce", "docker", "vm", "free-tier"],
                teardownHint: "Free-tier e2-micro; terraform destroy removes it."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    google = {
                      source  = "hashicorp/google"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "google" {
                  project = var.project_id
                  region  = var.region
                  zone    = var.zone
                }
                """),
            F("main.tf", """
                resource "google_compute_firewall" "web" {
                  name    = "${var.project_name}-web"
                  network = "default"

                  allow {
                    protocol = "tcp"
                    ports    = ["22", "80"]
                  }
                  source_ranges = ["0.0.0.0/0"]
                  target_tags   = ["${var.project_name}-web"]
                }

                resource "google_compute_instance" "app" {
                  name         = var.project_name
                  machine_type = var.machine_type
                  zone         = var.zone
                  tags         = ["${var.project_name}-web"]
                  labels       = merge(var.labels, { managed-by = "terraform" })

                  boot_disk {
                    initialize_params {
                      image = "debian-cloud/debian-12"
                      size  = 30
                      type  = "pd-standard"
                    }
                  }

                  network_interface {
                    network = "default"
                    access_config {} # ephemeral public IP
                  }

                  metadata_startup_script = <<-EOT
                    #!/bin/bash
                    set -e
                    apt-get update
                    apt-get install -y docker.io
                    systemctl enable --now docker
                    docker run -d --restart always -p 80:80 ${var.container_image}
                  EOT
                }
                """),
            F("variables.tf", """
                variable "project_id" {
                  description = "Your GCP project id."
                  type        = string
                }

                variable "project_name" {
                  description = "Short name used to name resources."
                  type        = string
                }

                variable "region" {
                  description = "GCP region."
                  type        = string
                  default     = "us-central1"
                }

                variable "zone" {
                  description = "GCP zone (must be in a free-tier region for e2-micro)."
                  type        = string
                  default     = "us-central1-a"
                }

                variable "machine_type" {
                  description = "e2-micro is free-tier eligible in us-west1/us-central1/us-east1."
                  type        = string
                  default     = "e2-micro"
                }

                variable "container_image" {
                  description = "Docker image to run on port 80."
                  type        = string
                  default     = "nginxdemos/hello:latest"
                }

                variable "labels" {
                  description = "Extra labels applied to the VM."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "public_ip" {
                  description = "VM public IP."
                  value       = google_compute_instance.app.network_interface[0].access_config[0].nat_ip
                }

                output "url" {
                  description = "Open your app here."
                  value       = "http://${google_compute_instance.app.network_interface[0].access_config[0].nat_ip}"
                }
                """),
            F("terraform.tfvars", """
                project_id      = "my-gcp-project"
                project_name    = "my-app"
                region          = "us-central1"
                zone            = "us-central1-a"
                machine_type    = "e2-micro"
                container_image = "nginxdemos/hello:latest"
                """),
            F("README.md", """
                # GCP VM + Docker (free tier)

                One e2-micro VM that installs Docker on boot and runs your container. e2-micro is free-tier
                eligible in us-west1 / us-central1 / us-east1.

                ## Use
                1. Set `project_id` and `container_image`.
                2. Plan & apply, then open the `url` output.

                ## Cost
                ~$0 on the free tier. `terraform destroy` when done.
                """)));

        // ── GCP · Cloud SQL PostgreSQL (db-f1-micro) ────────────────────────────────────────────────────
        list.Add(T(
            Info("gcp-cloudsql-postgres", "GCP · PostgreSQL (Cloud SQL db-f1-micro)",
                "A managed Cloud SQL PostgreSQL on the cheapest shared-core tier (db-f1-micro) with a generated password and an app database. Public IP with authorized-network control for simple app access.",
                TemplateProvider.Gcp, TemplateCategory.Database, TemplateCostTier.Low,
                "~$8–10/mo for db-f1-micro + 10GB. Cheapest managed Postgres on GCP.",
                ["cloud-sql", "postgres", "database", "shared-core"]),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    google = {
                      source  = "hashicorp/google"
                      version = "~> 5.0"
                    }
                    random = {
                      source  = "hashicorp/random"
                      version = "~> 3.6"
                    }
                  }
                }

                provider "google" {
                  project = var.project_id
                  region  = var.region
                }
                """),
            F("main.tf", """
                resource "google_project_service" "sqladmin" {
                  service            = "sqladmin.googleapis.com"
                  disable_on_destroy = false
                }

                resource "random_password" "db" {
                  length  = 24
                  special = false
                }

                resource "google_sql_database_instance" "db" {
                  name                = "${var.project_name}-pg"
                  database_version    = "POSTGRES_16"
                  region              = var.region
                  deletion_protection = false
                  depends_on          = [google_project_service.sqladmin]

                  settings {
                    tier              = "db-f1-micro" # cheapest shared-core
                    availability_type = "ZONAL"       # single zone = cheapest
                    disk_size         = 10
                    disk_type         = "PD_HDD"

                    backup_configuration {
                      enabled = true
                    }

                    ip_configuration {
                      ipv4_enabled = true
                      dynamic "authorized_networks" {
                        for_each = var.authorized_networks
                        content {
                          name  = authorized_networks.key
                          value = authorized_networks.value
                        }
                      }
                    }
                  }
                }

                resource "google_sql_database" "app" {
                  name     = var.db_name
                  instance = google_sql_database_instance.db.name
                }

                resource "google_sql_user" "app" {
                  name     = var.db_user
                  instance = google_sql_database_instance.db.name
                  password = random_password.db.result
                }
                """),
            F("variables.tf", """
                variable "project_id" {
                  description = "Your GCP project id."
                  type        = string
                }

                variable "project_name" {
                  description = "Short name used to name resources."
                  type        = string
                }

                variable "region" {
                  description = "GCP region."
                  type        = string
                  default     = "us-central1"
                }

                variable "db_name" {
                  description = "Initial database name."
                  type        = string
                  default     = "app"
                }

                variable "db_user" {
                  description = "Application database user."
                  type        = string
                  default     = "app"
                }

                variable "authorized_networks" {
                  description = "Map of name => CIDR allowed to reach the public IP. Empty = none (use the Cloud SQL Auth Proxy)."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "db_connection_name" {
                  description = "Use with the Cloud SQL Auth Proxy."
                  value       = google_sql_database_instance.db.connection_name
                }

                output "db_public_ip" {
                  value = google_sql_database_instance.db.public_ip_address
                }

                output "db_password" {
                  value     = random_password.db.result
                  sensitive = true
                }
                """),
            F("terraform.tfvars", """
                project_id   = "my-gcp-project"
                project_name = "my-db"
                region       = "us-central1"
                db_name      = "app"
                # authorized_networks = { office = "203.0.113.0/24" }
                """),
            F("README.md", """
                # GCP PostgreSQL (Cloud SQL db-f1-micro)

                The cheapest managed Postgres on GCP. Connect via the Cloud SQL Auth Proxy using
                `db_connection_name`, or add your CIDR to `authorized_networks`. Read the password with
                `terraform output -raw db_password`.
                """)));

        // ── GCP · Static website (Cloud Storage bucket) ─────────────────────────────────────────────────
        list.Add(T(
            Info("gcp-static-bucket", "GCP · Static website (Cloud Storage)",
                "A public Cloud Storage bucket configured for website hosting (SPA-friendly: 404 → index.html). The cheapest way to serve a static site on GCP. Add a load balancer later for HTTPS + a custom domain.",
                TemplateProvider.Gcp, TemplateCategory.StaticSite, TemplateCostTier.Free,
                "Storage + egress only — pennies at low traffic; fits the free tier.",
                ["storage", "static", "website", "spa", "free-tier"],
                teardownHint: "terraform destroy removes the bucket and its contents (force_destroy)."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    google = {
                      source  = "hashicorp/google"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "google" {
                  project = var.project_id
                  region  = var.region
                }
                """),
            F("main.tf", """
                resource "google_storage_bucket" "site" {
                  name                        = var.bucket_name
                  location                    = var.location
                  force_destroy               = true
                  uniform_bucket_level_access = true

                  website {
                    main_page_suffix = "index.html"
                    not_found_page   = "index.html" # SPA routing
                  }
                }

                # Public read access for website hosting.
                resource "google_storage_bucket_iam_member" "public" {
                  bucket = google_storage_bucket.site.name
                  role   = "roles/storage.objectViewer"
                  member = "allUsers"
                }
                """),
            F("variables.tf", """
                variable "project_id" {
                  description = "Your GCP project id."
                  type        = string
                }

                variable "bucket_name" {
                  description = "Globally-unique bucket name (often your domain)."
                  type        = string
                }

                variable "location" {
                  description = "Bucket location (e.g. US, EU, or a region)."
                  type        = string
                  default     = "US"
                }

                variable "region" {
                  description = "Provider region."
                  type        = string
                  default     = "us-central1"
                }
                """),
            F("outputs.tf", """
                output "bucket_name" {
                  description = "Upload here: gsutil -m rsync -r ./dist gs://<bucket>"
                  value       = google_storage_bucket.site.name
                }

                output "website_url" {
                  description = "Direct bucket website URL (HTTP). Front with a load balancer for HTTPS + custom domain."
                  value       = "https://storage.googleapis.com/${google_storage_bucket.site.name}/index.html"
                }
                """),
            F("terraform.tfvars", """
                project_id  = "my-gcp-project"
                bucket_name = "my-unique-site-bucket"
                location    = "US"
                """),
            F("README.md", """
                # GCP static website (Cloud Storage)

                A public bucket set up for website hosting. Upload your build with
                `gsutil -m rsync -r ./dist gs://<bucket_name>`. For HTTPS + a custom domain, put an HTTPS load
                balancer in front later.
                """)));
    }
}
