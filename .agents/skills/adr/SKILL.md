---
name: adr
description: Draft an ADR for a decision I have already made. Argument: <the decision, and the alternatives I rejected>
---

# adr


I have decided: **$ARGUMENTS**

Claim the number from `origin/main`, never from this branch:

```
git ls-tree --name-only origin/main brain/decisions/
```

Take the first free number after the highest that prints. Two branches that each read their own
copy of the index pick the same number, and git merges both because the filenames differ — three
ADRs had to be renumbered after the fact for exactly that reason.

Draft an ADR under `brain/decisions/` using that number and the frontmatter of the existing files
(`kind: adr`, `id`, `status`, `date`). Follow the shape of `ADR-0017-playback-session.md`:
Context, Decision, Consequences, Alternatives rejected.

Rules:

- Record my decision. Do not choose for me, and do not improve the decision on the way in.
- State the alternatives I rejected and why, in my words.
- Add the row to `brain/decisions/decisions-index.md`.
- If the decision closes an issue in `docs/SDD.md` section 17, say so; do not edit the SDD here.
- Stop and ask if the decision as stated is not defensible at a review.

Source: `docs/prompt-library/PROMPTS.md` prompt 7.
