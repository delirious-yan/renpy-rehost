# sdk-manifest.json

Pinned download verification for known Ren'Py releases. Embedded into
`RenpyRehost.Core`. Schema, keyed by `major.minor.patch`:

```json
{
  "7.4.11": {
    "sdk": { "size": 123456789, "sha256": "abc..." },
    "web": { "size": 12345678,  "sha256": "def..." }
  }
}
```

- `size` — exact byte length of the downloaded zip. Required for an entry to
  verify; a size mismatch fails the download.
- `sha256` — hex digest, lowercase. Optional but preferred.
- A version not listed here still downloads — unverified, with a warning.

Populate an entry after a known-good download: `rehost` prints the observed
size and SHA-256 of each SDK/web zip it fetches (verbose mode).
