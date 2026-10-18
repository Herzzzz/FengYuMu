# SKILL-001 result

- Task: SKILL-001 (one final skill-tooltip composition; visible name, max level,
  complete description, prerequisite, current and next level; list names separate).
- Tool: DeepSeek Harness (DSH) on Windows, PowerShell 7, .NET SDK 6.0.410 Roslyn.
- Branch: `dsh/skill-001`.
- Base commit: `bb4f9c361d5cc0ddef05d8cbdecfe95772eebe05` (EQUIP-001 R3b).
- Implementation commit: `5b6c9b07e2f870f1d626f7060eca76e0436da100`.
- Result files commit: reported in the final relay message; this file is committed
  with the result.
- EXE: `D:\GPT\文件\枫语幕\work\dsh-chat-001\枫语幕.exe`; SHA-256
  `45ED9F51C4274AD80977A99122CDECD58455F27A105C0A600B58F5DCF865DFE1`.
- Build evidence: `枫语幕.exe.build.json`; SHA-256
  `437EECC71DAB158302B31B57BE59A0C50B6B6E7BCF98CBF1F2E0699FBEB7CC5F`;
  head `5b6c9b0`, 67 inputs. The tracked worktree was clean; only the pre-existing
  untracked `main_ui_live_*.png` application captures were present. Those captures
  are the app UI, **not** skill-tooltip evidence, and were not committed or used as
  skill-rendering proof.

## Diagnosis

The failure was upstream of painting. The baseline EXE (`32AB92CF...`) produced
these labels for real controlled panel geometry:

```
case 14 Rush:          body = "提高剑类武器熟练度和命中率..."      <= Sword Mastery body
case 15 Critical Recovery: body = "提高剑类武器熟练度..."          <= Sword Mastery body
case 18 Sword Booster: body = "提高剑类武器熟练度..."              <= Sword Mastery body
case 26 Slow:          body = "提高剑类武器熟练度..."              <= Sword Mastery body
case 30 Arrow Bomb:    body = "将对敌人造成的部分伤害吸收为HP..."  <= Drain body
```

The cause was `DetectSkillContentId`: it took the first exact dictionary row.
Every panel whose visible text began with the generic `[Master Level: 20]`
segment matched that segment's owner (1200000, Sword Mastery) before the real
description was considered. A second identity bug made `Critical Recovery`
ambiguous because `DetectUniqueSkillNameId` counted the contained short name
`Recovery`; the old exact-name path therefore could not return 4100002.

The section layer had a separate problem. `AddSkillPanelLabels` ranked at most
three long-text candidates and let the description source union include the
master/prerequisite rows. Independent overview/effect passes could then add a
second owner for the same panel. One successful section could suppress or
overlap another, and a failed description could hide otherwise valid current and
next effects.

List names had a third failure mode: the main dictionary pass called
`FindSkillNameMatches` on every ordinary line. A bare `Heal` in a sentence, a
player name, or a list row could be replaced outside an explicit skill-list
context, and a trailing level digit had no protected span.

Baseline evidence: `evidence/baseline-skill-panel-probe.txt`.

## What changed

### 1. Unique identity (`src/SkillDictionaryMatching.cs`, `src/MapleOverlay.cs`)

- Exact whole-name spans are now resolved with maximal-length matching. A name
  contained inside a longer visible title no longer creates ambiguity:
  `Critical Recovery` -> `4100002`; `Recovery` inside it is ignored.
- `DetectSkillContentId` strips `[Master Level: N]`, prerequisite and
  current/next markers and resolves only from a meaningful visible body. The
  generic master row can never select a skill.
- Exact content candidates are enumerated across every skill detail row, not
  only the first bucket hit. A shared body is used only when every candidate
  supplies the identical Chinese; otherwise the body is preserved.
- OCR spelling repair still works (`Maglc Guard` -> 2001000) without allowing a
  shared English/Chinese name to choose a job ID. Qualified names such as
  `Arrow Bomb` can use a resolved ID to display the visible prefix `爆炸箭`
  without inventing the invisible `: Bow` qualifier.

### 2. One panel plan (`src/SkillPanelComposition.cs`, new)

- `SkillPanelPlan` owns each visible skill panel. `SkillSectionPlan` owns one
  role: `skill-name`, `skill-master-level`, `skill-body`, `skill-required`,
  `skill-current-body`, `skill-next-body`.
- `SplitSkillSemanticRows` handles a title, master header, body, prerequisite,
  current and next sharing one OCR line. It splits at measured marker boundaries
  and exact whole-name prefixes; proportional splits inside one OCR word are
  recorded as estimated and never upgraded to measured.
- Description source rows stop before the prerequisite/current/next rows. The
  master and prerequisite are separate labels and are never duplicated inside
  the description.
- Only visible sections are created. A next section is not invented at max
  level; a missing title does not suppress the body or level effects; a missing
  description does not suppress the effects.
- A visible-but-unsafe section is recorded as `preserved` with a concrete
  reason. An absent section is `absent`, not a translation failure. Other
  sections continue.
- Ambiguous shared descriptions (for example Rush / Amazon's Judgement) are
  preserved rather than borrowing another variant's wording or numbers.

### 3. Shared typography and composition (`src/SkillPanelComposition.cs`,
`src/SimpleFusionComposition.cs`)

- One panel plan chooses SimSun, `GraphicsUnit.Pixel`, body size 11-16px
  (derived from single-line OCR row heights) and title size body+3, capped at
  24px. There is no per-section 8px shrink loop.
- Section slots expand only into blank space before the next measured section.
  A name on a very narrow combined-row span uses the smallest readable size
  (>=11px) instead of overlapping the master label.
- The compositor uses `LayoutFontSize` / `LayoutFontFamily` for skill sections,
  refuses a section that does not fit at that size, and updates each section to
  `painted` or `preserved` with the reason. It logs `skill-panel` and
  `skill-paint`.
- Generic overview/effect passes receive the set of already handled panels and
  do not create a second owner.

### 4. Explicit list names (`src/SkillNameListPolicy.cs`, new)

- A skill name is translated outside a detail panel only when there is an
  explicit header (`SKILL INVENTORY`, `SKILL BOOK`, `SKILL POINT(S)`, `SKILLS`)
  or at least two same-column exact skill-name rows.
- A bare `Heal` with no list context, an ordinary sentence, a truncated name and
  a player line are rejected.
- A trailing list level (`Magic Guard 3`) is excluded from the replaced name
  span and protected with `KeepClearBounds`. The single-word split is marked
  `SourceBoundaryEstimated`; it is not promoted to measured.
- Shared list translations (for example `Rush` -> `突进`) are allowed without
  choosing a job ID.

### 5. No new OCR/AI rounds

The plan consumes the existing `OcrLine`/`OcrPanelInfo` data and the existing
frame. It adds no OCR call, model, prompt, retry or provider request. Chat
classification, quest matching and equipment grammar were not changed.

## Test matrix and evidence

### Identity / semantics

`tests/logic-skill-panel-001.ps1` (81 checks) covers:

- `Critical Recovery` unique ID despite the contained `Recovery`.
- Shared `Rush` / `Sword Mastery` names do not choose a job ID.
- Generic master header alone does not resolve an ID.
- Claw Booster body resolves its own ID; ambiguous shared bodies do not.
- Qualified name `Arrow Bomb` uses only the visible prefix; a bare `Fire`
  prefix is rejected; a truncated name is rejected.
- Full panel: name, max level, complete description, prerequisite, current and
  next all plan and compose at once.
- Matrix: only next; description+next; description+current+next; prerequisite;
  max level without next; missing title; title/master/body on one OCR row;
  ambiguous shared body; narrow panel.
- New name/new numbers/generic effects (`Cobalt Pulse`, 217/239) outside the
  shipped corpus.
- List: header-only, trailing level, level guard, no-context bare name,
  ordinary sentence, truncated name, shared `Rush`.
- No per-skill branch in the new planner, no OCR/AI call, no global bare-name
  replacement in the main dictionary pass.
- 41 controlled screenshot-geometry panels: every planned section composes;
  body=40, current=26, next=37 across the available rows. Six sections are
  preserved with reasons:
  - case 14/19 Rush body: no unique shared description;
  - case 20b/32 Amazon's Judgement body: shared description variants differ;
  - case 22 required row: prerequisite text cannot be parsed safely;
  - case 28 required row: prerequisite name is ambiguous.

  These are visible-but-unsafe sections, not normal positive cases. The
  corpus-wide coverage below covers all shipped unique records separately.

### Corpus coverage (recomputed)

```
skill ids                         264
names translated                  264/264
overview rows available            95
overview translations               95/95
ids with no overview row           169  (not a translation failure)
level effects                     3804
level effects translated           3804/3804
skills with >=2 distinct effects   164
Warrior / Magician / Bowman        85 / 73 / 53
Thief / Beginner / Pirate          50 / 3 / 0
1st / 2nd / 3rd                    93 / 70 / 98
```

`evidence/skill-coverage.tsv` lists every ID with class/stage band, name status,
overview status and per-ID level-pass counts. `evidence/coverage-summary.json`
contains the aggregate. The class/stage bands are derived from the shipped ID
prefix only; this is not a claim of complete game coverage.

### Test commands / results

| Command | Result |
| --- | --- |
| `pwsh -NoProfile -File tests/logic-skill-panel-001.ps1 -ExePath .\枫语幕.exe` | PASS, 81 checks |
| `pwsh -NoProfile -File tests/logic-skill-title-matching.ps1 -ExePath .\枫语幕.exe` | PASS, 13/13 |
| `pwsh -NoProfile -File tests/logic-skill-detail-semantics.ps1 -ExePath .\枫语幕.exe` | PASS |
| `pwsh -NoProfile -File tests/logic-equipment-fields.ps1` | PASS, 91 checks |
| `pwsh -NoProfile -File tests/logic-equipment-paint.ps1` | PASS, 42 checks |
| `pwsh -NoProfile -File tests/logic-equipment-panel-r6.ps1` | PASS, 56 checks |
| `pwsh -NoProfile -File tests/logic-equipment-body-r6.ps1` | PASS, 7 checks |
| `pwsh -NoProfile -File tests/logic-equipment-scope.ps1` | PASS, 27 checks |
| `pwsh -NoProfile -File tests/logic-simple-flow-r11.ps1` | PASS, 33 checks |
| `pwsh -NoProfile -File tests/logic-quest-font-layout.ps1` | PASS, 51 checks |
| `pwsh -NoProfile -File tests/logic-quest-panel-layout.ps1` | PASS, 164 checks |
| `pwsh -NoProfile -File tests/logic-journal-and-item-r6.ps1 -ExePath .\枫语幕.exe` | PASS, 23/23 |
| `pwsh -NoProfile -File tests/logic-chat-category-filtering.ps1` | PASS |
| `pwsh -NoProfile -File tests/logic-chat-continuity.ps1` | PASS, 9 checks |
| `pwsh -NoProfile -File tests/logic-chat-megaphone-pixels.ps1` | PASS, 164 checks |
| `pwsh -NoProfile -File tests/logic-ui-dpi-scaling.ps1` | PASS 100/125/150/175/200% and mixed DPI |
| `pwsh -NoProfile -File tests/logic-release-safety.ps1` | PASS, 22 checks |
| `pwsh -NoProfile -File tests/logic-followup-safety.ps1` | PASS, 11 checks |
| `pwsh -NoProfile -File tests/logic-scene-feedback-safety.ps1` | PASS, 32 checks |
| `pwsh -NoProfile -File tests/logic-capture-frame-policy.ps1` | PASS, 13 checks |
| `pwsh -NoProfile -File tests/logic-user-feedback-safety.ps1` | PASS, 20 checks |
| `pwsh -NoProfile -File src/build.ps1` | PASS, exit 0; EXE hash above |

The two generated-source harnesses (`logic-equipment-paint.ps1`,
`logic-simple-flow-r11.ps1`) were updated with the new `OverlayLabel` /
`SkillPanelPlan` stub fields only; their production assertions are unchanged.
`logic-quest-font-layout.ps1` uses the same generator and passes.

### Pre-existing failures (confirmed, not introduced by SKILL-001)

- `tests/logic-overlay-label-layout.ps1` (cross-screen geometry assertion).
- `tests/logic-overlay-paint-safety.ps1` (stale source guard).
- `tests/logic-overlay-dual-rendering.ps1` (stale source guard).
- `tests/logic-panel-context-isolation.ps1` (stale source guard).
- `tests/logic-quest-body-and-summary.ps1` (stale source guard).
- `tests/logic-quest-title-groups.ps1` (dictionary/title index assertion).
- `tests/logic-quest-shell-resilience.ps1` (clean quest scene not recognized).
- `tests/logic-characters-r15.ps1` (stale `/workspace/work/release-v4.0` path).
- `tests/logic-hotkey-toggle.ps1` (compiled self-check exits 3).

These match the pre-existing failure list recorded by EQUIP-001.

### Not run (stale private-workspace paths / private pixels)

- `tests/logic-skill-corpus-r15.ps1`
- `tests/logic-sections-r15.ps1`
- `tests/logic-skill-screenshots-r15.ps1`

They reference `/workspace/work/release-v4.0` or private original36 pixel
fixtures. The new `logic-skill-panel-001.ps1` replays all 41 controlled
screenshot-geometry panels from the current repository instead.

## Evidence index

- `evidence/baseline-skill-panel-probe.txt` - old EXE output showing wrong
  bodies and generic-master identity.
- `evidence/skill-panel-cases.json` - 32 per-section records for 9 controlled
  cases with caseId/inputHash/evidenceType/captureGeneration/panelIdentity/
  sectionRole/recognized/matched/planned/prepared/painted/preservedReason/
  source/content/protected/fontFamily/fontSize/fontUnit/scale/observed/expected/
  numericAssociations.
- `evidence/skill-coverage.tsv` - 264 IDs, per-ID name/overview/effect status.
- `evidence/coverage-summary.json` - aggregate corpus counts and UNVERIFIED
  markers.
- `evidence/logic-skill-panel-001-root.txt` - 81 checks against the final EXE.
- `evidence/skill-title-matching-root.txt` - 13/13.
- `evidence/skill-detail-semantics-root.txt` - existing detail/semantics test.
- `evidence/logic-equipment-paint.txt`, `logic-simple-flow-r11.txt`,
  `logic-quest-font-layout.txt`, `logic-journal-and-item-r6.txt`,
  `logic-equipment-fields.txt` - accompanying regression logs.

## Evidence boundary / UNVERIFIED

The following remain **UNVERIFIED** and must not be described as real-game
verification:

- Original 36 user screenshots / 41 panels were not present in this checkout.
  The controlled OCR rows and geometry in
  `tests/fixtures/skill-screenshot-geometry-r15.json` were replayed; original
  pixels were not.
- Windows native OCR on the actual game client.
- Real in-game skill-tooltip rendering, icon/border preservation and readability
  at 100/125/150/175/200% and mixed DPI.
- Actual native GDI+ output over real game pixels. The tests use native GDI+
  font measurement and controlled stand-in pixels.
- Private user screenshots, videos and chat evidence were not committed.
- Some generic fallback wording is deliberately literal/imperfect. Ambiguous or
  unsafe descriptions were preserved rather than guessed; a cloud review may
  prefer a different safe fallback.

`main_ui_live_*.png` are untracked app-UI captures and are not skill evidence.

## Cloud review status

PENDING. No push and no release were performed.
