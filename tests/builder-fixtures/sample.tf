# A hand-authored resource used to test the round-trip reader.
resource "aws_instance" "web" {
  ami           = "ami-123"          # simple string literal
  instance_type = var.instance_type  # reference -> complex
  count         = 2
  monitoring    = true
  tags = {
    Name = "web"
  }
  user_data = "${file("init.sh")}"   # interpolation -> complex

  ebs_block_device {
    device_name = "/dev/sdb"
    volume_size = 20
  }
}
