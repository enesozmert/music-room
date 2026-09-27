---
description: Draft an ADR for a decision I have already made
argument-hint: <the decision, and the alternatives I rejected>
---

I have decided: **$ARGUMENTS**

Draft an ADR under `brain/decisions/` using the numbering and frontmatter of the existing files
(`kind: adr`, `id`, `status`, `date`). Follow the shape of `ADR-0017-playback-session.md`:
Context, Decision, Consequences, Alternatives rejected.

Rules:

- Record my decision. Do not choose for me, and do not improve the decision on the way in.
- State the alternatives I rejected and why, in my words.
- Add the row to `brain/decisions/decisions-index.md`.
- If the decision closes an issue in `docs/SDD.md` section 17, say so; do not edit the SDD here.
- Stop and ask if the decision as stated is not defensible at a review.

Source: `docs/prompt-library/PROMPTS.md` prompt 7.
