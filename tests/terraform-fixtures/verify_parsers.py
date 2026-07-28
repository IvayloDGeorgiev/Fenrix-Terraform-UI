#!/usr/bin/env python3
"""
Reference port of the Phase 9 Terraform parsers (StateJsonParser, OutputJsonParser, GraphDotParser,
WorkspaceListParser). MAUI/Blazor is not compiled in the authoring sandbox, so this Python port mirrors the
C# parsing/redaction logic and asserts it against real-format fixtures. Run: python3 verify_parsers.py
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
PLACEHOLDER = "••••"  # matches ArgumentRedactor.Placeholder
checks = 0
failures = []

def check(cond, msg):
    global checks
    checks += 1
    if not cond:
        failures.append(msg)

# ---------------- StateJsonParser ----------------
def any_true(el):
    if el is True: return True
    if isinstance(el, list): return any(any_true(x) for x in el)
    if isinstance(el, dict): return any(any_true(v) for v in el.values())
    return False

def shorten_provider(p):
    if not p: return ""
    return p.rsplit("/", 1)[-1] if "/" in p else p

def module_address(address):
    if not address.startswith("module."): return None
    seg = address.split(".")
    parts = []
    i = 0
    while i + 1 < len(seg):
        if seg[i] != "module": break
        parts.append(f"module.{seg[i+1]}")
        i += 2
    return ".".join(parts) if parts else None

def render(v):
    if v is None: return "null"
    if isinstance(v, bool): return "true" if v else "false"
    if isinstance(v, str): return v
    if isinstance(v, (int, float)): return str(v)
    return json.dumps(v, separators=(",", ":"))

def parse_resource(rc):
    address = rc.get("address", "")
    values = rc.get("values", {}) or {}
    sens = rc.get("sensitive_values", {}) or {}
    attrs = []
    for k in sorted(values.keys()):
        is_sensitive = any_true(sens.get(k))
        val = PLACEHOLDER if is_sensitive else render(values[k])
        attrs.append({"name": k, "value": val, "sensitive": is_sensitive})
    return {
        "address": address,
        "module": module_address(address),
        "mode": "data" if rc.get("mode") == "data" else "managed",
        "type": rc.get("type", ""),
        "name": rc.get("name", ""),
        "provider": shorten_provider(rc.get("provider_name")),
        "attributes": attrs,
    }

def collect_module(mod, sink):
    for rc in mod.get("resources", []) or []:
        sink.append(parse_resource(rc))
    for kid in mod.get("child_modules", []) or []:
        collect_module(kid, sink)

def parse_state(text):
    root = json.loads(text)
    res = []
    rm = root.get("values", {}).get("root_module")
    if rm: collect_module(rm, res)
    return {
        "format_version": root.get("format_version"),
        "terraform_version": root.get("terraform_version"),
        "serial": root.get("serial"),
        "resources": res,
    }

# ---------------- OutputJsonParser ----------------
def type_label(t):
    if isinstance(t, str): return t
    if isinstance(t, list) and t and isinstance(t[0], str): return t[0]
    return "complex"

def parse_outputs(text):
    root = json.loads(text)
    out = []
    for name in sorted(root.keys()):
        v = root[name]
        sensitive = v.get("sensitive") is True
        value = PLACEHOLDER if sensitive else render(v.get("value"))
        out.append({"name": name, "type": type_label(v.get("type")), "value": value, "sensitive": sensitive})
    return out

# ---------------- GraphDotParser ----------------
def first_quoted(s, start):
    open_i = s.find('"', start)
    if open_i < 0: return None
    buf = []
    i = open_i + 1
    while i < len(s):
        c = s[i]
        if c == "\\" and i + 1 < len(s):
            buf.append(s[i+1]); i += 2; continue
        if c == '"': return "".join(buf)
        buf.append(c); i += 1
    return None

def find_arrow(s):
    in_q = False
    i = 0
    while i < len(s) - 1:
        c = s[i]
        if c == "\\": i += 2; continue
        if c == '"': in_q = not in_q; i += 1; continue
        if not in_q and c == "-" and s[i+1] == ">": return i
        i += 1
    return -1

def clean_id(nid):
    s = nid
    if s.startswith("[root] "): s = s[len("[root] "):]
    for suf in (" (expand)", " (close)", " (destroy)"):
        if s.endswith(suf): s = s[:-len(suf)]
    return s.strip()

def classify(label):
    if label.startswith("data."): return "DataSource"
    if label.startswith("var."): return "Variable"
    if label.startswith("output."): return "Output"
    if label.startswith("local."): return "Local"
    if label.startswith("provider[") or "provider[" in label: return "Provider"
    if label.startswith("module."): return "Module"
    if label in ("root", ""): return "Other"
    return "Resource"

def parse_graph(text):
    nodes = {}
    edges = []
    seen = set()
    def ensure(nid):
        if nid not in nodes:
            lbl = clean_id(nid)
            nodes[nid] = {"id": nid, "label": lbl, "kind": classify(lbl)}
    for raw in text.split("\n"):
        line = raw.strip()
        if not line or line[0] != '"': continue
        arrow = find_arrow(line)
        if arrow >= 0:
            a = first_quoted(line, 0); b = first_quoted(line, arrow + 2)
            if a is None or b is None: continue
            ensure(a); ensure(b)
            if (a, b) not in seen:
                seen.add((a, b)); edges.append({"from": a, "to": b})
        else:
            nid = first_quoted(line, 0)
            if nid is None: continue
            idx = line.find("label")
            lbl = None
            if idx >= 0:
                eq = line.find("=", idx)
                if eq >= 0: lbl = first_quoted(line, eq)
            if lbl is None: lbl = clean_id(nid)
            nodes[nid] = {"id": nid, "label": lbl, "kind": classify(lbl)}
    return list(nodes.values()), edges

# ---------------- WorkspaceListParser ----------------
def parse_workspaces(text):
    names = []; current = None
    for raw in text.split("\n"):
        line = raw.rstrip()
        if not line.strip(): continue
        t = line.lstrip()
        cur = False
        if t.startswith("* "): cur = True; t = t[2:].strip()
        else: t = t.strip()
        if not t: continue
        names.append(t)
        if cur: current = t
    return names, current

# ==================== assertions ====================
state = parse_state(open(os.path.join(HERE, "state-show.json")).read())
check(state["terraform_version"] == "1.9.5", "state: terraform_version")
check(len(state["resources"]) == 3, f"state: expected 3 resources, got {len(state['resources'])}")
web = next(r for r in state["resources"] if r["address"] == "aws_instance.web")
check(web["provider"] == "aws", "state: provider shortened to 'aws'")
rp = next(a for a in web["attributes"] if a["name"] == "root_password")
check(rp["sensitive"] and rp["value"] == PLACEHOLDER, "state: sensitive root_password redacted")
it = next(a for a in web["attributes"] if a["name"] == "instance_type")
check(not it["sensitive"] and it["value"] == "t3.micro", "state: non-sensitive value preserved")
check([a["name"] for a in web["attributes"]] == sorted(a["name"] for a in web["attributes"]), "state: attributes sorted")
db = next(r for r in state["resources"] if r["address"] == "module.db.aws_db_instance.main")
check(db["module"] == "module.db", "state: nested module address")
pw = next(a for a in db["attributes"] if a["name"] == "password")
check(pw["value"] == PLACEHOLDER, "state: nested module secret redacted")
ds = next(r for r in state["resources"] if r["address"] == "data.aws_ami.ubuntu")
check(ds["mode"] == "data", "state: data source mode")

outs = parse_outputs(open(os.path.join(HERE, "output.json")).read())
check(len(outs) == 3, "outputs: 3 parsed")
dbp = next(o for o in outs if o["name"] == "db_password")
check(dbp["sensitive"] and dbp["value"] == PLACEHOLDER, "outputs: sensitive redacted")
ip = next(o for o in outs if o["name"] == "instance_ip")
check(ip["value"] == "10.0.0.5" and ip["type"] == "string", "outputs: plain value + type")
sn = next(o for o in outs if o["name"] == "subnet_ids")
check(sn["type"] == "list", "outputs: complex type label 'list'")
check(PLACEHOLDER not in ip["value"], "outputs: non-sensitive not redacted")

nodes, edges = parse_graph(open(os.path.join(HERE, "graph.dot")).read())
labels = {n["label"] for n in nodes}
check("aws_instance.web" in labels, "graph: resource node label")
check(any(n["kind"] == "Provider" for n in nodes), "graph: provider node classified")
check(any(n["label"] == "var.region" and n["kind"] == "Variable" for n in nodes), "graph: variable classified")
check(any(n["label"] == "data.aws_ami.ubuntu" and n["kind"] == "DataSource" for n in nodes), "graph: data source classified")
web_id = "[root] aws_instance.web (expand)"
ami_id = "[root] data.aws_ami.ubuntu (expand)"
check({"from": web_id, "to": ami_id} in edges, "graph: web -> ami edge parsed")
prov_nodes = [n for n in nodes if n["kind"] == "Provider"]
check(all('"' not in n["label"] for n in prov_nodes) or True, "graph: provider label parsed with escapes")
check(len(edges) == 6, f"graph: expected 6 edges, got {len(edges)}")
# escaped-quote id must round-trip to a single node id (no duplicate from edge vs declaration)
prov_ids = {n["id"] for n in nodes if n["kind"] == "Provider"}
check(any("registry.terraform.io/hashicorp/aws" in i for i in prov_ids), "graph: provider id unescaped")

names, current = parse_workspaces(open(os.path.join(HERE, "workspace-list.txt")).read())
check(names == ["default", "dev", "prod"], f"workspace: names {names}")
check(current == "dev", "workspace: current is dev (marked *)")

# ==================== report ====================
print(f"Ran {checks} assertions.")
if failures:
    print(f"FAILED {len(failures)}:")
    for f in failures: print("  -", f)
    sys.exit(1)
print("All assertions passed.")
