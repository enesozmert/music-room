---
name: brain-check
description: Audit the knowledge layer for contradictions and stale claims, without reading source code
---

# brain-check


Audit the knowledge layer without reading source code:

- `brain/` notes that contradict each other
- `UNKNOWN` markers that a closed ADR has already answered
- wikilinks that do not resolve
- standards listed in `brain/standards-router.md` that do not exist, or the reverse
- files, functions or flags named in `brain/` that no longer exist
- `docs/SDD.md` section 16 rows claiming more than the code supports

Report only real mismatches. Invent no work. Change no file.

Source: `docs/prompt-library/PROMPTS.md` prompt 15.
