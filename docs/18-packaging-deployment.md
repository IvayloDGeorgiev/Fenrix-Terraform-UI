# 18 · Packaging & Deployment

Publish the Windows application as a **signed MSIX** package. .NET MAUI supports both packaged MSIX and unpackaged Windows deployment; MSIX also supports install/update configuration.

> **Current template note:** the scaffold sets `<WindowsPackageType>None</WindowsPackageType>` (unpackaged) for fast local dev. Release builds switch to MSIX. Keep unpackaged for the inner-loop, MSIX for distribution.

## Deployment channels

Development · Internal testing · Preview · Stable.

## Installer responsibilities

- Install the application.
- Create the Fenrix workspace root (`C:\FenrixSource\FenrixIaCStudio\`, with `%LOCALAPPDATA%` fallback — see [03-domain-model.md](03-domain-model.md)).
- Apply directory permissions (grant current user modify access).
- Check the WebView runtime is present.
- Register file associations where useful.
- Create a Start-menu shortcut.
- Optionally register a `fenrix-iac://` protocol.
- Preserve user projects during upgrades.
- Never remove the workspace root during uninstall without explicit approval.

## File associations

`.tf` · `.tfvars` · `.hcl` · `.tfplan` (read-only opening) · `.fenrixproject`.

## Update mechanism

MSIX update configuration drives channel-based updates. The updater must preserve the data root, database, and registered projects across versions, and run migrations on first launch of a new version ([12-database-design.md](12-database-design.md)). Crash recovery and database backup are part of release prep (Phase 12).
