---
description: Is a thing actually built? Evidence-backed verdict, read-only
argument-hint: <capability, endpoint or rule>
---

Read `brain/MEMORY.md` and follow its router. Do not scan the workspace.

Question: is **$ARGUMENTS** implemented?

Answer from evidence only:

- the types, endpoints, handlers or migrations that implement it, as `path:line` under
  `MusicRoom/` or `MusicRoomApp/`
- the tests that cover it, as `path:line`
- the deciding ADR from `brain/decisions/decisions-index.md`, by id, with its frontmatter status
- what `docs/SDD.md` section 16, `docs/DELIVERY-PLAN.md` and `docs/api/ENDPOINTS.md` claim about it
- one verdict, in the section 16 vocabulary: `done` / `partly implemented` / `designed` / `absent` /
  `claimed-but-absent`

`designed` means an Accepted ADR and no code. That is a normal state here, not a gap.
`absent` means no ADR and no code.
`claimed-but-absent` means section 16 says done or partly implemented and the code does not support
it. Quote the row and the code side by side.

Propose no work, write no code, change no file.

Source: `docs/prompt-library/PROMPTS.md` prompt 16.
