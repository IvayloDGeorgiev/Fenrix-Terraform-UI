using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

internal static partial class BuiltInTemplates
{
    static partial void AddDocker(List<ProjectTemplate> list)
    {
        // ── Local Docker (no cloud, zero cost) ──────────────────────────────────────────────────────────
        list.Add(T(
            Info("docker-local", "Local · Docker container (no cloud)",
                "Runs a container on your own machine via the Docker provider — its own network, a persistent volume, and a published port. Zero cloud cost: perfect for learning Terraform and local dev.",
                TemplateProvider.Docker, TemplateCategory.Containers, TemplateCostTier.Free,
                "$0 — everything runs locally on Docker Desktop / Engine.",
                ["docker", "local", "dev", "no-cloud", "free"],
                teardownHint: "terraform destroy stops and removes the container, network and volume."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    docker = {
                      source  = "kreuzwerker/docker"
                      version = "~> 3.0"
                    }
                  }
                }

                # Talks to your local Docker Desktop / Engine.
                provider "docker" {}
                """),
            F("main.tf", """
                resource "docker_image" "app" {
                  name         = var.image
                  keep_locally = true
                }

                resource "docker_network" "app" {
                  name = "${var.project_name}-net"
                }

                resource "docker_volume" "data" {
                  name = "${var.project_name}-data"
                }

                resource "docker_container" "app" {
                  name    = var.project_name
                  image   = docker_image.app.image_id
                  restart = "unless-stopped"

                  networks_advanced {
                    name = docker_network.app.name
                  }

                  ports {
                    internal = var.container_port
                    external = var.host_port
                  }

                  volumes {
                    volume_name    = docker_volume.data.name
                    container_path = var.data_path
                  }
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Name for the container / network / volume."
                  type        = string
                  default     = "local-app"
                }

                variable "image" {
                  description = "Docker image to run."
                  type        = string
                  default     = "nginxdemos/hello:latest"
                }

                variable "container_port" {
                  description = "Port the app listens on inside the container."
                  type        = number
                  default     = 80
                }

                variable "host_port" {
                  description = "Port published on your machine."
                  type        = number
                  default     = 8080
                }

                variable "data_path" {
                  description = "Path inside the container to persist on the volume."
                  type        = string
                  default     = "/data"
                }
                """),
            F("outputs.tf", """
                output "url" {
                  description = "Open your app here."
                  value       = "http://localhost:${var.host_port}"
                }
                """),
            F("terraform.tfvars", """
                project_name   = "local-app"
                image          = "nginxdemos/hello:latest"
                container_port = 80
                host_port      = 8080
                """),
            F("README.md", """
                # Local Docker container

                Runs a container on your own machine through the Docker provider — no cloud account, no cost.
                Requires Docker Desktop (or Docker Engine) running locally.

                ## Use
                1. Make sure Docker is running.
                2. Plan & apply, then open the `url` output (http://localhost:8080).

                ## Cost
                $0 — it's all local. `terraform destroy` removes the container, network and volume.
                """)));
    }
}
