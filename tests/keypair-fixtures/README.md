# Key-pair management fixtures (Phase 8.5)

`verify_keys.py` is a dependency-free Python reference port of the pure key logic in
`src/Fenrix.IaCStudio.Application/Security` — `SshPublicKey`, `PpkParser`,
`OpenSshPrivateKeyReader`. The C# mirrors this port, so agreement is evidence the C# is correct
(MAUI is not compiled in the authoring environment).

What it checks:

1. **OpenSSH SHA-256 fingerprint** computed against a *real, published* key — GitHub's ed25519
   host key `SHA256:+DiY3wvvV6TuJJhbpZisF/zLDA0zPMSvHdkr4UvCOqU`. This validates the exact
   fingerprint algorithm (`"SHA256:" + base64(sha256(blob))` with padding stripped) and the
   key-type read.
2. **SSH RSA wire encoder** + bit-size read-back, including the mpint `0x80` leading-zero padding
   path.
3. **PuTTY `.ppk` public-blob extraction** (algorithm/comment/Public-Lines) → same fingerprint.
4. **`openssh-key-v1` embedded public-blob extraction** round-trips (build container → parse → same
   fingerprint), the path that lets import derive the public key without decrypting the private half.

Run:

```
python3 verify_keys.py
```

> Not executed in the authoring session (the sandbox VM was unavailable). Run it locally to
> confirm the reference port passes; the C# was written to mirror it exactly.
