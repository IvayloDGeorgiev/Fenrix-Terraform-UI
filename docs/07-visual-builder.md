# 07 · Visual Resource Builder

A command UI removes the need to *type commands*; it does not remove the need to *author configuration*. The visual builder adds a schema-driven resource editor. It is **post-MVP** (Phase 10) because it depends on the stable execution, file, and plan foundations.

## Schema source

Terraform exports machine-readable provider/resource/data-source schemas:

```text
terraform providers schema -json
```

The output identifies fields, types, descriptions, required/optional/computed attributes, and nested blocks. Fenrix caches these per provider version in `Cache\terraform-schemas\`.

## Capabilities

- Select an installed provider.
- Browse resource types and data sources; search resources.
- Create a resource block with required fields shown first and optional fields in collapsible sections.
- Generate nested blocks; support lists, sets, maps, and objects.
- Add references to other resources.
- Preview generated HCL; write it to a selected file.
- Edit existing **simple** resource blocks (literal values).
- Preserve unsupported HCL as raw source.

## Deliberate limitation

Do **not** attempt to convert every HCL expression into graphical controls. Terraform supports expressions, functions, dynamic blocks, loops, conditionals, local values, and complex references. The first builder generates new blocks and edits straightforward literal values; anything advanced round-trips through the text editor untouched. This keeps the builder useful without it becoming a fragile HCL reimplementation.

## Templates

Schema-driven generation feeds a reusable **infrastructure template** feature (also Phase 10): parameterised, saved resource/module scaffolds that teams can reuse, and later share via the enterprise metadata database ([12-database-design.md](12-database-design.md), Phase 11).
