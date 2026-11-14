# brain/ — project memory for agents

Short, curated notes so a new agent (or session) can continue without re-deriving context. Keep it accurate: if you change
something that a note describes, **fix the note in the same change**. Prefer editing over appending, except `log.md`.

| File | What it holds |
|---|---|
| [`overview.md`](overview.md) | What the project is, the migration goal, current state, user preferences |
| [`architecture.md`](architecture.md) | Projects, layers, data flow, threading model, how a search and a download work end to end |
| [`config-compat.md`](config-compat.md) | The shared-file contract with the JavaFX app: config.json, SQLite, cache, paths |
| [`java-to-csharp-map.md`](java-to-csharp-map.md) | Java class → C# type table with port status; the porting checklist |
| [`booru-apis.md`](booru-apis.md) | Per-engine API quirks that the clients encode (paging, auth, ratings, categories) |
| [`gotchas.md`](gotchas.md) | Avalonia 12 / .NET 10 / tooling surprises and how they were solved |
| [`decisions.md`](decisions.md) | Decision log (what, why, alternatives) — add an entry when you choose between real options |
| [`log.md`](log.md) | Chronological session log; append one entry per working session |

Entry points: `AGENTS.md` (rules), `TODO.md` (work queue).
