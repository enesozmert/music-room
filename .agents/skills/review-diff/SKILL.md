---
name: review-diff
description: Review the current diff against the standards that apply to the files it touches
---

# review-diff


Review the current diff against the standards that apply to the files it touches.

Output one line per finding: `path:line: severity: problem. fix.`
Order by severity. Skip formatting nits; `dotnet format` already gates those.
Say nothing about code that is fine.

Source: `docs/prompt-library/PROMPTS.md` prompt 12.
