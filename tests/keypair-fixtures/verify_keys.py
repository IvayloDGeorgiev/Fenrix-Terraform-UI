#!/usr/bin/env python3
"""
Phase 8.5 — key-pair management cross-check.

A dependency-free Python reference port of the pure key logic in
src/Fenrix.IaCStudio.Application/Security (SshPublicKey, PpkParser, OpenSshPrivateKeyReader).
It validates the OpenSSH SHA-256 fingerprint algorithm against a REAL, published key
(GitHub's ed25519 host key) and round-trips the SSH wire encoders + the PPK / openssh-key-v1
public-blob extraction. The C# implementation mirrors this port line for line, so agreement here
is strong evidence the C# is correct (MAUI is not compiled in the authoring environment).

Run:  python3 verify_keys.py
"""

import base64
import hashlib
import struct
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
        print(f"  FAIL {name}\n         got:  {got!r}\n         want: {want!r}")


# ---- SSH wire primitives (mirror SshPublicKey.cs) ----

def ssh_string(b: bytes) -> bytes:
    return struct.pack(">I", len(b)) + b


def ssh_mpint(magnitude: bytes) -> bytes:
    i = 0
    while i < len(magnitude) - 1 and magnitude[i] == 0:
        i += 1
    trimmed = magnitude[i:]
    if len(trimmed) == 1 and trimmed[0] == 0:
        return ssh_string(b"")
    if trimmed[0] & 0x80:
        trimmed = b"\x00" + trimmed
    return ssh_string(trimmed)


def read_first_string(blob: bytes) -> str:
    if len(blob) < 4:
        return ""
    (n,) = struct.unpack(">I", blob[:4])
    if n == 0 or 4 + n > len(blob):
        return ""
    return blob[4:4 + n].decode("ascii", "replace")


def read_field(blob, pos):
    (n,) = struct.unpack(">I", blob[pos:pos + 4])
    pos += 4
    return blob[pos:pos + n], pos + n


def fingerprint(blob: bytes) -> str:
    return "SHA256:" + base64.b64encode(hashlib.sha256(blob).digest()).decode().rstrip("=")


def build_rsa_blob(modulus: bytes, exponent: bytes) -> bytes:
    return ssh_string(b"ssh-rsa") + ssh_mpint(exponent) + ssh_mpint(modulus)


def rsa_bits(blob: bytes):
    pos = 0
    _, pos = read_field(blob, pos)   # "ssh-rsa"
    _, pos = read_field(blob, pos)   # e
    n, pos = read_field(blob, pos)   # modulus
    i = 0
    while i < len(n) and n[i] == 0:
        i += 1
    sig = len(n) - i
    return sig * 8 if sig > 0 else None


# ---- PPK public-blob extraction (mirror PpkParser.cs) ----

def parse_ppk_public(text: str):
    lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    algorithm = ""
    comment = None
    pub = []
    i = 0
    while i < len(lines):
        line = lines[i]
        for key in ("PuTTY-User-Key-File-2", "PuTTY-User-Key-File-3"):
            if line.startswith(key + ":"):
                algorithm = line[len(key) + 1:].strip()
        if line.startswith("Comment:"):
            comment = line[len("Comment:"):].strip()
        if line.startswith("Public-Lines:"):
            count = int(line[len("Public-Lines:"):].strip())
            for _ in range(count):
                i += 1
                pub.append(lines[i].strip())
        i += 1
    return algorithm, comment, base64.b64decode("".join(pub))


# ---- openssh-key-v1 embedded public blob (mirror OpenSshPrivateKeyReader.cs) ----

def parse_openssh_public(text: str):
    start = text.index("-----BEGIN OPENSSH PRIVATE KEY-----") + len("-----BEGIN OPENSSH PRIVATE KEY-----")
    end = text.index("-----END OPENSSH PRIVATE KEY-----", start)
    body = base64.b64decode("".join(text[start:end].split()))
    magic = b"openssh-key-v1\x00"
    assert body[:len(magic)] == magic, "bad magic"
    pos = len(magic)
    _, pos = read_field(body, pos)   # ciphername
    _, pos = read_field(body, pos)   # kdfname
    _, pos = read_field(body, pos)   # kdfoptions
    (count,) = struct.unpack(">I", body[pos:pos + 4]); pos += 4
    assert count >= 1
    blob, pos = read_field(body, pos)
    return blob


def build_openssh_container(pubblob: bytes) -> str:
    body = (b"openssh-key-v1\x00"
            + ssh_string(b"none") + ssh_string(b"none") + ssh_string(b"")
            + struct.pack(">I", 1)
            + ssh_string(pubblob)
            + ssh_string(b"\x00\x00\x00\x00private-omitted"))
    b64 = base64.b64encode(body).decode()
    wrapped = "\n".join(b64[i:i + 70] for i in range(0, len(b64), 70))
    return f"-----BEGIN OPENSSH PRIVATE KEY-----\n{wrapped}\n-----END OPENSSH PRIVATE KEY-----\n"


def main():
    print("Phase 8.5 key-pair cross-check\n")

    # 1) Fingerprint against a REAL published key: GitHub's ed25519 host key.
    gh_b64 = "AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl"
    gh_fp = "SHA256:+DiY3wvvV6TuJJhbpZisF/zLDA0zPMSvHdkr4UvCOqU"
    blob = base64.b64decode(gh_b64)
    check("ed25519 type name", read_first_string(blob), "ssh-ed25519")
    check("ed25519 fingerprint == GitHub published", fingerprint(blob), gh_fp)

    # 2) RSA wire encode + bit-size read-back (mpint 0x80 padding path).
    e = b"\x01\x00\x01"
    n = bytes([0x80] + [0x11] * 255)           # 256 bytes, top bit set -> mpint pads a 0x00
    rblob = build_rsa_blob(n, e)
    check("rsa type name", read_first_string(rblob), "ssh-rsa")
    check("rsa bit size", rsa_bits(rblob), 2048)

    # 3) PPK public extraction -> same blob/fingerprint as the raw key.
    ppk = ("PuTTY-User-Key-File-3: ssh-ed25519\n"
           "Encryption: none\n"
           "Comment: bastion\n"
           "Public-Lines: 1\n"
           f"{gh_b64}\n"
           "Private-Lines: 1\n"
           "AAAA\n"
           "Private-MAC: 00\n")
    alg, comment, pblob = parse_ppk_public(ppk)
    check("ppk algorithm", alg, "ssh-ed25519")
    check("ppk comment", comment, "bastion")
    check("ppk public fingerprint matches", fingerprint(pblob), gh_fp)

    # 4) openssh-key-v1 embedded public extraction round-trips.
    container = build_openssh_container(blob)
    extracted = parse_openssh_public(container)
    check("openssh embedded blob matches", fingerprint(extracted), gh_fp)

    print(f"\n{passed} passed, {failed} failed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
