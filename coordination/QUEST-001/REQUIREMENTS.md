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

## Cloud review round (NEEDS_CHANGES)

- **QUEST-R1:** prose such as `Please take care of 50` and `Please collect a total of 20` was falsely treated as an objective counter, making a valid replacement region empty. Counters must be identified by target-column location, true counter structure and word boundaries; do not blacklist example phrases. Preserve real objective counts/icons/summary.
- **QUEST-R2:** wrapping `A😀` could hang. Surrogate boundaries must always advance; add timeout-safe CJK/emoji/supplementary title/body/full-pagination tests with no character loss.
- **QUEST-R3:** the R1 positive integration appended visible prose `Please take care of 50` after a record requiring 20 each; the selected Chinese record had no 50, but the fallback still accepted by similarity and erased the whole region. Add a generic completeness safeguard to every candidate/fallback path: visible quantities and prose must be covered by the selected English and Chinese, or the whole replacement must be rejected and original pixels/text preserved. Correct the R1 positive test to use fully corresponding English/Chinese content, and add negative regressions for conflicting quantities and appended unknown visible prose.
- **QUEST-R3a:** the completeness guard unconditionally skipped `line == heading`; in titleless/scroll paths that heading is a body locator. Skip only a structurally confirmed title. Test titleless/scrolled first lines with conflict/unknown and matching positives.
- **QUEST-R3b:** a bag-of-words ratio accepted appended/reordered prose such as `She wanted Green Mushroom Cap but not Orange Mushroom Cap` and lost the negation. Replace it with ordered source-text alignment over candidate English with bounded OCR-tolerant token matching. Unexplained/reordered/semantic-changing prose must preserve the original; normal OCR deviations must still pass. Add generic negatives and OCR-noise positives.
- **QUEST-R3c:** numeric tokens were skipped and quantities checked only as a set, so dictionary `1 Green Mushroom Cap and 10 Blue Snail Shells` accepted observed `10 Green Mushroom Cap and 1 Blue Snail Shell`. Numbers must participate in the ordered alignment and remain associated with surrounding item/prose tokens; do not skip single-number or short-number rows. Add correct-association, swapped-quantity and changed/duplicate-multiplicity regressions. No quest-specific exceptions.
- Evidence wording must distinguish the actual-GDI+ panel checks from the substitute Font/Graphics harness checks, and list actual font measurements separately.
- Preserve the one-region replacement and first-line/right-edge/summary residue checks. No chat/OCR/AI changes. Clean build, run relevant + safety regressions, update result/status, commit code/evidence, report SHAs and EXE hash.
