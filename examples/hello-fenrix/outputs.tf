output "greeting_file" {
  description = "Path to the file Terraform created."
  value       = local_file.greeting.filename
}

output "random_suffix" {
  description = "The random suffix used in the file name."
  value       = random_id.suffix.hex
}
