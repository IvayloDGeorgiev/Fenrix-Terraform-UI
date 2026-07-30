#!/usr/bin/env python3
"""
Phase 11 — Enterprise capability cross-check.

A dependency-free Python reference port of the four pure-logic pieces in
src/Fenrix.IaCStudio.Application/Enterprise:

  * PermissionEvaluator  — union of in-scope role grants (Global/Project/Environment) + Has()
  * PolicyEvaluator      — org policy: approval requirements + hard blocks (+ TF-version check)
  * TemplateInstantiator — typed {{placeholder}} substitution (String quoted; others raw)
  * ApprovalResolver     — separation-of-duties + permission gate; approval validity/expiry

The C# mirrors this port, so agreement here is strong evidence the C# logic is correct
(MAUI is not compiled in the authoring environment; the sandbox VM was down this session, so
this port was hand-traced, not executed — run it in a Python environment).

Run:  python3 verify_enterprise.py
"""

import re
import sys

passed = 0
failed = 0


def check(name, got, want):
    global passed, failed
    if got == want:
        passed += 1
        print(f"  ok   {name}")
    else:
        failed += 1
        print(f"  FAIL {name}: got {got!r}, want {want!r}")


# --------------------------------------------------------------------------------------
# Permission flags (mirror Domain/Enterprise/EnterpriseEnums.cs)
# --------------------------------------------------------------------------------------

P_VIEW = 1 << 0
P_PLAN = 1 << 1
P_APPLY = 1 << 2
P_APPLY_PROD = 1 << 3
P_DESTROY = 1 << 4
P_STATE = 1 << 5
P_UNLOCK = 1 << 6
P_CONN = 1 << 7
P_EXPORT = 1 << 8
P_APPROVE = 1 << 9
P_TEMPLATES = 1 << 10
P_POLICY = 1 << 11
P_ROLES = 1 << 12
P_AUDIT = 1 << 13
P_ALL = (1 << 14) - 1

GLOBAL, PROJECT, ENVIRONMENT = "Global", "Project", "Environment"

OPERATOR = P_VIEW | P_PLAN | P_APPLY | P_STATE | P_CONN
APPROVER = P_VIEW | P_AUDIT | P_APPROVE


def grant_applies(scope, g_proj, g_env, proj, env):
    if scope == GLOBAL:
        return True
    if scope == PROJECT:
        return proj is not None and g_proj == proj
    if scope == ENVIRONMENT:
        return env is not None and g_env == env
    return False


def effective(grants, proj, env):
    result = 0
    for (scope, g_proj, g_env, perms) in grants:
        if grant_applies(scope, g_proj, g_env, proj, env):
            result |= perms
    return result


def has(eff, required):
    return (eff & required) == required


# Global Operator + an Environment-scoped RunApplyProduction grant on env E1.
grants = [
    (GLOBAL, None, None, OPERATOR),
    (ENVIRONMENT, None, "E1", P_APPLY_PROD),
]

check("global operator can plan anywhere", has(effective(grants, "P1", None), P_PLAN), True)
check("operator cannot destroy", has(effective(grants, "P1", None), P_DESTROY), False)
check("prod-apply only on E1", has(effective(grants, "P1", "E1"), P_APPLY_PROD), True)
check("prod-apply not on E2", has(effective(grants, "P1", "E2"), P_APPLY_PROD), False)
check("global grant still applies at env scope", has(effective(grants, "P1", "E1"), P_APPLY), True)

# Project-scoped ManageState on P2 only.
grants2 = [(PROJECT, "P2", None, P_STATE)]
check("project grant applies to its project", has(effective(grants2, "P2", None), P_STATE), True)
check("project grant not on other project", has(effective(grants2, "P3", None), P_STATE), False)
check("project grant needs a project id", has(effective(grants2, None, None), P_STATE), False)
check("no grants => nothing", effective([], "P1", "E1"), 0)
check("admin has everything", has(P_ALL, P_ROLES | P_POLICY | P_APPROVE), True)


# --------------------------------------------------------------------------------------
# PolicyEvaluator (mirror Application/Enterprise/PolicyEvaluator.cs)
# --------------------------------------------------------------------------------------

def eval_policy(policy, is_prod, env_name, is_destroy, branch, repo_private):
    if policy is None:
        return (False, False, 0)
    reasons = 0
    blocked = False
    approval = False
    if is_prod and policy.get("approveProd"):
        approval = True
        reasons += 1
    if any(n.lower() == env_name.lower() for n in policy.get("approveEnvs", [])):
        approval = True
        reasons += 1
    if is_destroy and is_prod and policy.get("blockProdDestroy"):
        blocked = True
        reasons += 1
    req_branch = policy.get("requiredBranch")
    if is_prod and req_branch:
        if branch != req_branch:
            blocked = True
            reasons += 1
    if policy.get("requirePrivate") and repo_private is False:
        blocked = True
        reasons += 1
    return (blocked, approval, reasons)


pol = {
    "approveProd": True,
    "approveEnvs": ["staging"],
    "blockProdDestroy": True,
    "requiredBranch": "main",
    "requirePrivate": True,
}

check("policy off => clear", eval_policy(None, True, "Live", True, "x", False), (False, False, 0))
# Prod apply on main, private repo: approval required (prod), not blocked.
check("prod apply needs approval", eval_policy(pol, True, "Live", False, "main", True), (False, True, 1))
# Named env approval.
check("named env needs approval", eval_policy(pol, False, "staging", False, "main", True), (False, True, 1))
# Prod destroy blocked.
check("prod destroy blocked", eval_policy(pol, True, "Live", True, "main", True)[0], True)
# Wrong branch on prod blocks.
check("wrong prod branch blocks", eval_policy(pol, True, "Live", False, "dev", True)[0], True)
# Public repo blocks when private required.
check("public repo blocks", eval_policy(pol, False, "Dev", False, "main", False)[0], True)
# Dev, private, main: nothing.
check("dev clean => clear", eval_policy(pol, False, "Dev", False, "main", True), (False, False, 0))


def satisfies(version, constraint):
    # Minimal ">= X.Y.Z" comparator for the test (the C# uses the full Phase 3 grammar).
    m = re.match(r">=\s*(\d+)\.(\d+)\.(\d+)", constraint)
    if not m:
        return True
    lo = tuple(int(x) for x in m.groups())
    vm = re.match(r"v?(\d+)\.(\d+)\.(\d+)", version)
    if not vm:
        return False
    return tuple(int(x) for x in vm.groups()) >= lo


def check_tf(constraint, version):
    if not constraint:
        return None
    if not version:
        return "unknown"
    return None if satisfies(version, constraint) else "blocked"


check("tf version allowed", check_tf(">= 1.5.0", "1.6.2"), None)
check("tf version blocked", check_tf(">= 1.5.0", "1.4.9"), "blocked")
check("tf no constraint => allowed", check_tf(None, "0.1.0"), None)


# --------------------------------------------------------------------------------------
# TemplateInstantiator (mirror Application/Enterprise/TemplateInstantiator.cs)
# --------------------------------------------------------------------------------------

PLACEHOLDER = re.compile(r"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")


def escape_hcl(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


def instantiate(body, params, values):
    # params: name -> (type, default, required); type in String/Number/Bool/Expression
    missing, unknown = set(), set()

    def repl(m):
        name = m.group(1)
        if name not in params:
            unknown.add(name)
            return m.group(0)
        ptype, default, required = params[name]
        raw = values.get(name) or default
        if not raw:
            if required:
                missing.add(name)
            return raw or ""
        if ptype == "String":
            return f'"{escape_hcl(raw)}"'
        return raw

    hcl = PLACEHOLDER.sub(repl, body)
    return (len(missing) == 0, hcl, sorted(missing), sorted(unknown))


body = 'resource "aws_s3_bucket" "b" {\n  bucket = {{name}}\n  count  = {{n}}\n}\n'
params = {
    "name": ("String", None, True),
    "n": ("Number", "1", False),
}

ok, hcl, missing, unknown = instantiate(body, params, {"name": "my-bucket"})
check("template ok when required present", ok, True)
check("string param quoted", '"my-bucket"' in hcl, True)
check("number param raw (default)", "count  = 1" in hcl, True)
check("no missing", missing, [])

ok2, _, missing2, _ = instantiate(body, params, {})
check("missing required flagged", (ok2, missing2), (False, ["name"]))

ok3, _, _, unknown3 = instantiate("x = {{ghost}}\n", {}, {})
check("unknown placeholder flagged", unknown3, ["ghost"])

_, hcl4, _, _ = instantiate('v = {{q}}\n', {"q": ("String", None, True)}, {"q": 'a"b\\c'})
check("string escaping", hcl4.strip(), 'v = "a\\"b\\\\c"')


# --------------------------------------------------------------------------------------
# ApprovalResolver (mirror Application/Enterprise/ApprovalResolver.cs)
# --------------------------------------------------------------------------------------

PENDING, APPROVED, REJECTED, CANCELLED, EXPIRED = "Pending", "Approved", "Rejected", "Cancelled", "Expired"


def can_decide(status, requester, decider, decider_perms):
    if status != PENDING:
        return (False, "not pending")
    if requester == decider:
        return (False, "self")
    if not has(decider_perms, P_APPROVE):
        return (False, "no perm")
    return (True, None)


def authorises(status, expires_at, now):
    return status == APPROVED and (expires_at is None or expires_at > now)


def effective_status(status, expires_at, now):
    if status == PENDING and expires_at is not None and expires_at <= now:
        return EXPIRED
    return status


check("approver can decide", can_decide(PENDING, "alice", "bob", APPROVER)[0], True)
check("requester cannot self-approve", can_decide(PENDING, "alice", "alice", APPROVER), (False, "self"))
check("no permission cannot decide", can_decide(PENDING, "alice", "bob", OPERATOR), (False, "no perm"))
check("already decided cannot decide", can_decide(APPROVED, "alice", "bob", APPROVER), (False, "not pending"))
check("approved authorises deploy", authorises(APPROVED, None, 100), True)
check("rejected does not authorise", authorises(REJECTED, None, 100), False)
check("expired approval does not authorise", authorises(APPROVED, 50, 100), False)
check("unexpired approval authorises", authorises(APPROVED, 150, 100), True)
check("pending past expiry => expired", effective_status(PENDING, 50, 100), EXPIRED)
check("pending before expiry => pending", effective_status(PENDING, 150, 100), PENDING)


# --------------------------------------------------------------------------------------
print()
print(f"{passed} passed, {failed} failed")
sys.exit(1 if failed else 0)
