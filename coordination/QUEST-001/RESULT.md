# QUEST-001 / FONT-001 result

- Task: QUEST-001 with the shared FONT-001 layout requirements.
- Tool: DeepSeek Harness (DSH) on Windows, PowerShell 7.6.5, .NET SDK 6.0.410 Roslyn.
- Branch: `dsh/quest-font-001`.
- Base commit: `dff639b828542d0bcad94fc9eb50a816f3f9bfd3`.
- Previous implementation commit: `eba0d4d131f56fdeac88c7464b17f07a88162ed7`.
- Review-fix implementation commit: **PENDING**.
- Result files commit: reported in the final relay message.
- EXE: `D:\GPT\文件\枫语幕\work\dsh-chat-001\枫语幕.exe`; SHA-256 **PENDING**.
- Build evidence: `枫语幕.exe.build.json`; SHA-256 **PENDING**; head **PENDING**, 64 inputs, clean worktree required.

## What changed

### 1. `src/QuestJournalOverlay.cs`

- The confirmed body is now a **single replacement label**. Its measured region is grown to own every OCR row whose words appear in the selected dictionary body, not merely the paper rectangle or the first/last OCR fragment.
- Region construction now:
  - includes the complete first and last body row plus a one-source-pixel seam;
  - clips to the measured detail pane and the quest owner, protecting adjacent panels/scrollbars;
  - removes partial overlaps with `Quest Summary`, level gates, counters, footer buttons and objective cards instead of leaving half-erased English;
  - detects OCR-confused objective counters such as `stump 30 f 30` locally, so those rows stay outside the body region;
  - rejects the panel when an objective icon/card still lies inside the region.
- Selection evidence:
  - the existing title+prose anchors still select first;
  - when those anchors are over-strict but the visible title is unique and not clipped, the dictionary's observed-body matcher supplies candidates;
  - candidates are ranked against the **whole measured panel**, with a clear-winner margin; id-less shared rows are accepted only for the resolved unique task context. Ambiguous results stay unselected and the original is preserved.
- The title/body/page plan is built once with a real `Graphics` and stored on the document, then reused by composition. A plan that cannot fit the complete body makes the journal path decline, so the existing whole-body fallback is not suppressed.
- Only labels that the replacement region **fully owns** are removed. An adjacent label that merely touches the boundary stays alive.

### 2. `src/QuestJournalGeometry.cs`

- Added the reusable `PanelTypography` and `QuestJournalLayout` plan:
  - one family (`Microsoft YaHei`) and one pixel unit per panel;
  - body base size derived once from the measured source height, floor 15px, cap 28px; title = body + 4px bold;
  - controlled insets, right gutter for glyph overhang/scrollbar, and a title gap;
  - title wrapping/reduction and body wrapping by **measured content width**;
  - body pagination (up to 128 pages) with page text stored as ordered substrings, so no character is silently dropped;
  - footer reservation when multiple pages exist;
  - recorded `Font.Size`, `Font.Unit`, `Content`, `WrapWidth`, `PageCount`, `MaxLineWidth` and `Overflow` decisions.
- Added replacement-region expansion/snapping (`ExpandReplacementRegion`) and local counter-shape protection.
- Existing paper-surface measurement (`Measure`) and its conservative rejection of unknown backgrounds are preserved.

### 3. `src/QuestJournalComposition.cs`

- `TryPrepareJournalDraw` uses the stored plan when its region matches; otherwise it builds one with the real composition graphics.
- Every wrapped line is drawn with the point overload of `Graphics.DrawString`, so GDI+ cannot re-wrap or clip the final glyph at the layout rectangle edge. A right gutter and measured line widths keep the text inside the replacement region.
- The title uses its own larger bold font; the body uses the shared base font. Paging redraws title, page body lines and footer from the cached plan without OCR.
- The page view records the plan, title/body sizes, unit, content bounds, page lines and overflow decision.

### 4. `src/SimpleFusionComposition.cs`

- The journal branch uses the plan/title font and caches the plan for page changes.
- The generic single-label path keeps its behavior, but `task-body`/`task-journal` labels now have a 14px floor instead of being squeezed to 8px. If the complete text cannot fit at the floor, the original is preserved rather than truncated.

## Cloud review fix round

### QUEST-R1 — counter detection false positives

The previous local counter regex could match the suffix `of 50` / `of 20` in ordinary prose because `o` was accepted as a zero substitute and `f` as a slash substitute. Counter detection now uses three signals:

- **target-column location**: objective card/list rectangles measured by `QuestObjectiveSurface.Find` (with a scale-tolerant edge) are passed into the region planner; a line inside that column is treated as objective chrome;
- **true counter structure**: a numeric suffix pair at the end of the line, with a strong `/`/`\` separator or an OCR-confused `f`/`t` separator;
- **word boundaries and real digits**: the pair must start at a word boundary. The weak `f`/`t` form requires a real ASCII digit on both sides, so `Please take care of 50` and `Please collect a total of 20` are never counters. A strong slash pair may be accepted in the target column even when OCR rendered both digits as confusable letters (`O / O`), but never outside it without a real digit.

Real objective counters/icons/summary remain protected, including `stump 30 f 30`. The R1 positive integration now uses the selected record's own English/Chinese rows; the prose examples are tested as counter strings, not appended to a record that does not contain them. Negative integrations append `Please take care of 50` (quantity conflict) and an unknown sentence, and verify the whole journal replacement is rejected with original pixels preserved.

### QUEST-R3 — visible-prose completeness safeguard

Every journal selection path (anchored `SelectJournal`, observed-body fallback, and remembered-scroll context) now goes through one generic `JournalReplacementComplete` guard before a replacement label is emitted:

- every visible number inside the final region must exist in the candidate English and in the candidate Chinese; otherwise the quantity/version is inconsistent and the whole replacement is rejected;
- the complete visible non-chrome prose sequence must align **in order** to the candidate English. Matching is OCR-tolerant per token (case/punctuation normalization and bounded edit distance), but every visible token must find a later candidate token. Inserted negation, reordered words across lines, or unknown prose rejects the whole replacement;
- only a structurally confirmed stage title is skipped. In a titleless/scrolled path the heading is a body locator and is checked like any other prose row;
- summary, level gate, counters and objective-card rows are excluded from the prose check because they are translated/handled separately.

The guard is not phrase-specific. A visible appended line with a missing quantity (`50` vs a record that only has `20`) or unknown/reordered/negated prose causes `TryAddQuestJournal` to return false and records `quest-journal-incomplete:<reason>` in `lastQuestJournalRejection`. No journal label is created, so the original pixels/text stay in place.

- **R3a:** titleless and scrolled first-row cases cover both conflict/unknown and matching positives, with and without a caption used only to keep selection reachable. The first body row is never skipped.
- **R3b:** the coverage check now uses ordered source-text alignment over the whole visible prose, not a bag-of-words ratio. Tests cover `She wanted Green Mushroom Cap but not Orange Mushroom Cap`, reordered variants, cross-line reversal, an extra unknown token, and OCR noise (case/punctuation/one-letter typo) that must still pass.

### QUEST-R2 — Unicode wrapping progress

`WrapText` no longer binary-searches arbitrary UTF-16 code units. It builds a list of complete code-point boundaries (surrogate pairs advance together), searches only those boundaries, and has defensive advance checks at both the line end and the position update. `A😀` now returns immediately; CJK, emoji and supplementary-plane characters preserve every character across titles, bodies and full pagination. The substitute Font/Graphics regression wraps every R2 case in an 8-second watchdog, and the actual-GDI+ panel regression also builds `A😀` and a long supplementary title/body.

## Typography / overflow evidence

Actual GDI+ metrics are recorded in `evidence/layout-metrics.json` (142 panel checks, measured with the built EXE's real `Graphics` and `Font`). Representative values:

| Case | Body | Title | Unit | Pages | Overflow |
|---|---:|---:|---|---:|---|
| short body + summary | 18 | 22 | Pixel | 1 | title-fit; body=single-page |
| short body, no summary | 18 | 22 | Pixel | 1 | title-fit; body=single-page |
| three objective counters | 18 | 22 | Pixel | 1 | title-fit; body=single-page |
| long manual body | 18 | 22 | Pixel | 8 | title-fit; body=pages:8 |
| 100% | 18 | 22 | Pixel | 1 | title-fit; body=single-page |
| 125% | 22 | 26 | Pixel | 1 | title-fit; body=single-page |
| 150% | 27 | 31 | Pixel | 1 | title-fit; body=single-page |
| 200% | 28 | 32 | Pixel | 1 | title-fit; body=single-page |

Across all recorded cases the measured maximum line width is <= the measured content width, the replacement region covers the first body row, the `Quest Summary` row is outside it, and objective counters are outside it.

## Tests

See `evidence/test-results.json` and the individual logs in `evidence/`. The logs below are pre-clean R3a/R3b PASS runs; the final clean-build rerun is recorded after the clean build.

Passing on this host/build:

- new `tests/logic-quest-panel-layout.ps1` — **142 checks** (the original 64 actual-GDI+ panel checks plus all R1/R2/R3 additions) using real GDI+ measurement on the built EXE (selection/region/typography/pagination/repeat F8/scale, R1 counter policy and integration, R2 actual-GDI+ emoji/supplementary plans, R3 completeness positive/negative cases, R3a titleless/scrolled anchors, R3b ordered alignment/OCR noise);
- new `tests/logic-quest-font-layout.ps1` — **51 checks** (the original 33 plus 18 R2 checks) using the generated-source **substitute Font/Graphics harness**, not native GDI acceptance (full composition, summary coexistence, first-line/right-edge markers, long/short body, scroll, page turn, repeated composition, scales, and 8-second-watchdog CJK/emoji/supplementary wrapping with character preservation);
- `tests/logic-simple-flow-r11.ps1` — 33 checks;
- `tests/logic-journal-and-item-r6.ps1 -ExePath .\枫语幕.exe` — 23/23;
- `tests/logic-quest-text-scope-r12.ps1` — 17 checks;
- `tests/logic-quest-matching-r12.ps1 -ExePath .\枫语幕.exe` — 28 checks;
- `tests/logic-observed-quest-wording.ps1 -ExePath .\枫语幕.exe` — 12/12;
- `tests/logic-ui-dpi-scaling.ps1` — 100/125/150/175/200%, mixed DPI;
- `tests/logic-chat-category-filtering.ps1` — pass (chat untouched);
- `tests/logic-chat-megaphone-pixels.ps1` — 164 checks (chat untouched);
- `tests/logic-chat-continuity.ps1` — 9/9;
- `tests/logic-independent-window.ps1` — pass.

## Pre-existing failures (confirmed on baseline `dff639b` EXE `6E618DF2...`)

These fail identically on the untouched baseline worktree and were re-confirmed on the final R1/R2 EXE (`evidence/safety-regressions-pre-existing.txt`); they are not caused by QUEST-001:

- `tests/logic-quest-body-and-summary.ps1` — source guard still expects the old `results.RemoveAll(...TaskId...)` string; current source has the `||` continuation.
- `tests/logic-panel-context-isolation.ps1` — source guard still looks for literal `normalized.Contains("quest summary")`; current code uses `QuestDetailGeometry.IsSummaryKey`.
- `tests/logic-overlay-label-layout.ps1` — existing geometry assertion for a 1800x60 cross-screen block.
- `tests/logic-overlay-paint-safety.ps1` — source guard still looks for `OverlayLabel[] snapshot = labels.ToArray()`.
- `tests/logic-overlay-dual-rendering.ps1` — source guard still looks for `label.RenderMode == LabelRenderMode.Seamless`.
- `tests/logic-quest-title-groups.ps1` — pre-existing data-index assertion (`Father's Mushroom Stew` in the concrete title index).
- `tests/logic-quest-shell-resilience.ps1` — clean synthetic scene still reports `识别场景=` empty on this host's Windows OCR; the journal itself selects and composes successfully in the same run (`quest-journal selected=true`, 12 labels, journal plan reused). Baseline fails at the same assertion.

Tests with hard-coded `/workspace` or `bin/windows-v4.0` paths (`logic-quest-paragraph-pipeline-r12`, `logic-quest-journal-r13`, `logic-universal-panels-r14`, etc.) were not run; they are stale-path tests, not source regressions.

## Runtime / scope

- No chat classification, OCR model/protocol or AI request logic changes.
- No additional OCR or AI rounds. The journal plan is built from the already-selected OCR text and cached; page turns use the cached layout and pixels.
- The existing conservative paper measurement and fallback preservation remain; if the plan/region cannot safely fit, English is preserved rather than partially erased.
- Synthetic quest shell run on the final EXE: `quest-journal selected=true reason=stage-and-state-anchor`, region `818,451,796x700`, layout `body=15px`, `title=19px`, `content=371,205,342,307`, `pages=1`, composition `labels=12 prepared=12` (the test's separate scene-classification assertion still reports an empty scene on this host; that failure reproduces on the untouched baseline).
- Real in-game visual acceptance (physical F8, actual quest UI at 100/125/150/200%, real Game window) is **UNVERIFIED**. The 90 panel checks use synthetic pixels with actual GDI+; the 51 font checks use a substitute Font/Graphics harness. Neither is a real-game visual acceptance.
- No raw user screenshots, chat text, keys or private logs were committed. Evidence contains only synthetic panel geometry and public dictionary text.
