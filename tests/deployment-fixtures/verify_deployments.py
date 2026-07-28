#!/usr/bin/env python3
"""
Phase 9.5 — CI/CD pipelines & deployments cross-check.

A dependency-free Python reference port of the three pure-logic pieces in
src/Fenrix.IaCStudio.Application/Deployments:

  * SemVerLabel            — tolerant semver parse + precedence (prerelease < release)
  * VersionMatrixBuilder   — versions × environments deploy-state grid (current/previous/available)
  * DeploymentGateEvaluator — non-interactive stage gates + interactive-gate requirements

The C# mirrors this port, so agreement here is strong evidence the C# logic is correct
(MAUI is not compiled in the authoring environment).

Run:  python3 verify_deployments.py
"""

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
# SemVerLabel
# --------------------------------------------------------------------------------------

def parse_semver(label):
    raw = (label or "").strip()
    core = raw
    pre = None
    if core[:1] in ("v", "V"):
        core = core[1:]
    if "-" in core:
        i = core.index("-")
        pre = core[i + 1:]
        core = core[:i]
    if "+" in core:
        core = core[:core.index("+")]
    parts = [p for p in core.split(".") if p != ""]
    major = minor = patch = 0
    ok = len(parts) > 0

    def try_int(s):
        return s.isdigit()

    if ok and not try_int(parts[0]):
        ok = False
    else:
        if ok:
            major = int(parts[0])
    if ok and len(parts) > 1:
        if try_int(parts[1]):
            minor = int(parts[1])
        else:
            ok = False
    if ok and len(parts) > 2:
        if try_int(parts[2]):
            patch = int(parts[2])
        else:
            ok = False
    return {"major": major, "minor": minor, "patch": patch,
            "pre": pre if pre else None, "orig": raw, "is_semver": ok}


def cmp_prerelease(a, b):
    ap = a.split(".")
    bp = b.split(".")
    n = max(len(ap), len(bp))
    for i in range(n):
        if i >= len(ap):
            return -1
        if i >= len(bp):
            return 1
        an, bn = ap[i].isdigit(), bp[i].isdigit()
        if an and bn:
            c = (int(ap[i]) > int(bp[i])) - (int(ap[i]) < int(bp[i]))
        elif an:
            c = -1
        elif bn:
            c = 1
        else:
            c = (ap[i] > bp[i]) - (ap[i] < bp[i])
        if c != 0:
            return c
    return 0


def cmp_semver(x, y):
    if not x["is_semver"] or not y["is_semver"]:
        return (x["orig"] > y["orig"]) - (x["orig"] < y["orig"])
    for k in ("major", "minor", "patch"):
        c = (x[k] > y[k]) - (x[k] < y[k])
        if c != 0:
            return c
    if x["pre"] is None and y["pre"] is None:
        return 0
    if x["pre"] is None:
        return 1
    if y["pre"] is None:
        return -1
    return cmp_prerelease(x["pre"], y["pre"])


def sv(label):
    return parse_semver(label)


def test_semver():
    print("SemVerLabel")
    check("parse 1.5 core", (sv("1.5")["major"], sv("1.5")["minor"]), (1, 5))
    check("strip leading v", sv("v2.0.1")["patch"], 1)
    check("prerelease captured", sv("2.0.0-rc.1")["pre"], "rc.1")
    check("build metadata dropped", sv("1.2.3+build")["pre"], None)
    check("release > prerelease", cmp_semver(sv("2.0.0"), sv("2.0.0-rc")) > 0, True)
    check("2.0.0 > 1.9.9", cmp_semver(sv("2.0.0"), sv("1.9.9")) > 0, True)
    check("1.5 > 1.0", cmp_semver(sv("1.5"), sv("1.0")) > 0, True)
    check("rc.1 < rc.2", cmp_semver(sv("1.0.0-rc.1"), sv("1.0.0-rc.2")) < 0, True)
    check("numeric pre < alpha pre", cmp_semver(sv("1.0.0-1"), sv("1.0.0-alpha")) < 0, True)
    check("non-semver ordinal", cmp_semver(sv("hotfix"), sv("hotfix")) == 0, True)
    check("equal cores no pre", cmp_semver(sv("1.0.0"), sv("1.0")) == 0, True)


# --------------------------------------------------------------------------------------
# VersionMatrixBuilder
# --------------------------------------------------------------------------------------

CURRENT, PREVIOUS, AVAILABLE = "current", "previous", "available"


def build_matrix(envs, versions, deployments):
    """envs: [id]; versions: [(id, created)]; deployments: [(vid, envid, status, when)]."""
    current_by_env = {}
    for e in envs:
        succ = [d for d in deployments if d[1] == e and d[2] == "Succeeded"]
        if succ:
            latest = max(succ, key=lambda d: d[3])
            current_by_env[e] = latest[0]

    succeeded_at = {}
    for d in deployments:
        if d[2] != "Succeeded":
            continue
        key = (d[0], d[1])
        if key not in succeeded_at or d[3] > succeeded_at[key]:
            succeeded_at[key] = d[3]

    ordered = sorted(versions, key=lambda v: v[1], reverse=True)
    rows = []
    for v in ordered:
        cells = []
        for e in envs:
            deployed = (v[0], e) in succeeded_at
            is_current = current_by_env.get(e) == v[0]
            state = CURRENT if is_current else PREVIOUS if deployed else AVAILABLE
            cells.append((e, state))
        rows.append((v[0], cells))
    return rows


def test_matrix():
    print("VersionMatrixBuilder")
    # Scenario: v2 on Dev, v1.5 on UAT, v1 on Live (doc's worked example).
    envs = ["dev", "uat", "live"]
    versions = [("v1", 1), ("v15", 2), ("v2", 3)]
    deployments = [
        ("v1", "dev", "Succeeded", 10),
        ("v1", "uat", "Succeeded", 11),
        ("v1", "live", "Succeeded", 12),
        ("v15", "dev", "Succeeded", 20),
        ("v15", "uat", "Succeeded", 21),
        ("v2", "dev", "Succeeded", 30),
    ]
    rows = build_matrix(envs, versions, deployments)
    # rows ordered newest-first: v2, v15, v1
    check("row order newest first", [r[0] for r in rows], ["v2", "v15", "v1"])
    cell = {(r[0], c[0]): c[1] for r in rows for c in r[1]}
    check("v2 current on dev", cell[("v2", "dev")], CURRENT)
    check("v2 available on uat", cell[("v2", "uat")], AVAILABLE)
    check("v2 available on live", cell[("v2", "live")], AVAILABLE)
    check("v15 current on uat", cell[("v15", "uat")], CURRENT)
    check("v15 previous on dev", cell[("v15", "dev")], PREVIOUS)
    check("v1 current on live", cell[("v1", "live")], CURRENT)
    check("v1 previous on dev", cell[("v1", "dev")], PREVIOUS)
    check("v1 previous on uat", cell[("v1", "uat")], PREVIOUS)
    # Failed deployment does not make a version current.
    rows2 = build_matrix(["dev"], [("a", 1)], [("a", "dev", "Failed", 5)])
    cell2 = {(r[0], c[0]): c[1] for r in rows2 for c in r[1]}
    check("failed deploy = available", cell2[("a", "dev")], AVAILABLE)


# --------------------------------------------------------------------------------------
# DeploymentGateEvaluator
# --------------------------------------------------------------------------------------

def evaluate_gates(i):
    gates = []
    gates.append(("CloudConnection", i["has_cloud"], True))
    at_version = i["current_commit"] is not None and \
        i["current_commit"].lower() == i["version_commit"].lower()
    gates.append(("RepositoryAtVersion", at_version, True))
    if i.get("required_branch"):
        branch_ok = i["current_branch"] == i["required_branch"]
        gates.append(("RequiredBranch", branch_ok, True))
    if i.get("require_clean"):
        gates.append(("CleanWorkingTree", not i["dirty"], True))
    if i.get("require_prev"):
        passed = i["prev_has_version"] if i["prev_has_version"] is not None else True
        gates.append(("PreviousStageSuccess", passed, True))
    requires_typed = i["is_production"] and i.get("require_typed_prod", True)
    return gates, i.get("require_approval", False), requires_typed


def base_inputs(**over):
    i = {
        "has_cloud": True, "is_production": False,
        "version_commit": "abc123", "current_commit": "abc123",
        "current_branch": "main", "dirty": False,
        "require_approval": False, "require_prev": False,
        "require_clean": False, "require_typed_prod": True,
        "required_branch": None, "prev_has_version": None,
    }
    i.update(over)
    return i


def blocking_pass(gates):
    return all(p for (_, p, blocker) in gates if blocker)


def test_gates():
    print("DeploymentGateEvaluator")
    g, appr, typed = evaluate_gates(base_inputs())
    check("clean happy path passes", blocking_pass(g), True)
    check("no approval by default", appr, False)
    check("no typed confirm (non-prod)", typed, False)

    g, _, typed = evaluate_gates(base_inputs(is_production=True))
    check("production requires typed confirm", typed, True)

    g, _, typed = evaluate_gates(base_inputs(is_production=True, require_typed_prod=False))
    check("prod typed can be disabled", typed, False)

    g, _, _ = evaluate_gates(base_inputs(has_cloud=False))
    check("unbound cloud blocks", blocking_pass(g), False)

    g, _, _ = evaluate_gates(base_inputs(current_commit="deadbeef"))
    check("wrong commit blocks (repo-at-version)", blocking_pass(g), False)

    g, _, _ = evaluate_gates(base_inputs(required_branch="release", current_branch="main"))
    check("wrong branch blocks", blocking_pass(g), False)

    g, _, _ = evaluate_gates(base_inputs(required_branch="main", current_branch="main"))
    check("right branch passes", blocking_pass(g), True)

    g, _, _ = evaluate_gates(base_inputs(require_clean=True, dirty=True))
    check("dirty tree blocks when required", blocking_pass(g), False)

    g, _, _ = evaluate_gates(base_inputs(require_prev=True, prev_has_version=False))
    check("previous stage missing version blocks", blocking_pass(g), False)

    g, _, _ = evaluate_gates(base_inputs(require_prev=True, prev_has_version=None))
    check("first stage passes previous-stage gate", blocking_pass(g), True)

    _, appr, _ = evaluate_gates(base_inputs(require_approval=True))
    check("approval flagged as required", appr, True)


def main():
    test_semver()
    test_matrix()
    test_gates()
    print(f"\n{passed} passed, {failed} failed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
