# 28 · Per-Project Key-Pair & SSH Key Management

A DevOps engineer shouldn't have to leave Fenrix to create an EC2 key pair, download the `.pem`, and
figure out where to keep it safely. Fenrix manages **SSH/instance key pairs per project** end to end:
import an existing key, or generate a new one through Terraform, and in both cases the private key lands
in a secure, app-managed folder that the Terraform screens can reference — all inside the project.

This builds directly on the secret-reference model in [11-secrets.md](11-secrets.md): **Fenrix stores an
encrypted private key at rest and a reference to it — never a plaintext key in the DB, manifest, logs, or
Git.**

## What the user can do

- **Import a key** — select an existing `.pem`/`.ppk`/OpenSSH private key. Fenrix copies it into the
  secure store (encrypted), records metadata, and removes the need to track the original file.
- **Generate a key pair** — Fenrix runs Terraform on the backend (`tls_private_key` + a provider
  resource such as `aws_key_pair`), captures the generated private key from a **sensitive output**, and
  writes it straight into the secure store. No AWS-console round-trip.
- **View & use keys** — list a project's keys with fingerprint, public key, algorithm, created date, and
  source (imported/generated). Copy the public key, copy the secure path for use in a `connection`/
  `provisioner` block, or insert a reference into config.
- **Lifecycle** — rename, rotate, delete (with confirmation), and export the public key. Private-key
  export is gated and audited.

## Storage & security

- Private keys live **outside the project folder** under the Fenrix data root, keyed by project:
  `Data\keys\<projectId>\<keyId>` — so they can never be accidentally committed. A matching
  `.gitignore` reminder is still offered for any in-repo key material.
- Encrypted at rest with **Windows DPAPI** (per-user) — item (6) in [11-secrets.md](11-secrets.md).
  The DB holds only a `KeyPair` record (id, project, name, public key, fingerprint, algorithm, source,
  created-at, encrypted-file path) — never the private bytes.
- Values are decrypted only when needed, into a process-scoped path/environment, and the plaintext is
  discarded after use. Copy actions offer "clear clipboard" as elsewhere.
- Every reveal/export/use is written to the redacted command/audit history
  ([15-logging-auditing.md](15-logging-auditing.md)).

## Generate-via-Terraform flow

```text
User: Keys → New key pair (project "CustomerInfra", env Dev, name "app-bastion")
  → Fenrix writes a small generator config to a temp working dir
       resource "tls_private_key" "k"   { algorithm = "RSA" ; rsa_bits = 4096 }
       resource "aws_key_pair"    "k"   { key_name = "app-bastion" ; public_key = tls_private_key.k.public_key_openssh }
       output  "private_key_pem"        { value = tls_private_key.k.private_key_pem ; sensitive = true }
  → plan → apply (saved-plan safety, per-env lock)                       [Phase 4]
  → terraform output -json  → read the sensitive private_key_pem
  → encrypt (DPAPI) + store under Data\keys\<projectId>\ ; record KeyPair
  → surface the new key in the project's Keys section
```

The command preview shows exactly what runs at every step ([23-command-transparency.md](23-command-transparency.md)).

## Domain / layering

- **Domain:** `Security/KeyPair` (metadata + `SecretReference` to the encrypted file).
- **Application:** `Abstractions/Security/IKeyPairService` (import, generate, list, reveal-public,
  export, delete) + a generator that emits the Terraform above.
- **Infrastructure:** DPAPI-backed `KeyStore` (Windows), secure-folder placement, EF config; generation
  reuses the Phase 3 process runner and the Phase 4 plan/apply + `output -json` parsing.
- **UI:** a **Keys** section inside the project (a tab alongside Files and Terraform), plus a picker so
  `connection`/`provisioner` blocks and `aws_key_pair` references can point at a managed key.

## Delivery placement

Import + secure store + view can land as soon as the secure-storage plumbing exists. **Generation**
depends on the plan→apply engine and `output -json` (Phase 4) and on provider/cloud context
(Phases 7–8), so the full feature is scheduled as **Phase 8.5 — Project secrets & key-pair management**,
after cloud connections. Tracked in [ROADMAP.md](ROADMAP.md) / [PROGRESS.md](PROGRESS.md).

## Related DevOps capabilities to consider (same "everything for a project in one place" spirit)

- **Environment variables & tfvars secrets** — manage sensitive variable values via secret references,
  injected per command ([11](11-secrets.md), Phase 8).
- **Remote state & backends** — configure/init backends, state locking, force-unlock, state browse
  ([09/25], Phase 9).
- **Policy & security scanning** — pre-apply checks (tfsec/checkov/OPA) surfaced next to the plan.
- **Cost estimation** — Infracost-style diff on the plan screen.
- **Drift detection** — scheduled refresh-only plans per environment with alerts.
- **Connect action** — SSH into an instance/bastion using a managed key (closes the loop on key usage).
- **Module registry** — private/shared modules per client/org.
