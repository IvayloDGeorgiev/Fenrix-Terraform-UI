# 32 · Project templates (Phase 12)

Complete, cost-aware Terraform starters you pick when creating a project. Choosing a template prefills **every
environment's working directory** with real, ready-to-edit configuration — networking, security, and
compute/storage — so a new project has everything that project type needs on day one. Choose **Blank** for just
the recommended structure.

## Design

- **No database.** Templates are files/code. Built-in templates ship in `BuiltInTemplates` (Infrastructure);
  user templates live one-JSON-per-template under `<dataRoot>\Templates`. `IProjectTemplateService`
  (Application) serves both.
- **Applying a template** writes its files into `environments/<slug>/` for each environment, overwriting the
  blank scaffold. A file named `terraform.tfvars` is written as the environment's own `<slug>.tfvars`, so values
  load through the environment's var-file (Fenrix's per-environment model). A path-escape guard prevents writes
  outside the environment directory.
- **Management** (`/templates`): browse built-in + user templates with a provider filter, inspect every file,
  **create a template from an existing project** (captures one environment's `.tf`/`.tfvars`), and delete your
  own. Built-in templates can't be edited or deleted.
- **Selection** happens in the New Project dialog ("Start from"): Blank or a template, with a provider filter and
  cost badges.

## Cost philosophy

Templates favour real-world, cost-effective patterns over the most expensive vendor default:

- **No NAT gateway** where public subnets + strict security groups suffice (a NAT gateway is ~$32/mo each).
- **Graviton/ARM** sizes (t4g, arm64 Lambda) — cheaper per unit.
- **Scale-to-zero / serverless** for demos: Cloud Run, Azure Container Apps (Consumption), Lambda, Static Web Apps.
- **Cheapest managed options**: `db.t4g.micro` over Aurora for low-traffic DBs; CloudFront `PriceClass_100`;
  S3 + CloudFront + OAC for static sites; a single small VM + Docker over managed Kubernetes for small apps.
- Every template carries a **cost summary** and, for free/demo ones, a **teardown hint** (`terraform destroy`).

## Free-tier / demo templates

Several templates fit the always-free / free-trial tiers or scale to zero, so you can spin something up, try it,
and `terraform destroy` at ~$0: AWS serverless API (Lambda + DynamoDB), Azure Static Web App (Free), Azure
Container App (scale to zero), GCP Cloud Run (scale to zero), GCP e2-micro VM, and a local Docker template with
no cloud at all.

## Built-in catalog (20)

**AWS (8):** static site (S3 + CloudFront + OAC), serverless API (Lambda + DynamoDB, free tier), VM + Docker
(Graviton), VPC networking baseline (no NAT gateway), container web app (Fargate Spot + ALB), PostgreSQL (RDS
db.t4g.micro), remote-state backend (S3 + DynamoDB), scheduled task (Lambda + EventBridge, free tier).

**Azure (6):** Static Web App (Free), Container App (scale to zero), VM + Docker (B-series), PostgreSQL (Flexible
B1ms), Functions (Consumption, free grant), static site on Storage `$web`.

**GCP (4):** Cloud Run (scale to zero, free tier), VM + Docker (e2-micro, free tier), PostgreSQL (Cloud SQL
db-f1-micro), static website (Cloud Storage).

**Multi/local (2):** Kubernetes deploy to any cluster (kubeconfig), Local Docker (no cloud).

The catalog is code (`BuiltInTemplates.*`), so new entries are additive with no migration. These are authored
from best-practice research and are strong first drafts — `terraform validate`/`plan` them before production use,
since provider attribute names occasionally shift between versions.
