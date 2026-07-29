#!/usr/bin/env python3
"""
Reference port of the Phase 10 visual-builder pure logic:
  - ProviderSchemaJsonParser  (terraform providers schema -json -> typed schema)
  - HclEmitter                (block/value model -> canonical HCL)
  - ConfigHclBuilder          (config-side generators: variable/output/locals/provider/terraform/module/resource/tfvars)
  - HclLexer + HclReader      (outline + argument classification + literal round-trip splice)

MAUI/Blazor is not compiled in the authoring sandbox, so this Python port mirrors the C# algorithms and
asserts them against real-format fixtures. Run: python3 verify_builder.py
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
checks = 0
failures = []

def check(cond, msg):
    global checks
    checks += 1
    if not cond:
        failures.append(msg)

# ============================ ProviderSchemaJsonParser ============================

def parse_type(el):
    if isinstance(el, str):
        kind = {"string": "String", "number": "Number", "bool": "Bool"}.get(el, "Dynamic")
        label = el if el in ("string", "number", "bool") else "any"
        return {"kind": kind, "label": label}
    if isinstance(el, list) and el:
        ctor = el[0]
        if ctor in ("list", "set", "map"):
            elem = parse_type(el[1]) if len(el) >= 2 else {"kind": "Dynamic", "label": "any"}
            kind = {"list": "List", "set": "Set", "map": "Map"}[ctor]
            return {"kind": kind, "label": f"{ctor}({elem['label']})", "element": elem}
        if ctor == "object":
            fields = []
            if len(el) >= 2 and isinstance(el[1], dict):
                for name, t in el[1].items():
                    fields.append((name, parse_type(t)))
            body = ", ".join(f"{n} = {t['label']}" for n, t in fields)
            return {"kind": "Object", "label": f"object({{{body}}})", "fields": fields}
        if ctor == "tuple":
            elems = [parse_type(e) for e in el[1]] if len(el) >= 2 and isinstance(el[1], list) else []
            body = ", ".join(t["label"] for t in elems)
            return {"kind": "Tuple", "label": f"tuple([{body}])"}
    return {"kind": "Dynamic", "label": "any"}

def parse_attribute(name, a):
    required = a.get("required") is True
    optional = a.get("optional") is True
    computed = a.get("computed") is True
    sensitive = a.get("sensitive") is True
    if "nested_type" in a and isinstance(a["nested_type"], dict):
        t = {"kind": "Object", "label": "object"}
    elif "type" in a:
        t = parse_type(a["type"])
    else:
        t = {"kind": "Dynamic", "label": "any"}
    return {"name": name, "type": t, "required": required, "optional": optional,
            "computed": computed, "sensitive": sensitive}

def parse_block(b):
    attrs = [parse_attribute(n, a) for n, a in (b.get("attributes") or {}).items()]
    nested = []
    for n, nb in (b.get("block_types") or {}).items():
        mode = {"single": "Single", "list": "List", "set": "Set", "map": "Map", "group": "Group"}.get(nb.get("nesting_mode"), "Single")
        nested.append({"type_name": n, "mode": mode, "block": parse_block(nb.get("block", {})),
                       "min": nb.get("min_items", 0), "max": nb.get("max_items", 0)})
    return {"attributes": attrs, "nested": nested}

def parse_provider(address, el):
    cfg = None
    if "provider" in el and "block" in el["provider"]:
        cfg = parse_block(el["provider"]["block"])
    def types(key):
        out = []
        for t, s in (el.get(key) or {}).items():
            out.append({"type": t, "version": s.get("version"), "block": parse_block(s.get("block", {}))})
        out.sort(key=lambda x: x["type"])
        return out
    return {"address": address, "config": cfg,
            "resources": types("resource_schemas"), "data_sources": types("data_source_schemas")}

def local_name(address):
    return address.split("/")[-1]

def source(address):
    parts = address.split("/")
    if len(parts) >= 3:
        host = parts[0]
        rest = f"{parts[-2]}/{parts[-1]}"
        return rest if host == "registry.terraform.io" else f"{host}/{rest}"
    return address

def required_attrs(block):
    return sorted([a for a in block["attributes"] if a["required"]], key=lambda a: a["name"])

def optional_attrs(block):
    return sorted([a for a in block["attributes"] if a["optional"] and not a["required"]], key=lambda a: a["name"])

def test_schema_parser():
    with open(os.path.join(HERE, "providers-schema.json")) as f:
        data = json.load(f)
    check(data.get("format_version") == "1.0", "format_version parsed")
    providers = [parse_provider(addr, el) for addr, el in data["provider_schemas"].items()]
    check(len(providers) == 1, "one provider")
    p = providers[0]
    check(local_name(p["address"]) == "aws", "provider local name aws")
    check(source(p["address"]) == "hashicorp/aws", "provider source hashicorp/aws")
    # provider config
    cfg_attrs = {a["name"]: a for a in p["config"]["attributes"]}
    check(cfg_attrs["access_key"]["sensitive"] is True, "access_key sensitive")
    # resource
    inst = next(r for r in p["resources"] if r["type"] == "aws_instance")
    req = [a["name"] for a in required_attrs(inst["block"])]
    check(req == ["ami", "instance_type"], f"required attrs sorted: {req}")
    opt = [a["name"] for a in optional_attrs(inst["block"])]
    check(opt == ["monitoring", "tags", "user_data"], f"optional attrs (id excluded as computed): {opt}")
    tags = {a["name"]: a for a in inst["block"]["attributes"]}["tags"]
    check(tags["type"]["kind"] == "Map" and tags["type"]["label"] == "map(string)", "tags is map(string)")
    ud = {a["name"]: a for a in inst["block"]["attributes"]}["user_data"]
    check(ud["sensitive"] is True, "user_data sensitive")
    # id is computed-only -> not configurable
    idattr = {a["name"]: a for a in inst["block"]["attributes"]}["id"]
    check(idattr["computed"] and not idattr["optional"] and not idattr["required"], "id computed-only")
    # nested block
    check(len(inst["block"]["nested"]) == 1, "one nested block")
    nb = inst["block"]["nested"][0]
    check(nb["type_name"] == "ebs_block_device" and nb["mode"] == "Set", "ebs_block_device is a set")
    check([a["name"] for a in required_attrs(nb["block"])] == ["device_name"], "nested required device_name")
    # data source
    ami = next(d for d in p["data_sources"] if d["type"] == "aws_ami")
    owners = {a["name"]: a for a in ami["block"]["attributes"]}["owners"]
    check(owners["type"]["label"] == "list(string)", "owners list(string)")

# ============================ HclEmitter ============================

def is_identifier(s):
    if not s: return False
    if not (s[0].isalpha() or s[0] == "_"): return False
    return all(c.isalnum() or c in "_-" for c in s)

def escape(v):
    out = []
    for c in v:
        out.append({"\\": "\\\\", '"': '\\"', "\n": "\\n", "\r": "\\r", "\t": "\\t"}.get(c, c))
    return "".join(out).replace("${", "$${").replace("%{", "%%{")

def fmt_key(k):
    return k if is_identifier(k) else f'"{escape(k)}"'

def render_value(v, depth):
    t = v[0]
    if t == "string": return f'"{escape(v[1])}"'
    if t == "number": return v[1]
    if t == "bool": return "true" if v[1] else "false"
    if t == "null": return "null"
    if t == "raw": return v[1]
    if t == "list": return render_list(v[1], depth)
    if t == "object": return render_object(v[1], depth)
    return "null"

def render_list(items, depth):
    if not items: return "[]"
    multiline = any(i[0] in ("list", "object") for i in items)
    if not multiline:
        return "[" + ", ".join(render_value(i, depth) for i in items) + "]"
    inner = depth + 1
    s = "[\n"
    for i, it in enumerate(items):
        s += "  " * inner + render_value(it, inner) + ("," if i < len(items) - 1 else "") + "\n"
    s += "  " * depth + "]"
    return s

def render_object(entries, depth):
    if not entries: return "{}"
    inner = depth + 1
    s = "{\n"
    for k, val in entries:
        s += "  " * inner + fmt_key(k) + " = " + render_value(val, inner) + "\n"
    s += "  " * depth + "}"
    return s

def emit_block(block, depth):
    pad = "  " * depth
    s = pad + block["type"]
    for lbl in block["labels"]:
        s += ' "' + escape(lbl) + '"'
    s += " {\n"
    inner = depth + 1
    for name, val in block["args"]:
        s += "  " * inner + name + " = " + render_value(val, inner) + "\n"
    if block["args"] and block["blocks"]:
        s += "\n"
    for i, b in enumerate(block["blocks"]):
        s += emit_block(b, inner)
        if i < len(block["blocks"]) - 1:
            s += "\n"
    s += pad + "}\n"
    return s

def emit(block):
    return emit_block(block, 0).rstrip("\n")

def blk(type_, labels, args=None, blocks=None):
    return {"type": type_, "labels": labels, "args": args or [], "blocks": blocks or []}

def test_emitter():
    # variable
    var = blk("variable", ["instance_type"], [
        ("type", ("raw", "string")),
        ("default", ("string", "t3.micro")),
        ("description", ("string", "Instance size")),
    ])
    expected = 'variable "instance_type" {\n  type = string\n  default = "t3.micro"\n  description = "Instance size"\n}'
    check(emit(var) == expected, f"variable emit:\n{emit(var)}")

    # terraform settings + required_providers + backend
    rp = blk("required_providers", [], [
        ("aws", ("object", [("source", ("string", "hashicorp/aws")), ("version", ("string", "~> 5.0"))])),
    ])
    tf = blk("terraform", [], [("required_version", ("string", ">= 1.5.0"))], [rp])
    expected_tf = ('terraform {\n  required_version = ">= 1.5.0"\n\n'
                   '  required_providers {\n    aws = {\n      source = "hashicorp/aws"\n'
                   '      version = "~> 5.0"\n    }\n  }\n}')
    check(emit(tf) == expected_tf, f"terraform emit:\n{emit(tf)}")

    # resource with nested block
    res = blk("resource", ["aws_instance", "web"], [
        ("ami", ("string", "ami-123")),
        ("instance_type", ("string", "t3.micro")),
    ], [blk("ebs_block_device", [], [("device_name", ("string", "/dev/sdb"))])])
    expected_res = ('resource "aws_instance" "web" {\n  ami = "ami-123"\n  instance_type = "t3.micro"\n\n'
                    '  ebs_block_device {\n    device_name = "/dev/sdb"\n  }\n}')
    check(emit(res) == expected_res, f"resource emit:\n{emit(res)}")

    # interpolation sigil escaping in a literal string
    check(render_value(("string", "cost is ${x}"), 0) == '"cost is $${x}"', "interpolation sigil escaped")

    # tfvars (bare lines)
    tfvars = "\n".join(f"{n} = {render_value(v, 0)}" for n, v in [
        ("instance_type", ("string", "t3.micro")),
        ("count", ("raw", "3")),
    ])
    check(tfvars == 'instance_type = "t3.micro"\ncount = 3', f"tfvars:\n{tfvars}")

# ============================ HclLexer + HclReader ============================

def scan_string(src, i):
    n = len(src); j = i + 1
    while j < n:
        c = src[j]
        if c == "\\": j += 2; continue
        if c == '"': return j + 1
        if c in "$%" and j + 1 < n and src[j + 1] == "{":
            j = scan_interp(src, j + 2); continue
        j += 1
    return n

def scan_interp(src, k):
    n = len(src); depth = 1
    while k < n and depth > 0:
        c = src[k]
        if c == '"': k = scan_string(src, k); continue
        if c == "{": depth += 1; k += 1
        elif c == "}": depth -= 1; k += 1
        else: k += 1
    return k

def scan_number(src, i):
    n = len(src); j = i
    if src[j] == "-": j += 1
    while j < n and (src[j].isdigit() or src[j] == "."): j += 1
    if j < n and src[j] in "eE":
        j += 1
        if j < n and src[j] in "+-": j += 1
        while j < n and src[j].isdigit(): j += 1
    return j

PUNCT = {"{": "LBrace", "}": "RBrace", "[": "LBracket", "]": "RBracket",
         "(": "LParen", ")": "RParen", ",": "Comma"}

def tokenize(src):
    tokens = []; i = 0; n = len(src)
    while i < n:
        c = src[i]
        if c in " \t\r": i += 1; continue
        if c == "\n": tokens.append(("Newline", i, i + 1, "\n")); i += 1; continue
        if c == "#" or (c == "/" and i + 1 < n and src[i + 1] == "/"):
            while i < n and src[i] != "\n": i += 1
            continue
        if c == "/" and i + 1 < n and src[i + 1] == "*":
            i += 2
            while i + 1 < n and not (src[i] == "*" and src[i + 1] == "/"): i += 1
            i = min(n, i + 2); continue
        if c == '"':
            e = scan_string(src, i); tokens.append(("String", i, e, src[i:e])); i = e; continue
        if c.isdigit() or (c == "-" and i + 1 < n and src[i + 1].isdigit()):
            e = scan_number(src, i); tokens.append(("Number", i, e, src[i:e])); i = e; continue
        if c.isalpha() or c == "_":
            e = i + 1
            while e < n and (src[e].isalnum() or src[e] in "_-"): e += 1
            tokens.append(("Identifier", i, e, src[i:e])); i = e; continue
        if c == "=" and not (i + 1 < n and src[i + 1] == "="):
            tokens.append(("Equals", i, i + 1, "=")); i += 1; continue
        tokens.append((PUNCT.get(c, "Other"), i, i + 1, c)); i += 1
    tokens.append(("Eof", n, n, ""))
    return tokens

def unquote(t):
    if len(t) >= 2 and t[0] == '"' and t[-1] == '"': return decode_string(t)
    return t

def decode_string(t):
    body = t[1:-1] if len(t) >= 2 and t[0] == '"' and t[-1] == '"' else t
    out = []; i = 0
    while i < len(body):
        c = body[i]
        if c == "\\" and i + 1 < len(body):
            nx = body[i + 1]; i += 2
            out.append({"n": "\n", "t": "\t", "r": "\r", '"': '"', "\\": "\\"}.get(nx, nx))
        else:
            out.append(c); i += 1
    return "".join(out)

def skip_braced(tokens, p):
    depth = 0
    while tokens[p][0] != "Eof":
        if tokens[p][0] == "LBrace": depth += 1
        elif tokens[p][0] == "RBrace":
            depth -= 1
            if depth == 0: return p + 1
        p += 1
    return p

def line_of(src, offset):
    return src.count("\n", 0, min(offset, len(src))) + 1

def read_outline(src):
    tokens = tokenize(src); handles = []; p = 0; index = 0
    while tokens[p][0] != "Eof":
        k = tokens[p][0]
        if k in ("Newline", "Comma"): p += 1; continue
        if k == "Identifier":
            btype = tokens[p][3]; start = tokens[p][1]; p += 1
            if tokens[p][0] == "Equals":
                # skip top-level assignment
                depth = 0
                while tokens[p][0] != "Eof":
                    tk = tokens[p][0]
                    if depth == 0 and tk == "Newline": p += 1; break
                    if tk in ("LBrace", "LBracket", "LParen"): depth += 1
                    elif tk in ("RBrace", "RBracket", "RParen"): depth -= 1
                    p += 1
                continue
            labels = []
            while tokens[p][0] in ("Identifier", "String"):
                labels.append(unquote(tokens[p][3])); p += 1
            if tokens[p][0] == "LBrace":
                after = skip_braced(tokens, p)
                end = tokens[after - 1][2]
                handles.append({"index": index, "type": btype, "labels": labels,
                                "start": start, "end": end,
                                "start_line": line_of(src, start), "end_line": line_of(src, end - 1)})
                index += 1; p = after
            continue
        p += 1
    return handles

def slice_toks(toks, a, b):
    return toks[a:min(b, len(toks))]

def split_top_level(toks, commas_only):
    segs = []; cur = []; depth = 0
    for t in toks:
        if t[0] in ("LBrace", "LBracket", "LParen"): depth += 1
        elif t[0] in ("RBrace", "RBracket", "RParen"): depth -= 1
        is_sep = depth == 0 and (t[0] == "Comma" or (not commas_only and t[0] == "Newline"))
        if is_sep:
            if cur: segs.append(cur)
            cur = []; continue
        if t[0] == "Newline": continue
        cur.append(t)
    if cur: segs.append(cur)
    return segs

def trim_newlines(tokens):
    a, b = 0, len(tokens)
    while a < b and tokens[a][0] == "Newline": a += 1
    while b > a and tokens[b - 1][0] == "Newline": b -= 1
    return tokens[a:b]

def classify(value_tokens, src):
    trimmed = trim_newlines(value_tokens)
    if not trimmed: return (None, False)
    raw = src[trimmed[0][1]:trimmed[-1][2]]
    sig = [t for t in trimmed if t[0] != "Newline"]
    if not sig: return (None, False)
    if len(sig) == 1:
        t = sig[0]
        if t[0] == "String":
            if "${" in t[3] or "%{" in t[3]: return (("raw", t[3]), False)
            return (("string", decode_string(t[3])), True)
        if t[0] == "Number": return (("number", t[3]), True)
        if t[0] == "Identifier":
            return {"true": (("bool", True), True), "false": (("bool", False), True),
                    "null": (("null",), True)}.get(t[3], (("raw", t[3]), False))
        return (("raw", raw), False)
    first, last = sig[0], sig[-1]
    if first[0] == "LBracket" and last[0] == "RBracket":
        inner = slice_toks(trimmed, 1, len(trimmed) - 1)
        items = []
        for seg in split_top_level(inner, True):
            if not seg: continue
            v, s = classify(seg, src)
            if not s or v is None: return (("raw", raw), False)
            items.append(v)
        return (("list", items), True)
    if first[0] == "LBrace" and last[0] == "RBrace":
        inner = slice_toks(trimmed, 1, len(trimmed) - 1)
        entries = []
        for entry in split_top_level(inner, False):
            if not entry: continue
            if len(entry) < 2 or entry[1][0] != "Equals" or entry[0][0] not in ("Identifier", "String"):
                return (("raw", raw), False)
            key = unquote(entry[0][3])
            v, s = classify(slice_toks(entry, 2, len(entry)), src)
            if not s or v is None: return (("raw", raw), False)
            entries.append((key, v))
        return (("object", entries), True)
    return (("raw", raw), False)

def read_arguments(src, handle):
    base = handle["start"]
    sub = src[handle["start"]:handle["end"]]
    tokens = tokenize(sub); args = []
    p = 0
    while tokens[p][0] not in ("LBrace", "Eof"): p += 1
    if tokens[p][0] != "LBrace": return args
    p += 1
    while tokens[p][0] not in ("RBrace", "Eof"):
        if tokens[p][0] in ("Newline", "Comma"): p += 1; continue
        if tokens[p][0] not in ("Identifier", "String"): p += 1; continue
        name = unquote(tokens[p][3]); p += 1
        if tokens[p][0] == "Equals":
            p += 1
            while tokens[p][0] == "Newline": p += 1
            vt = []; depth = 0
            while True:
                t = tokens[p]
                if t[0] == "Eof": break
                if depth == 0 and t[0] in ("Newline", "Comma", "RBrace"): break
                if t[0] in ("LBrace", "LBracket", "LParen"): depth += 1
                elif t[0] in ("RBrace", "RBracket", "RParen"): depth -= 1
                vt.append(t); p += 1
            if vt:
                vstart, vend = vt[0][1], vt[-1][2]
                value, simple = classify(vt, sub)
                args.append({"name": name, "raw": sub[vstart:vend],
                             "start": base + vstart, "end": base + vend,
                             "value": value, "simple": simple})
        else:
            while tokens[p][0] in ("Identifier", "String"): p += 1
            if tokens[p][0] == "LBrace": p = skip_braced(tokens, p)
            else: p += 1
    return args

def test_reader():
    with open(os.path.join(HERE, "sample.tf")) as f:
        src = f.read()
    handles = read_outline(src)
    check(len(handles) == 1, f"one top-level block, got {len(handles)}")
    h = handles[0]
    check(h["type"] == "resource" and h["labels"] == ["aws_instance", "web"], "resource block header")
    args = {a["name"]: a for a in read_arguments(src, h)}
    # ebs_block_device is a nested block, not an argument
    check("ebs_block_device" not in args, "nested block excluded from arguments")
    check(set(args.keys()) == {"ami", "instance_type", "count", "monitoring", "tags", "user_data"},
          f"direct args: {sorted(args.keys())}")
    check(args["ami"]["simple"] and args["ami"]["value"] == ("string", "ami-123"), "ami simple string")
    check(not args["instance_type"]["simple"], "instance_type reference is complex")
    check(args["count"]["simple"] and args["count"]["value"] == ("number", "2"), "count simple number")
    check(args["monitoring"]["simple"] and args["monitoring"]["value"] == ("bool", True), "monitoring simple bool")
    check(args["tags"]["simple"] and args["tags"]["value"] == ("object", [("Name", ("string", "web"))]),
          f'tags simple object: {args["tags"]["value"]}')
    check(not args["user_data"]["simple"], "user_data interpolation is complex")

    # value span integrity: the recorded span must exactly cover the source token(s)
    check(src[args["ami"]["start"]:args["ami"]["end"]] == '"ami-123"', "ami value span exact")

    # in-place literal edit splice: change ami-123 -> ami-999, everything else byte-preserved
    e = args["ami"]
    edited = src[:e["start"]] + '"ami-999"' + src[e["end"]:]
    check('"ami-999"' in edited, "edit applied")
    check("var.instance_type" in edited and "${file(\"init.sh\")}" in edited, "complex args preserved")
    check(edited.replace("ami-999", "ami-123") == src, "only the edited span changed")

# ============================ run ============================

if __name__ == "__main__":
    test_schema_parser()
    test_emitter()
    test_reader()
    print(f"ran {checks} checks")
    if failures:
        print(f"FAILED ({len(failures)}):")
        for f in failures:
            print("  -", f)
        sys.exit(1)
    print("all checks passed")
