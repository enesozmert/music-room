---
aliases: [obsidian-setup]
kind: setup
updated: 2026-09-23
---

# Obsidian Setup

Obsidian is the **human view** over files that already exist. It stores nothing of its own
beyond preferences, and nothing here is required for the Brain to work. Delete `.obsidian/`
and every note still reads correctly in any editor.

## Opening the vault

1. Obsidian → *Open folder as vault*
2. Choose `/Users/eozmert/ecole42/music-room` — the **repository root**, not `brain/`.

The root is the vault root on purpose: the Brain (`brain/`) and the standards library
(`docs/technical-guide/`) must be linkable to each other. A vault rooted at `brain/` could not
see the standards.

3. Open [[HOME]]. Set it as the startup note if you like: *Settings → Core plugins → Workspace*
   is not needed — just pin the tab.

## What is configured

`.obsidian/app.json` only:

| Setting | Value | Why |
|---|---|---|
| `useMarkdownLinks` | `false` | wikilinks, so notes stay readable and rename-safe |
| `newLinkFormat` | `shortest` | `[[system-overview]]`, not a full path |
| `alwaysUpdateLinks` | `true` | renaming a note fixes its backlinks |
| `attachmentFolderPath` | `docs/assets` | pasted images never land in `brain/` |
| `userIgnoreFilters` | see below | keeps search and graph honest |

Excluded from search and graph:

```
reference/          vendored study repo with its own git history
docs/subject/       binary PDF, distilled into subject-requirements
bin/  obj/          .NET build output
node_modules/       JS dependencies
```

No community plugin is installed and none is required. Core plugins in their default state are
enough: Graph view, Backlinks, Outgoing links, Search, Quick switcher.

## How links resolve

- Brain notes have unique filenames, so `[[system-overview]]` resolves directly.
- Standards are all named `STANDARD.md`, so each declares an `aliases:` entry in its frontmatter
  — `[[secrets-management]]` resolves through that alias. Do not remove the alias line when
  editing a standard.
- Links inside standards use relative paths (`../../40-security/...`). Those work in Obsidian and
  in GitHub's Markdown renderer alike.

## Rules

- **Do not** create a note per source file, class or function. The code is the source of truth
  for the code; the vault is for decisions, structure and rules.
- **Do not** create a note only to improve the graph. An artificial hub is a lie about the system.
- **Do not** let Obsidian move or rename files under `docs/technical-guide/` — the manifest and
  the PDF build script depend on the `<topic>/STANDARD.md` shape.
- Editing a note in Obsidian edits the Brain. Commit it like any other change.
- Attachments go to `docs/assets`. Keep `brain/` text-only.

## Suggested graph filter

To see only the knowledge layer, set the graph search to `path:brain OR path:docs/technical-guide`.
Local graph on [[MEMORY]] or [[knowledge-map]] shows the retrieval chain clearly.

## Related

[[HOME]] · [[user-manual]] · [[prompt-library]] · [[knowledge-map]] · [[MEMORY]] · [[technical-guide-index]]
