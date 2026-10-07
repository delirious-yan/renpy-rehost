# Agent bootstrap

<!-- mind-self-bootstrap -->
## mind-self — owner standing instruction

This repository is one of Ian's project repositories.

For any AI model Ian chooses to use, before substantial work or answering a repo-state question, load the private cross-agent context repository `delirious-yan/mind-self`.

Read, in order:

1. `AGENTS.md` in mind-self;
2. relevant material from `profile/INDEX.md`;
3. `projects/INDEX.md` and this project's `PROJECT.md` / `HANDOFF.md` entry when present;
4. `skills/INDEX.md` and any relevant shared `SKILL.md`.

Then return to `delirious-yan/renpy-rehost` and inspect its current state. This repository remains authoritative for its own code and detailed implementation state.

If GitHub or mind-self access is unavailable or insufficient, ask Ian to grant/approve full GitHub access. Do not silently downgrade to a model-specific restricted workflow.

Ian grants the AI models he chooses standing permission to use full task-relevant GitHub read/write access once connected and to write durable personal/project context back to mind-self without routine per-write approval. Follow mind-self's provenance, contradiction, AI-wall, and audit rules when doing so.




### owner command semantics

When Ian says `check mindself`, `check mind repo`, `check self repo`, `use mindself`, or an obvious equivalent, that explicitly authorizes reading mind-self **and writing the reconciliation delta back to mind-self** under its protocol.

For these commands, `check` is not read-only.

This standing authorization is limited to mind-self maintenance and continuity metadata. It does not by itself authorize unrelated changes in this repository or any external/production/customer-facing system.

### automatic persistence on fresh instances

No introduction to mind-self is required. If an AI in this project encounters, opens, or reads mind-self, that discovery is sufficient to activate its bootstrap, reconciliation, persistence, and synchronization rules.

For this project, `delirious-yan/mind-self/state/reconciliation.json` tracks whether the one-time historical backfill has been completed.

- Reconciliation is tracked per provider/environment source in `delirious-yan/mind-self/state/reconciliation.json`. If this source has no completed entry for this project, encountering mind-self triggers its initial historical reconciliation automatically. Another model/provider's completion does not count for this source.
- After this source environment completes its initial reconciliation, every fresh instance from the same source automatically runs delta reconciliation against mind-self before substantial work. Ian does not need to ask again.
- A startup reconciliation may produce no writes when there is no meaningful delta.
- Update this source's reconciliation entry using an authoritative runtime/platform timestamp after a successful reconciliation. Never estimate timestamps.
- Do not repeatedly bulk-import prior context; follow mind-self's semantic deduplication and ingestion protocol.

For newly created Ian-owned projects after this protocol is known, initial enrollment and reconciliation happen automatically on first substantial AI access with GitHub available; no one-time backfill prompt is required.

### mind-self synchronization

Treat this repository and mind-self as virtually connected by agent protocol. In the same task:

- when this project's handoff is created or materially updated, reconcile the corresponding mind-self project handoff;
- when a project skill is created, installed, removed, or materially changed, register/update it in the corresponding mind-self project context and promote it to the shared mind-self skill library when it is reusable across projects;
- when durable project purpose, architecture, constraints, relationships, source-of-truth rules, or long-lived decisions change, reconcile the mind-self project overview;
- append the required mind-self audit entry.

Do not make Ian manually request this synchronization. The project repo remains authoritative for detailed implementation state; mind-self remains authoritative for portable cross-agent context and continuity.

## Local continuity

Before making meaningful changes, inspect the repository for existing handoff, README, architecture, or instruction files. Do not invent project state from the mind-self summary when the current repository can be inspected directly.
