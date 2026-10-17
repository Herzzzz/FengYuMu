# QUEST-001 + shared FONT-001 — approved work brief

Task ID: **QUEST-001** (shared layout requirements: **FONT-001**)
Branch: `dsh/quest-font-001`
Base: `dff639b828542d0bcad94fc9eb50a816f3f9bfd3` (the branch supplied for this work; v4.0 source plus completed CHAT-001 records)

This file records the work brief as relayed to DSH. It is not a separate cloud acceptance record.

## User-visible defects to fix

1. The first line of a confirmed quest body can retain English pixels (or only a partial row is replaced).
2. The right edge of the Chinese body can be clipped.
3. The `Quest Summary` title/section can retain English pixels or be partially removed while its label is lost.

## Required behaviour

- Treat the confirmed quest body as **one complete replacement region**.
- Use shared panel typography: a stable title/body hierarchy and one consistent body base size per panel. Do not shrink each label independently down to 8px.
- Wrap text by the **measured content width**.
- Protect icons, objective quantities, live values and adjacent panels. Do not erase a partially covered adjacent label while leaving its English behind.
- Do not silently truncate text. If the complete body cannot fit, paginate it; if the region is unsafe, preserve the original rather than paint a partial replacement.
- Do not guess full text from an ambiguous clipped title.
- Do not add task-specific runtime branches.
- Do not modify chat classification, the OCR model/protocol, or AI request logic.
- No extra OCR or AI rounds.

## Scope

`src/QuestJournalOverlay.cs`, `src/QuestJournalGeometry.cs`, `src/QuestJournalComposition.cs`, and the final panel-composition/typography hook in `src/SimpleFusionComposition.cs`. Tests and coordination files are in scope; unrelated game panels and chat are not.

## Required verification

Synthetic regressions for:

- long and short bodies;
- with and without a summary pane;
- stage/objective counts;
- scrolled/heading-less body;
- repeated F8 composition;
- 100/125/150/200% scale.

Record actual `Font.Size`, `Font.Unit`, content bounds and overflow/pagination decisions where testable. Build on Windows, run the relevant quest/font/global regressions, commit code and evidence, and report the commit SHA and EXE SHA-256. Mark real in-game visual checks UNVERIFIED.
