variable "greeting" {
  description = "The message written into the generated file."
  type        = string
  default     = "Hello from Fenrix IaC Studio"
}

variable "output_dir" {
  description = "Directory (relative to this project) where the greeting file is written."
  type        = string
  default     = "generated"
}

variable "suffix_length" {
  description = "Number of random hex characters appended to the file name."
  type        = number
  default     = 4
}
