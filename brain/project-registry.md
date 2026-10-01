---
kind: registry
updated: 2026-09-28
---

# Project Registry

Every component in this workspace. One row per component. Cards hold the detail.

| Component | Type | Status | Stack | Card |
|---|---|---|---|---|
| MusicRoom.Backend | backend / REST + realtime API | identity, credential rate limits, the audit log, bearer authentication and own-profile endpoints are built; realtime, events, playlists and delegation are designed only. All 3 services in scope. Status per unit: `docs/specs/` | .NET 10, ASP.NET Core, EF Core, SignalR | [[musicroom-backend]] |
| MusicRoom.Mobile | mobile client at `MusicRoomApp/` | parallel workstream, separate owner | React Native | [[musicroom-mobile]] |
| nArchitecture | reference material — **read only, never edited** | external, frozen | .NET 6 Clean Architecture | [[reference-narchitecture]] |
| docs/subject | authoritative requirements | fixed, v6 | PDF | [[subject-requirements]] |
| docs/technical-guide | engineering standards library | active | Markdown + PDF | [[standards-router]] |
| brain | AI retrieval layer | active | Markdown | [[MEMORY]] |

## Directory map (top level only)

```
music-room/
├── MusicRoom/            # backend source code (.NET 10). 13 projects: 5 application, 4 corePackages, 4 test.
├── MusicRoomApp/         # React Native client. Currently empty. Owner: UI developer.
├── brain/                # knowledge layer
├── docs/
│   ├── subject/          # 42 subject PDF (authoritative)
│   └── technical-guide/  # engineering standards library
└── reference/            # READ ONLY — study material, never edited, never built
    └── nArchitecture/    # own .git, net6, stale packages
```

Three rules: **backend writes under `MusicRoom/`, the client writes under `MusicRoomApp/`,
everyone reads `reference/`.** There is no case where code is written into `reference/` or copied
out of it, and no case where one application root writes into the other.

## Not scanned on purpose

`reference/nArchitecture/**` beyond top-level layout, `**/bin`, `**/obj`,
`**/node_modules`, `.git`, `.DS_Store`. Inspect only on a targeted question.
