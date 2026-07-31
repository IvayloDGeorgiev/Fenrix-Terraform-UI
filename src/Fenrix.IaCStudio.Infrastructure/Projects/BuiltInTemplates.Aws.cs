using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

internal static partial class BuiltInTemplates
{
    static partial void AddAws(List<ProjectTemplate> list)
    {
        // ── AWS · static site (S3 private + CloudFront + OAC, SPA-aware) ────────────────────────────────
        list.Add(T(
            Info("aws-static-site", "AWS · Static website (S3 + CloudFront)",
                "Private S3 bucket served only through CloudFront via Origin Access Control (SigV4), HTTPS by default, SPA routing (403/404 → index.html), cheapest edge price class. Near-free for low traffic.",
                TemplateProvider.Aws, TemplateCategory.StaticSite, TemplateCostTier.Low,
                "~$0.50–2/mo at low traffic (S3 + CloudFront requests; no fixed compute).",
                ["static", "s3", "cloudfront", "spa", "cdn"],
                teardownHint: "terraform destroy removes the bucket + distribution — no lingering cost."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                # Private bucket — no public access; only CloudFront may read it.
                resource "aws_s3_bucket" "site" {
                  bucket        = "${var.project_name}-site"
                  force_destroy = true
                  tags          = local.tags
                }

                resource "aws_s3_bucket_public_access_block" "site" {
                  bucket                  = aws_s3_bucket.site.id
                  block_public_acls       = true
                  block_public_policy     = true
                  ignore_public_acls      = true
                  restrict_public_buckets = true
                }

                resource "aws_cloudfront_origin_access_control" "site" {
                  name                              = "${var.project_name}-oac"
                  origin_access_control_origin_type = "s3"
                  signing_behavior                  = "always"
                  signing_protocol                  = "sigv4"
                }

                resource "aws_cloudfront_distribution" "site" {
                  enabled             = true
                  default_root_object = "index.html"
                  comment             = "${var.project_name} static site"
                  price_class         = "PriceClass_100" # cheapest: NA + EU edges only
                  tags                = local.tags

                  origin {
                    domain_name              = aws_s3_bucket.site.bucket_regional_domain_name
                    origin_id                = "s3-site"
                    origin_access_control_id = aws_cloudfront_origin_access_control.site.id
                  }

                  default_cache_behavior {
                    target_origin_id       = "s3-site"
                    viewer_protocol_policy = "redirect-to-https"
                    allowed_methods        = ["GET", "HEAD"]
                    cached_methods         = ["GET", "HEAD"]
                    compress               = true
                    cache_policy_id        = "658327ea-f89d-4fab-a63d-7e88639e58f6" # AWS managed CachingOptimized
                  }

                  # Single-page-app routing: serve index.html for client-side routes / missing keys.
                  custom_error_response {
                    error_code            = 403
                    response_code         = 200
                    response_page_path    = "/index.html"
                    error_caching_min_ttl = 10
                  }
                  custom_error_response {
                    error_code            = 404
                    response_code         = 200
                    response_page_path    = "/index.html"
                    error_caching_min_ttl = 10
                  }

                  restrictions {
                    geo_restriction {
                      restriction_type = "none"
                    }
                  }

                  viewer_certificate {
                    cloudfront_default_certificate = true
                  }
                }

                # Bucket policy: allow only this distribution to GetObject.
                data "aws_iam_policy_document" "site" {
                  statement {
                    actions   = ["s3:GetObject"]
                    resources = ["${aws_s3_bucket.site.arn}/*"]
                    principals {
                      type        = "Service"
                      identifiers = ["cloudfront.amazonaws.com"]
                    }
                    condition {
                      test     = "StringEquals"
                      variable = "AWS:SourceArn"
                      values   = [aws_cloudfront_distribution.site.arn]
                    }
                  }
                }

                resource "aws_s3_bucket_policy" "site" {
                  bucket = aws_s3_bucket.site.id
                  policy = data.aws_iam_policy_document.site.json
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short, globally-unique-ish name used to prefix resources (e.g. the S3 bucket)."
                  type        = string
                }

                variable "region" {
                  description = "AWS region for the bucket."
                  type        = string
                  default     = "us-east-1"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "bucket_name" {
                  description = "Upload your built site here: aws s3 sync ./dist s3://<bucket>"
                  value       = aws_s3_bucket.site.bucket
                }

                output "site_url" {
                  description = "Public HTTPS URL."
                  value       = "https://${aws_cloudfront_distribution.site.domain_name}"
                }
                """),
            F("terraform.tfvars", """
                project_name = "my-site"
                region       = "us-east-1"
                """),
            F("README.md", """
                # AWS static website (S3 + CloudFront)

                A private S3 bucket fronted by CloudFront using Origin Access Control — the bucket is never public.
                SPA routing sends 403/404 to `index.html` so client-side routers work.

                ## Use
                1. `terraform init` then plan & apply.
                2. Upload your built site: `aws s3 sync ./dist s3://<bucket_name>`.
                3. Open the `site_url` output.

                ## Cost
                No fixed compute — you pay per request/GB. Typically well under $2/mo at low traffic.
                Tear down with `terraform destroy`.
                """)));

        // ── AWS · serverless API (Lambda + HTTP API + DynamoDB) — free-tier friendly ─────────────────────
        list.Add(T(
            Info("aws-serverless-api", "AWS · Serverless API (Lambda + DynamoDB)",
                "HTTP API Gateway → Graviton (arm64) Lambda → DynamoDB on-demand. No servers, no idle cost, scales to zero. Fits the AWS free tier for demos and dev.",
                TemplateProvider.Aws, TemplateCategory.Serverless, TemplateCostTier.Free,
                "Free tier: 1M Lambda + 1M HTTP API requests/mo, 25GB DynamoDB on-demand. ~$0 idle.",
                ["serverless", "lambda", "dynamodb", "api-gateway", "free-tier"],
                teardownHint: "Nothing runs when idle; terraform destroy leaves zero cost."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                    archive = {
                      source  = "hashicorp/archive"
                      version = "~> 2.4"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                locals {
                  name = var.project_name
                  tags = merge(var.tags, { Project = local.name, ManagedBy = "terraform" })
                }

                # On-demand table: pay per request, no idle cost, generous free tier.
                resource "aws_dynamodb_table" "items" {
                  name         = "${local.name}-items"
                  billing_mode = "PAY_PER_REQUEST"
                  hash_key     = "id"
                  attribute {
                    name = "id"
                    type = "S"
                  }
                  tags = local.tags
                }

                data "archive_file" "lambda" {
                  type        = "zip"
                  source_file = "${path.module}/src/handler.py"
                  output_path = "${path.module}/build/handler.zip"
                }

                resource "aws_iam_role" "lambda" {
                  name = "${local.name}-lambda"
                  assume_role_policy = jsonencode({
                    Version = "2012-10-17"
                    Statement = [{
                      Action    = "sts:AssumeRole"
                      Effect    = "Allow"
                      Principal = { Service = "lambda.amazonaws.com" }
                    }]
                  })
                  tags = local.tags
                }

                resource "aws_iam_role_policy_attachment" "logs" {
                  role       = aws_iam_role.lambda.name
                  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
                }

                resource "aws_iam_role_policy" "ddb" {
                  name = "${local.name}-ddb"
                  role = aws_iam_role.lambda.id
                  policy = jsonencode({
                    Version = "2012-10-17"
                    Statement = [{
                      Effect   = "Allow"
                      Action   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query", "dynamodb:Scan", "dynamodb:UpdateItem", "dynamodb:DeleteItem"]
                      Resource = aws_dynamodb_table.items.arn
                    }]
                  })
                }

                resource "aws_lambda_function" "api" {
                  function_name    = "${local.name}-api"
                  role             = aws_iam_role.lambda.arn
                  runtime          = "python3.12"
                  handler          = "handler.handler"
                  filename         = data.archive_file.lambda.output_path
                  source_code_hash = data.archive_file.lambda.output_base64sha256
                  architectures    = ["arm64"] # Graviton: cheaper per ms
                  timeout          = 10
                  memory_size      = 128
                  environment {
                    variables = { TABLE_NAME = aws_dynamodb_table.items.name }
                  }
                  tags = local.tags
                }

                resource "aws_apigatewayv2_api" "http" {
                  name          = "${local.name}-http"
                  protocol_type = "HTTP"
                  tags          = local.tags
                }

                resource "aws_apigatewayv2_integration" "lambda" {
                  api_id                 = aws_apigatewayv2_api.http.id
                  integration_type       = "AWS_PROXY"
                  integration_uri        = aws_lambda_function.api.invoke_arn
                  payload_format_version = "2.0"
                }

                resource "aws_apigatewayv2_route" "default" {
                  api_id    = aws_apigatewayv2_api.http.id
                  route_key = "$default"
                  target    = "integrations/${aws_apigatewayv2_integration.lambda.id}"
                }

                resource "aws_apigatewayv2_stage" "default" {
                  api_id      = aws_apigatewayv2_api.http.id
                  name        = "$default"
                  auto_deploy = true
                }

                resource "aws_lambda_permission" "apigw" {
                  statement_id  = "AllowAPIGatewayInvoke"
                  action        = "lambda:InvokeFunction"
                  function_name = aws_lambda_function.api.function_name
                  principal     = "apigateway.amazonaws.com"
                  source_arn    = "${aws_apigatewayv2_api.http.execution_arn}/*/*"
                }
                """),
            F("src/handler.py", """
                import json
                import os

                def handler(event, context):
                    return {
                        "statusCode": 200,
                        "headers": {"content-type": "application/json"},
                        "body": json.dumps({
                            "message": "Hello from a serverless Lambda!",
                            "table": os.environ.get("TABLE_NAME"),
                            "path": event.get("rawPath", "/"),
                        }),
                    }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "region" {
                  description = "AWS region."
                  type        = string
                  default     = "us-east-1"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "api_url" {
                  description = "Invoke the API here."
                  value       = aws_apigatewayv2_api.http.api_endpoint
                }

                output "table_name" {
                  description = "DynamoDB table name."
                  value       = aws_dynamodb_table.items.name
                }
                """),
            F("terraform.tfvars", """
                project_name = "my-api"
                region       = "us-east-1"
                """),
            F("README.md", """
                # AWS serverless API (Lambda + DynamoDB)

                An HTTP API backed by an arm64 Lambda and an on-demand DynamoDB table. Scales to zero — you pay
                only per request, and it fits the AWS free tier for demos.

                ## Use
                1. Plan & apply. Terraform zips `src/handler.py` for you.
                2. `curl $(terraform output -raw api_url)`.
                3. Edit `src/handler.py` and re-apply to ship changes.

                ## Cost
                ~$0 idle. Free tier covers 1M requests/mo. `terraform destroy` when done.
                """)));

        // ── AWS · single VM + Docker (Graviton, default VPC) ────────────────────────────────────────────
        list.Add(T(
            Info("aws-vm-docker", "AWS · VM + Docker (cheapest full host)",
                "One Graviton (arm64) EC2 instance in the default VPC running your container via Docker on boot, with a locked-down security group. The cheapest way to host a full app — no ALB, no NAT.",
                TemplateProvider.Aws, TemplateCategory.VirtualMachine, TemplateCostTier.Low,
                "~$6–8/mo for a t4g.small on-demand (less on a Savings Plan); free tier covers t2.micro/t3.micro 750h.",
                ["ec2", "docker", "graviton", "vm", "cheap"],
                teardownHint: "terraform destroy stops billing; the instance is the only ongoing cost."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                data "aws_vpc" "default" {
                  default = true
                }

                data "aws_subnets" "default" {
                  filter {
                    name   = "vpc-id"
                    values = [data.aws_vpc.default.id]
                  }
                }

                # Latest Amazon Linux 2023 for arm64 (Graviton = cheaper).
                data "aws_ami" "al2023" {
                  most_recent = true
                  owners      = ["amazon"]
                  filter {
                    name   = "name"
                    values = ["al2023-ami-*-arm64"]
                  }
                  filter {
                    name   = "architecture"
                    values = ["arm64"]
                  }
                }

                resource "aws_security_group" "web" {
                  name        = "${var.project_name}-web"
                  description = "Web + SSH"
                  vpc_id      = data.aws_vpc.default.id

                  ingress {
                    description = "HTTP"
                    from_port   = 80
                    to_port     = 80
                    protocol    = "tcp"
                    cidr_blocks = ["0.0.0.0/0"]
                  }
                  ingress {
                    description = "SSH (restrict to your IP in production)"
                    from_port   = 22
                    to_port     = 22
                    protocol    = "tcp"
                    cidr_blocks = [var.ssh_cidr]
                  }
                  egress {
                    from_port   = 0
                    to_port     = 0
                    protocol    = "-1"
                    cidr_blocks = ["0.0.0.0/0"]
                  }
                  tags = local.tags
                }

                resource "aws_instance" "app" {
                  ami                         = data.aws_ami.al2023.id
                  instance_type               = var.instance_type
                  subnet_id                   = data.aws_subnets.default.ids[0]
                  vpc_security_group_ids      = [aws_security_group.web.id]
                  associate_public_ip_address = true

                  user_data = <<-EOT
                    #!/bin/bash
                    dnf update -y
                    dnf install -y docker
                    systemctl enable --now docker
                    docker run -d --restart always -p 80:80 ${var.container_image}
                  EOT

                  root_block_device {
                    volume_size = 20
                    volume_type = "gp3"
                  }

                  tags = merge(local.tags, { Name = var.project_name })
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "region" {
                  description = "AWS region."
                  type        = string
                  default     = "us-east-1"
                }

                variable "instance_type" {
                  description = "Graviton size. t4g.small is a good cheap default; t4g.micro is free-tier-eligible in some regions."
                  type        = string
                  default     = "t4g.small"
                }

                variable "container_image" {
                  description = "Docker image to run on port 80."
                  type        = string
                  default     = "nginxdemos/hello:latest"
                }

                variable "ssh_cidr" {
                  description = "CIDR allowed to SSH. Lock this to your IP/32 in production."
                  type        = string
                  default     = "0.0.0.0/0"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "public_ip" {
                  description = "Instance public IP."
                  value       = aws_instance.app.public_ip
                }

                output "url" {
                  description = "Open your app here."
                  value       = "http://${aws_instance.app.public_ip}"
                }
                """),
            F("terraform.tfvars", """
                project_name    = "my-app"
                region          = "us-east-1"
                instance_type   = "t4g.small"
                container_image = "nginxdemos/hello:latest"
                # ssh_cidr      = "203.0.113.10/32"  # your IP
                """),
            F("README.md", """
                # AWS VM + Docker

                One Graviton EC2 instance in the default VPC that installs Docker and runs your container on boot.
                No load balancer, no NAT gateway — the cheapest way to host a real app.

                ## Use
                1. Set `container_image` (and lock `ssh_cidr` to your IP).
                2. Plan & apply, then open the `url` output.

                ## Cost
                Just the instance (~$6–8/mo for t4g.small). Swap to t3.micro/t4g.micro for the free tier.
                `terraform destroy` when done.
                """)));

        // ── AWS · VPC networking baseline (no NAT gateway) ──────────────────────────────────────────────
        list.Add(T(
            Info("aws-vpc-network", "AWS · VPC networking baseline",
                "A production-shaped VPC across two AZs: public + private subnets, internet gateway, route tables. Deliberately NO NAT gateway (that's ~$32/mo each) — add VPC endpoints or a NAT instance only if private egress is truly needed.",
                TemplateProvider.Aws, TemplateCategory.Networking, TemplateCostTier.Low,
                "~$0 for the VPC itself (you pay for what you run in it). No NAT gateway = no fixed network cost.",
                ["vpc", "network", "subnets", "no-nat", "foundation"]),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                data "aws_availability_zones" "available" {
                  state = "available"
                }

                locals {
                  azs  = slice(data.aws_availability_zones.available.names, 0, var.az_count)
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "aws_vpc" "main" {
                  cidr_block           = var.vpc_cidr
                  enable_dns_support   = true
                  enable_dns_hostnames = true
                  tags                 = merge(local.tags, { Name = var.project_name })
                }

                resource "aws_internet_gateway" "igw" {
                  vpc_id = aws_vpc.main.id
                  tags   = merge(local.tags, { Name = "${var.project_name}-igw" })
                }

                resource "aws_subnet" "public" {
                  count                   = var.az_count
                  vpc_id                  = aws_vpc.main.id
                  cidr_block              = cidrsubnet(var.vpc_cidr, 8, count.index)
                  availability_zone       = local.azs[count.index]
                  map_public_ip_on_launch = true
                  tags                    = merge(local.tags, { Name = "${var.project_name}-public-${count.index}", Tier = "public" })
                }

                resource "aws_subnet" "private" {
                  count             = var.az_count
                  vpc_id            = aws_vpc.main.id
                  cidr_block        = cidrsubnet(var.vpc_cidr, 8, count.index + 100)
                  availability_zone = local.azs[count.index]
                  tags              = merge(local.tags, { Name = "${var.project_name}-private-${count.index}", Tier = "private" })
                }

                resource "aws_route_table" "public" {
                  vpc_id = aws_vpc.main.id
                  route {
                    cidr_block = "0.0.0.0/0"
                    gateway_id = aws_internet_gateway.igw.id
                  }
                  tags = merge(local.tags, { Name = "${var.project_name}-public-rt" })
                }

                resource "aws_route_table_association" "public" {
                  count          = var.az_count
                  subnet_id      = aws_subnet.public[count.index].id
                  route_table_id = aws_route_table.public.id
                }

                # Private route table with NO NAT gateway (saves ~$32/mo each). Attach VPC endpoints or a cheap
                # NAT instance / fck-nat here only if private subnets need outbound internet.
                resource "aws_route_table" "private" {
                  vpc_id = aws_vpc.main.id
                  tags   = merge(local.tags, { Name = "${var.project_name}-private-rt" })
                }

                resource "aws_route_table_association" "private" {
                  count          = var.az_count
                  subnet_id      = aws_subnet.private[count.index].id
                  route_table_id = aws_route_table.private.id
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "region" {
                  description = "AWS region."
                  type        = string
                  default     = "us-east-1"
                }

                variable "vpc_cidr" {
                  description = "CIDR for the VPC."
                  type        = string
                  default     = "10.0.0.0/16"
                }

                variable "az_count" {
                  description = "How many availability zones to spread across."
                  type        = number
                  default     = 2
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "vpc_id" {
                  value = aws_vpc.main.id
                }

                output "public_subnet_ids" {
                  value = aws_subnet.public[*].id
                }

                output "private_subnet_ids" {
                  value = aws_subnet.private[*].id
                }
                """),
            F("terraform.tfvars", """
                project_name = "my-net"
                region       = "us-east-1"
                vpc_cidr     = "10.0.0.0/16"
                az_count     = 2
                """),
            F("README.md", """
                # AWS VPC networking baseline

                A two-AZ VPC with public and private subnets and an internet gateway — but no NAT gateway, so
                there's no fixed network cost. Reference the subnet outputs from your compute stacks.
                """)));

        // ── AWS · Fargate Spot web app (ALB, public subnets, no NAT) ────────────────────────────────────
        list.Add(T(
            Info("aws-fargate-web", "AWS · Container web app (Fargate Spot + ALB)",
                "A containerized web app on ECS Fargate Spot (up to ~70% cheaper) behind an Application Load Balancer, in public subnets with public IPs so there's no NAT gateway. Includes an ECR repo for your own images and arm64 tasks.",
                TemplateProvider.Aws, TemplateCategory.WebApp, TemplateCostTier.Medium,
                "Mainly the ALB (~$16–22/mo) + Fargate Spot vCPU/GB-seconds. No NAT gateway.",
                ["ecs", "fargate", "fargate-spot", "alb", "ecr", "containers"]),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                data "aws_availability_zones" "available" {
                  state = "available"
                }

                locals {
                  azs  = slice(data.aws_availability_zones.available.names, 0, 2)
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "aws_vpc" "main" {
                  cidr_block           = "10.0.0.0/16"
                  enable_dns_support   = true
                  enable_dns_hostnames = true
                  tags                 = merge(local.tags, { Name = var.project_name })
                }

                resource "aws_internet_gateway" "igw" {
                  vpc_id = aws_vpc.main.id
                  tags   = local.tags
                }

                resource "aws_subnet" "public" {
                  count                   = 2
                  vpc_id                  = aws_vpc.main.id
                  cidr_block              = cidrsubnet(aws_vpc.main.cidr_block, 8, count.index)
                  availability_zone       = local.azs[count.index]
                  map_public_ip_on_launch = true
                  tags                    = local.tags
                }

                resource "aws_route_table" "public" {
                  vpc_id = aws_vpc.main.id
                  route {
                    cidr_block = "0.0.0.0/0"
                    gateway_id = aws_internet_gateway.igw.id
                  }
                  tags = local.tags
                }

                resource "aws_route_table_association" "public" {
                  count          = 2
                  subnet_id      = aws_subnet.public[count.index].id
                  route_table_id = aws_route_table.public.id
                }

                resource "aws_ecr_repository" "app" {
                  name                 = var.project_name
                  image_tag_mutability = "MUTABLE"
                  force_delete         = true
                  tags                 = local.tags
                }

                resource "aws_security_group" "alb" {
                  name   = "${var.project_name}-alb"
                  vpc_id = aws_vpc.main.id
                  ingress {
                    from_port   = 80
                    to_port     = 80
                    protocol    = "tcp"
                    cidr_blocks = ["0.0.0.0/0"]
                  }
                  egress {
                    from_port   = 0
                    to_port     = 0
                    protocol    = "-1"
                    cidr_blocks = ["0.0.0.0/0"]
                  }
                  tags = local.tags
                }

                resource "aws_security_group" "svc" {
                  name   = "${var.project_name}-svc"
                  vpc_id = aws_vpc.main.id
                  ingress {
                    from_port       = var.container_port
                    to_port         = var.container_port
                    protocol        = "tcp"
                    security_groups = [aws_security_group.alb.id]
                  }
                  egress {
                    from_port   = 0
                    to_port     = 0
                    protocol    = "-1"
                    cidr_blocks = ["0.0.0.0/0"]
                  }
                  tags = local.tags
                }

                resource "aws_lb" "app" {
                  name               = "${var.project_name}-alb"
                  load_balancer_type = "application"
                  security_groups    = [aws_security_group.alb.id]
                  subnets            = aws_subnet.public[*].id
                  tags               = local.tags
                }

                resource "aws_lb_target_group" "app" {
                  name        = "${var.project_name}-tg"
                  port        = var.container_port
                  protocol    = "HTTP"
                  vpc_id      = aws_vpc.main.id
                  target_type = "ip"
                  health_check {
                    path    = var.health_check_path
                    matcher = "200-399"
                  }
                  tags = local.tags
                }

                resource "aws_lb_listener" "http" {
                  load_balancer_arn = aws_lb.app.arn
                  port              = 80
                  protocol          = "HTTP"
                  default_action {
                    type             = "forward"
                    target_group_arn = aws_lb_target_group.app.arn
                  }
                }

                resource "aws_iam_role" "exec" {
                  name = "${var.project_name}-exec"
                  assume_role_policy = jsonencode({
                    Version = "2012-10-17"
                    Statement = [{ Action = "sts:AssumeRole", Effect = "Allow", Principal = { Service = "ecs-tasks.amazonaws.com" } }]
                  })
                  tags = local.tags
                }

                resource "aws_iam_role_policy_attachment" "exec" {
                  role       = aws_iam_role.exec.name
                  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
                }

                resource "aws_cloudwatch_log_group" "app" {
                  name              = "/ecs/${var.project_name}"
                  retention_in_days = 14
                  tags              = local.tags
                }

                resource "aws_ecs_cluster" "app" {
                  name = var.project_name
                  tags = local.tags
                }

                # Associate the Fargate capacity providers so the service may use FARGATE_SPOT.
                resource "aws_ecs_cluster_capacity_providers" "app" {
                  cluster_name       = aws_ecs_cluster.app.name
                  capacity_providers = ["FARGATE", "FARGATE_SPOT"]
                }

                resource "aws_ecs_task_definition" "app" {
                  family                   = var.project_name
                  requires_compatibilities = ["FARGATE"]
                  network_mode             = "awsvpc"
                  cpu                      = var.task_cpu
                  memory                   = var.task_memory
                  execution_role_arn       = aws_iam_role.exec.arn

                  runtime_platform {
                    cpu_architecture        = "ARM64"
                    operating_system_family = "LINUX"
                  }

                  container_definitions = jsonencode([{
                    name         = "app"
                    image        = var.container_image
                    essential    = true
                    portMappings = [{ containerPort = var.container_port, protocol = "tcp" }]
                    logConfiguration = {
                      logDriver = "awslogs"
                      options = {
                        awslogs-group         = aws_cloudwatch_log_group.app.name
                        awslogs-region        = var.region
                        awslogs-stream-prefix = "app"
                      }
                    }
                  }])
                  tags = local.tags
                }

                resource "aws_ecs_service" "app" {
                  name            = var.project_name
                  cluster         = aws_ecs_cluster.app.id
                  task_definition = aws_ecs_task_definition.app.arn
                  desired_count   = var.desired_count

                  # Fargate Spot: up to ~70% cheaper than on-demand.
                  capacity_provider_strategy {
                    capacity_provider = "FARGATE_SPOT"
                    weight            = 1
                  }

                  network_configuration {
                    subnets          = aws_subnet.public[*].id
                    security_groups  = [aws_security_group.svc.id]
                    assign_public_ip = true # public subnet ⇒ no NAT gateway required
                  }

                  load_balancer {
                    target_group_arn = aws_lb_target_group.app.arn
                    container_name   = "app"
                    container_port   = var.container_port
                  }

                  depends_on = [aws_lb_listener.http, aws_ecs_cluster_capacity_providers.app]
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "region" {
                  description = "AWS region."
                  type        = string
                  default     = "us-east-1"
                }

                variable "container_image" {
                  description = "Container image to run (arm64/multi-arch). Push your own to the created ECR repo."
                  type        = string
                  default     = "public.ecr.aws/nginx/nginx:latest"
                }

                variable "container_port" {
                  description = "Port the container listens on."
                  type        = number
                  default     = 80
                }

                variable "health_check_path" {
                  description = "ALB health-check path."
                  type        = string
                  default     = "/"
                }

                variable "task_cpu" {
                  description = "Fargate task CPU units (256 = 0.25 vCPU)."
                  type        = string
                  default     = "256"
                }

                variable "task_memory" {
                  description = "Fargate task memory (MiB)."
                  type        = string
                  default     = "512"
                }

                variable "desired_count" {
                  description = "Number of tasks to run."
                  type        = number
                  default     = 1
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "app_url" {
                  description = "Public URL via the load balancer."
                  value       = "http://${aws_lb.app.dns_name}"
                }

                output "ecr_repository_url" {
                  description = "Push your images here, then update container_image."
                  value       = aws_ecr_repository.app.repository_url
                }
                """),
            F("terraform.tfvars", """
                project_name    = "my-web"
                region          = "us-east-1"
                container_image = "public.ecr.aws/nginx/nginx:latest"
                container_port  = 80
                """),
            F("README.md", """
                # AWS container web app (Fargate Spot + ALB)

                A load-balanced container service on Fargate Spot in public subnets (no NAT gateway). An ECR repo
                is created for your own images. Plan & apply, then open the `app_url` output.
                """)));

        // ── AWS · RDS PostgreSQL (db.t4g.micro, single-AZ) ──────────────────────────────────────────────
        list.Add(T(
            Info("aws-rds-postgres", "AWS · PostgreSQL (RDS db.t4g.micro)",
                "A single-AZ RDS PostgreSQL on a Graviton db.t4g.micro in the default VPC's private range, reachable only from inside the VPC, with a generated password. The cheapest reliable managed Postgres (beats Aurora for low traffic).",
                TemplateProvider.Aws, TemplateCategory.Database, TemplateCostTier.Low,
                "~$12/mo for db.t4g.micro single-AZ + 20GB gp3. (Aurora Serverless v2 min is ~$43/mo.)",
                ["rds", "postgres", "database", "graviton", "cheap"]),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                    random = {
                      source  = "hashicorp/random"
                      version = "~> 3.6"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                data "aws_vpc" "default" {
                  default = true
                }

                data "aws_subnets" "default" {
                  filter {
                    name   = "vpc-id"
                    values = [data.aws_vpc.default.id]
                  }
                }

                resource "random_password" "db" {
                  length  = 20
                  special = false
                }

                resource "aws_db_subnet_group" "db" {
                  name       = "${var.project_name}-db"
                  subnet_ids = data.aws_subnets.default.ids
                  tags       = local.tags
                }

                # Only resources inside the VPC may reach the database.
                resource "aws_security_group" "db" {
                  name   = "${var.project_name}-db"
                  vpc_id = data.aws_vpc.default.id
                  ingress {
                    description = "PostgreSQL from within the VPC"
                    from_port   = 5432
                    to_port     = 5432
                    protocol    = "tcp"
                    cidr_blocks = [data.aws_vpc.default.cidr_block]
                  }
                  egress {
                    from_port   = 0
                    to_port     = 0
                    protocol    = "-1"
                    cidr_blocks = ["0.0.0.0/0"]
                  }
                  tags = local.tags
                }

                resource "aws_db_instance" "db" {
                  identifier              = "${var.project_name}-db"
                  engine                  = "postgres"
                  engine_version          = var.engine_version
                  instance_class          = "db.t4g.micro" # cheapest Graviton
                  allocated_storage       = 20
                  storage_type            = "gp3"
                  db_name                 = var.db_name
                  username                = var.db_username
                  password                = random_password.db.result
                  db_subnet_group_name    = aws_db_subnet_group.db.name
                  vpc_security_group_ids  = [aws_security_group.db.id]
                  multi_az                = false # single-AZ = cheapest
                  publicly_accessible     = false
                  backup_retention_period = 7
                  skip_final_snapshot     = true
                  tags                    = local.tags
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "region" {
                  description = "AWS region."
                  type        = string
                  default     = "us-east-1"
                }

                variable "db_name" {
                  description = "Initial database name."
                  type        = string
                  default     = "app"
                }

                variable "db_username" {
                  description = "Master username."
                  type        = string
                  default     = "app"
                }

                variable "engine_version" {
                  description = "PostgreSQL major version."
                  type        = string
                  default     = "16"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "db_endpoint" {
                  description = "Host to connect to (from inside the VPC)."
                  value       = aws_db_instance.db.address
                }

                output "db_password" {
                  description = "Generated master password."
                  value       = random_password.db.result
                  sensitive   = true
                }
                """),
            F("terraform.tfvars", """
                project_name = "my-db"
                region       = "us-east-1"
                db_name      = "app"
                """),
            F("README.md", """
                # AWS PostgreSQL (RDS db.t4g.micro)

                A cheap single-AZ managed Postgres reachable only from inside the VPC. Read the password with
                `terraform output -raw db_password`. `terraform destroy` when done.
                """)));

        // ── AWS · Terraform remote-state backend (S3 + DynamoDB) ────────────────────────────────────────
        list.Add(T(
            Info("aws-remote-state", "AWS · Remote state backend (S3 + DynamoDB)",
                "Bootstraps the S3 bucket (versioned + encrypted + private) and DynamoDB lock table that Terraform's S3 backend needs for safe team state. Essentially free. Apply this once, then point your other projects' backends at it.",
                TemplateProvider.Aws, TemplateCategory.Starter, TemplateCostTier.Free,
                "Effectively free: pennies for state storage + on-demand lock table.",
                ["backend", "remote-state", "s3", "dynamodb", "team"]),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                locals {
                  tags = merge(var.tags, { Project = var.project_name, ManagedBy = "terraform" })
                }

                resource "aws_s3_bucket" "state" {
                  bucket = var.state_bucket_name
                  tags   = local.tags
                }

                resource "aws_s3_bucket_versioning" "state" {
                  bucket = aws_s3_bucket.state.id
                  versioning_configuration {
                    status = "Enabled"
                  }
                }

                resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
                  bucket = aws_s3_bucket.state.id
                  rule {
                    apply_server_side_encryption_by_default {
                      sse_algorithm = "AES256"
                    }
                  }
                }

                resource "aws_s3_bucket_public_access_block" "state" {
                  bucket                  = aws_s3_bucket.state.id
                  block_public_acls       = true
                  block_public_policy     = true
                  ignore_public_acls      = true
                  restrict_public_buckets = true
                }

                resource "aws_dynamodb_table" "lock" {
                  name         = var.lock_table_name
                  billing_mode = "PAY_PER_REQUEST"
                  hash_key     = "LockID"
                  attribute {
                    name = "LockID"
                    type = "S"
                  }
                  tags = local.tags
                }
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name for tagging."
                  type        = string
                  default     = "tfstate"
                }

                variable "region" {
                  description = "AWS region."
                  type        = string
                  default     = "us-east-1"
                }

                variable "state_bucket_name" {
                  description = "Globally-unique S3 bucket name for state."
                  type        = string
                }

                variable "lock_table_name" {
                  description = "DynamoDB table name for state locks."
                  type        = string
                  default     = "terraform-locks"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "state_bucket" {
                  value = aws_s3_bucket.state.bucket
                }

                output "lock_table" {
                  value = aws_dynamodb_table.lock.name
                }
                """),
            F("terraform.tfvars", """
                project_name      = "tfstate"
                region            = "us-east-1"
                state_bucket_name = "my-org-terraform-state"
                lock_table_name   = "terraform-locks"
                """),
            F("README.md", """
                # AWS remote state backend

                Creates the S3 bucket + DynamoDB lock table for Terraform's S3 backend. Then in another project:

                ```
                terraform {
                  backend "s3" {
                    bucket         = "<state_bucket>"
                    key            = "env/terraform.tfstate"
                    region         = "us-east-1"
                    dynamodb_table = "<lock_table>"
                    encrypt        = true
                  }
                }
                ```
                """)));

        // ── AWS · Scheduled Lambda (EventBridge cron) — free tier ───────────────────────────────────────
        list.Add(T(
            Info("aws-lambda-cron", "AWS · Scheduled task (Lambda + EventBridge)",
                "A serverless cron: an arm64 Lambda invoked on a schedule by EventBridge. No servers, scales to zero, fits the free tier — ideal for periodic jobs, clean-ups, and pings.",
                TemplateProvider.Aws, TemplateCategory.Serverless, TemplateCostTier.Free,
                "Free tier: 1M Lambda requests/mo. ~$0 for a light schedule.",
                ["lambda", "eventbridge", "cron", "scheduled", "free-tier"],
                teardownHint: "Nothing runs between invocations; terraform destroy leaves zero cost."),
            F("providers.tf", """
                terraform {
                  required_version = ">= 1.5.0"
                  required_providers {
                    aws = {
                      source  = "hashicorp/aws"
                      version = "~> 5.0"
                    }
                    archive = {
                      source  = "hashicorp/archive"
                      version = "~> 2.4"
                    }
                  }
                }

                provider "aws" {
                  region = var.region
                }
                """),
            F("main.tf", """
                locals {
                  name = var.project_name
                  tags = merge(var.tags, { Project = local.name, ManagedBy = "terraform" })
                }

                data "archive_file" "lambda" {
                  type        = "zip"
                  source_file = "${path.module}/src/handler.py"
                  output_path = "${path.module}/build/handler.zip"
                }

                resource "aws_iam_role" "lambda" {
                  name = "${local.name}-lambda"
                  assume_role_policy = jsonencode({
                    Version = "2012-10-17"
                    Statement = [{ Action = "sts:AssumeRole", Effect = "Allow", Principal = { Service = "lambda.amazonaws.com" } }]
                  })
                  tags = local.tags
                }

                resource "aws_iam_role_policy_attachment" "logs" {
                  role       = aws_iam_role.lambda.name
                  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
                }

                resource "aws_lambda_function" "job" {
                  function_name    = "${local.name}-job"
                  role             = aws_iam_role.lambda.arn
                  runtime          = "python3.12"
                  handler          = "handler.handler"
                  filename         = data.archive_file.lambda.output_path
                  source_code_hash = data.archive_file.lambda.output_base64sha256
                  architectures    = ["arm64"]
                  timeout          = 30
                  memory_size      = 128
                  tags             = local.tags
                }

                resource "aws_cloudwatch_event_rule" "schedule" {
                  name                = "${local.name}-schedule"
                  schedule_expression = var.schedule
                  tags                = local.tags
                }

                resource "aws_cloudwatch_event_target" "lambda" {
                  rule = aws_cloudwatch_event_rule.schedule.name
                  arn  = aws_lambda_function.job.arn
                }

                resource "aws_lambda_permission" "events" {
                  statement_id  = "AllowEventBridge"
                  action        = "lambda:InvokeFunction"
                  function_name = aws_lambda_function.job.function_name
                  principal     = "events.amazonaws.com"
                  source_arn    = aws_cloudwatch_event_rule.schedule.arn
                }
                """),
            F("src/handler.py", """
                import datetime

                def handler(event, context):
                    print("Scheduled job ran at", datetime.datetime.utcnow().isoformat())
                    return {"ok": True}
                """),
            F("variables.tf", """
                variable "project_name" {
                  description = "Short name used to prefix resources."
                  type        = string
                }

                variable "region" {
                  description = "AWS region."
                  type        = string
                  default     = "us-east-1"
                }

                variable "schedule" {
                  description = "EventBridge schedule expression, e.g. rate(1 hour) or cron(0 8 * * ? *)."
                  type        = string
                  default     = "rate(1 hour)"
                }

                variable "tags" {
                  description = "Extra tags applied to every resource."
                  type        = map(string)
                  default     = {}
                }
                """),
            F("outputs.tf", """
                output "function_name" {
                  value = aws_lambda_function.job.function_name
                }

                output "schedule" {
                  value = aws_cloudwatch_event_rule.schedule.schedule_expression
                }
                """),
            F("terraform.tfvars", """
                project_name = "my-job"
                region       = "us-east-1"
                schedule     = "rate(1 hour)"
                """),
            F("README.md", """
                # AWS scheduled task (Lambda + EventBridge)

                An arm64 Lambda run on a schedule. Edit `src/handler.py` and re-apply to change the job; edit
                `schedule` to change the cadence. Free tier covers light schedules.
                """)));
    }
}
