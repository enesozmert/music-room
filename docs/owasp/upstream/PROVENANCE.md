---
aliases: [owasp-provenance]
kind: provenance
updated: 2026-09-23
---

# Upstream OWASP Artifacts — Provenance

Verbatim upstream files. **Do not edit anything in `upstream/`.** Re-download to update.

## OWASP Application Security Verification Standard 5.0.0 (English)

| Field | Value |
|---|---|
| Project | OWASP ASVS |
| Version | 5.0.0 |
| Retrieved | 2026-09-23 |
| Source | `https://raw.githubusercontent.com/OWASP/ASVS/master/5.0/docs_en/` |
| Licence | Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0) — see `asvs-5.0.0/LICENSE.md` |
| Contents | 345 requirements, 17 chapters, level column `L` (1, 2, 3) |

### Files and checksums

```
98c8fe911b9edb403af8ee05d3ce8201ecac2659e313b053890a62847cdcf680  OWASP_Application_Security_Verification_Standard_5.0.0_en.csv
bcdbec214d70abcfad9284a31d4f9e5134305831d628aad3aa85d7e26626cb35  OWASP_Application_Security_Verification_Standard_5.0.0_en.json
28a9529c7d0bb4dc51f4bf5c116a3d16ef247a052f7591466768ddf563fd1cf5  LICENSE.md
```

Verify with `shasum -a 256 asvs-5.0.0/*`.

### Attribution and licence obligations

ASVS is licensed CC BY-SA 4.0. Two consequences for this repository:

1. **Attribution.** Any use must credit OWASP and the ASVS project. The files in `asvs-5.0.0/`
   are redistributed unmodified, with the upstream licence alongside them.
2. **Share-alike on adaptations.** Text adapted or substantially quoted from ASVS carries
   CC BY-SA 4.0. Our own documents in `docs/owasp/` therefore reference ASVS **by requirement id**
   rather than reproducing requirement text, which keeps them project material.

Quote an ASVS requirement verbatim only where the exact wording matters, and mark it as a quote.

## Not vendored

| Artifact | Why | Where to read it |
|---|---|---|
| OWASP Top 10 | short, stable, better read at the source | https://owasp.org/www-project-top-ten/ |
| OWASP API Security Top 10 | same | https://owasp.org/API-Security/ |
| OWASP Cheat Sheet Series | large and frequently updated; a stale local copy is worse than a link | https://cheatsheetseries.owasp.org/ |

Pointers to the cheat sheets that actually apply to this project are in `../CHEATSHEETS.md`.
