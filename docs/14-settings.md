# 14 · Settings Model

Settings are stored in the `Settings` table (Key/Value/Scope/UpdatedAt) and `settings.json` under the data root. Scope allows global, per-project, and per-environment overrides.

## General

Startup behaviour · reopen last project · default projects directory · confirmation settings · notification settings · autosave preference.

## Terraform

Default executable · installed versions · auto-discovery · provider plugin cache · default parallelism · default lock timeout · default input behaviour · upgrade checking · environment-variable overrides.

## Git

Git executable · user name · user email · default branch · pull strategy · prune settings · commit signing · Git LFS · credential helper.

## Cloud

Azure CLI path · AWS CLI path · Google Cloud CLI path · default profiles · connection-test settings.

## Database

SQLite · SQL Server · connection test · migration status · backup · restore · export diagnostics.

## Security

Secret storage provider · log redaction · clipboard clearing · production confirmation · session locking · diagnostic-data consent.

## Advanced

Raw environment variables · CLI arguments · feature flags · experimental visual builder · terminal shell · file-watcher exclusions.

## Appearance

(See [13-ui-design.md](13-ui-design.md).) Theme mode, density, fonts, high-contrast, saved layouts.

## Scope resolution

When resolving a setting, Fenrix reads the most specific scope first: **environment → project → global → built-in default**. The Settings UI shows which scope a value comes from and lets the user override or clear it.
