---
description: Bring docs/SDD.md back in line with the code after a change lands
argument-hint: <decision or design change that landed>
---

**$ARGUMENTS** landed.

Update `docs/SDD.md`: the affected viewpoint section, the section 16 traceability table, and the
section 17 issue tables if this opened or closed one.

Rules:

- do not restate ADR rationale in the SDD; reference the ADR
- never write `done` in section 16 for anything with no code
- leave `UNKNOWN - requires decision` where a decision is genuinely missing
- regenerate the PDF with `docs/build-pdf.sh`

Source: `docs/prompt-library/PROMPTS.md` prompt 11.
