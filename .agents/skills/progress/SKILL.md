---
name: progress
description: Count how far the backend has got, and refresh the progress board at its fixed URL
---

# progress


Run `./.agent/progress.sh --write` and read `docs/progress/PROGRESS.json`.

Then refresh the progress board **in place**, at the URL it already has:

```
https://claude.ai/artifact/4fLzR9r23v7syYnYhUUoS7
```

Use the Artifact tool with `action: "read"` on that URL first, then publish the updated page back
to the **same** `url`. Publishing without it creates a second board, and a second board is a
second set of numbers to keep true.

What to carry into the page:

- every figure from `PROGRESS.json`, unchanged — the script counted them, do not recompute by eye
- the slice track from the `State` column of the `docs/DELIVERY-PLAN.md` slice table
- the ADR split from the `Lands in` column of `brain/decisions/decisions-index.md`, with the slice
  each waiting decision is waiting on
- the open rows of `docs/SDD.md` §17, with what each one blocks
- the commit and the date the snapshot names, in the page's provenance line
- the **last-refreshed stamp** in the page's top right corner: run `date -u "+%Y-%m-%d %H:%M UTC"`
  and write what it returns. Never the machine's local time, and never a time carried over from
  the version already published

Rules:

- **Report the counts, do not improve them.** A number that looks wrong is a document that needs
  fixing, not a number to adjust on the page. Say so instead.
- `designed` is a decision made, not a gap. Never present it as work missing.
- Change no file under `docs/` or `brain/` in this turn except `docs/progress/PROGRESS.json`,
  which the script writes.
- If `PROGRESS.json` disagrees with what a document claims in prose, name the mismatch in your
  reply. That is what `/brain-check` and `/sdd-sync` are for.

**The board counts `main`, and `main` can be a long way behind what is built.** Work arrives as
one branch and one pull request per spec, and a stack of them sits unmerged for days: on
2026-10-01 `main` held 11 specs and 15 endpoints while the tip of the stack held 17 and 47. So
check `gh pr list` as well, and say in your reply how much is built but not landed. The number on
the page is not wrong — it is answering "what has landed", which is a different question from
"what have we written". `make land` is what closes the gap.

Source: `.agent/progress.sh`, which reads two columns that are kept true by hand — the slice
`State` and the ADR `Lands in`. Everything else it counts.
