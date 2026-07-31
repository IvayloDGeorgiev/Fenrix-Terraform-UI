using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

internal static partial class BuiltInTemplates
{
    static partial void AddMisc(List<ProjectTemplate> list)
    {
        // ── Kubernetes · deploy an app to an existing cluster (any provider) ────────────────────────────
        list.Add(T(
            Info("k8s-deploy", "Kubernetes · Deploy an app (any cluster)",
                "Deploys a containerized app (Namespace + Deployment + Service) to an existing Kubernetes cluster via your kubeconfig — works with EKS, AKS, GKE, k3s, or Docker Desktop. Sensible resource requests/limits and a configurable service type.",
                TemplateProvider.MultiCloud, TemplateCategory.Kubernetes, TemplateCostTier.Free,
                "$0 added — you only pay for the cluster you already run (free on local k3s / Docker Desktop).",
                ["kubernetes", "k8s", "deployment", "service", "any-cloud"],
                teardownHint: "terraform destroy removes the namespace, deployment and service."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    kubernetes = {
                      source  = "hashicorp/kubernetes"
                      version = "~> 2.30"
                    }
                  }
                }

                provider "kubernetes" {
                  config_path    = pathexpand(var.kubeconfig_path)
                  config_context = var.kube_context != "" ? var.kube_context : null
                }
                """),
            F("main.tf", """
                locals {
                  labels = { app = var.app_name }
                }

                resource "kubernetes_namespace" "app" {
                  metadata {
                    name = var.namespace
                  }
                }

                resource "kubernetes_deployment" "app" {
                  metadata {
                    name      = var.app_name
                    namespace = kubernetes_namespace.app.metadata[0].name
                    labels    = local.labels
                  }

                  spec {
                    replicas = var.replicas

                    selector {
                      match_labels = local.labels
                    }

                    template {
                      metadata {
                        labels = local.labels
                      }
                      spec {
                        container {
                          name  = var.app_name
                          image = var.image

                          port {
                            container_port = var.container_port
                          }

                          resources {
                            requests = {
                              cpu    = "100m"
                              memory = "128Mi"
                            }
                            limits = {
                              cpu    = "500m"
                              memory = "256Mi"
                            }
                          }
                        }
                      }
                    }
                  }
                }

                resource "kubernetes_service" "app" {
                  metadata {
                    name      = var.app_name
                    namespace = kubernetes_namespace.app.metadata[0].name
                  }

                  spec {
                    selector = local.labels
                    port {
                      port        = 80
                      target_port = var.container_port
                    }
                    type = var.service_type
                  }
                }
                """),
            F("variables.tf", """
                variable "kubeconfig_path" {
                  description = "Path to your kubeconfig."
                  type        = string
                  default     = "~/.kube/config"
                }

                variable "kube_context" {
                  description = "kubeconfig context to use (empty = current context)."
                  type        = string
                  default     = ""
                }

                variable "namespace" {
                  description = "Namespace to deploy into."
                  type        = string
                  default     = "demo"
                }

                variable "app_name" {
                  description = "App / deployment name."
                  type        = string
                  default     = "web"
                }

                variable "image" {
                  description = "Container image to run."
                  type        = string
                  default     = "nginxdemos/hello:latest"
                }

                variable "container_port" {
                  description = "Port the container listens on."
                  type        = number
                  default     = 80
                }

                variable "replicas" {
                  description = "Number of pod replicas."
                  type        = number
                  default     = 2
                }

                variable "service_type" {
                  description = "ClusterIP, NodePort, or LoadBalancer."
                  type        = string
                  default     = "ClusterIP"
                }
                """),
            F("outputs.tf", """
                output "namespace" {
                  value = kubernetes_namespace.app.metadata[0].name
                }

                output "service_name" {
                  value = kubernetes_service.app.metadata[0].name
                }
                """),
            F("terraform.tfvars", """
                app_name       = "web"
                namespace      = "demo"
                image          = "nginxdemos/hello:latest"
                container_port = 80
                replicas       = 2
                service_type   = "ClusterIP"
                """),
            F("README.md", """
                # Kubernetes · deploy an app

                Deploys a Namespace + Deployment + Service to whatever cluster your kubeconfig points at.

                ## Use
                1. Ensure `kubectl get nodes` works against your target cluster.
                2. Plan & apply.
                3. Reach it: `kubectl -n <namespace> port-forward svc/<service_name> 8080:80` (or use a
                   LoadBalancer service on a cloud cluster).
                """)));
    }
}
