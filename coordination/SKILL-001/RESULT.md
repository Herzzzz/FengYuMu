# SKILL-001 result

- Task: SKILL-001 (one final skill-tooltip composition; visible name, max level,
  complete description, prerequisite, current and next level; list names separate).
- Tool: DeepSeek Harness (DSH) on Windows, PowerShell 7, .NET SDK 6.0.410 Roslyn.
- Branch: `dsh/skill-001`.
- Base commit: `bb4f9c361d5cc0ddef05d8cbdecfe95772eebe05` (EQUIP-001 R3b).
- Implementation commit: `4307b99620053a8f1d627f026603bf6ba50d63f4`.
- Result files commit: reported in the final relay message; this file is committed
  with the result.
- EXE: `D:\GPT\文件\枫语幕\work\dsh-chat-001\枫语幕.exe`; SHA-256
  `7B129A27DD22D30E7B85EBE31920F070B64B0BC3E0B314438C35D12B68D8D16A`.
- Build evidence: `枫语幕.exe.build.json`; SHA-256
  `AD63204550B7EC7A818A7CDD38E92E4EAFCF39B2D9DE7A9B3CC28266CA775823`;
  head `4307b99`, 68 inputs. The tracked worktree contained the R1-R7, R2a and R2b
  source/test changes; only the pre-existing untracked `main_ui_live_*.png` application
  captures were present. Those captures are the app UI, **not** skill-tooltip
  evidence, and were not committed or used as skill-rendering proof.

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

## R2a follow-up

The third review found three remaining visible-semantics failures in the same
overview gate:

- A Recovery sentence with `and fly` appended still borrowed the old Chinese
  because set coverage was above 72%. Coverage is now **ordered and complete**
  for every visible content token in both directions. A whole-token edit-distance
  of one character is allowed for OCR glyph noise; an unknown valid action is not
  covered and preserves the original.
- `unless using a sword` was treated as equivalent to `when using a sword`
  because both contain a condition word. Condition markers are now parsed as a
  signature: relation (`when`/`unless`/`before`/`after`/`if`/`while`/`until`/...)
  plus clause scope, and the target Chinese must carry the same relation and the
  associated weapon/scope evidence. `when` vs `unless`, `before` vs `after` and
  `sword` vs `axe` scope mismatches are rejected.
- `Recover HP -10 for 30 sec.` could become positive `10` because
  `Normalize()` discarded `-`. `RawNumberFacts` now runs **before any Normalize**
  and compares raw sign, canonical value, unit and associated HP/MP/damage
  attribute for every dictionary candidate and the vocabulary fallback.
  Negative target Chinese may use `减少/降低`, but the signed value and unit must
  survive.

The regressions include appended-action variants, `when`/`unless`,
`before`/`after`, scope swaps, positive/negative sign variants and a controlled
single-glyph OCR typo. Each negative plan also proves that its safe current and
next level labels remain painted. The final primary coverage is 264/264 observed,
202 planned, 190 actually painted, 74 preserved with a reason, 0 absent, and
164/164 multi-level panels painted together.

## R2b follow-up

The fourth review found that the shared `ContentTokens` stop list deleted
meaningful logic, direction and object-scope words:

- `and`/`or`, `up`/`down` and `all`/`some` now remain content tokens. They are
  compared in order in both directions.
- These tokens are also in a protected semantic set, so the one-character OCR
  tolerance cannot replace or skip them.
- Condition clause scopes use the same tokens, so `when using a sword or a
  shield` cannot match a candidate using `and`.
- The regressions include both directions of all three pairs and a positive
  control for each. A production plan/composition case uses a temporary
  dictionary candidate and proves the wrong Chinese is not drawn while safe
  current/next levels still paint.

The coverage statement remains deliberately not "all translated": 264/264
primary items were checked, 202 planned, 190 actually painted, 74 preserved with
a reason, 0 absent, and 164/164 multi-level panels painted together.

## Cloud review R1-R7 response

The second review returned `NEEDS_CHANGES`; only R1-R7 were handled. Existing
source changes and ITEM/CAPTURE work were left alone.

- **R1 identity:** exact name occurrences are stored with start/length. Only a
  shorter occurrence contained inside a longer occurrence is discarded;
  independently positioned names remain in the ambiguity set. Therefore
  `Magic Guard Recovery` and `Critical Recovery Recovery` return no id, while
  `Critical Recovery` remains `4100002`.
- **R2 overview safety:** every candidate and the full-text fallback passes
  `SkillOverviewSafety` for visible-source coverage, negation, conditions,
  restriction classes, attribute set and numeric association. A known id can no
  longer replace `Never recover HP constantly for 30 sec.` or
  `An unrelated story lasts for 30 sec.`; unsafe prose keeps its original pixels.
- **R3 header/effect:** a failed effect uses only a measured header span. The
  effect region is untouched and recorded as `preserved`; the header is recorded
  separately. A successful header cannot mean a successful section.
- **R4 measured confidence:** `SkillTextRow` carries `Measured`/`Synthetic`, the
  splitter rebuilds synthetic rows from word-level slices, and `AddSpanRows`
  ANDs the confidence. A proportional split inside one word remains estimated;
  final labels refuse estimated sections. Wrapping cannot upgrade it.
- **R5 list locality:** title, candidate row and neighbouring names must share a
  local region. A header at `(40,30)` plus a standalone `Heal` at `(900,590)`,
  a distant local list, or a player name cannot authorize replacement.
- **R6 prerequisite relation:** the parser reads the whole relation
  (`level N`, `at least`, `at most`, common OCR spellings). `At most Level 5
  Heal` becomes `至多5级`; unsupported relations preserve the original instead of
  defaulting to `至少`.
- **R7 coverage:** the denominator is one primary visible overview per id,
  selected from the current server description, then the full site description,
  then a prose segment. A `[Master Level]` prefix is not required. Every item is
  classified as expected/observed/planned/painted/preserved/absent; painted
  requires membership in the real `simpleReady` set. The final run has
  `264/264` observed, `202` planned, `190` painted, `74` preserved with concrete
  reasons, `0` absent, and `164/164` multi-level panels with current and next
  painted together. `394` other visible variants are listed separately as
  supplemental.

## What changed

### 1. Unique identity (`src/SkillDictionaryMatching.cs`, `src/MapleOverlay.cs`)

- Exact whole-name spans are now resolved with position-aware maximal matching.
  A name contained inside a longer visible title no longer creates ambiguity:
  `Critical Recovery` -> `4100002`. A second independently positioned name does:
  `Magic Guard Recovery` and `Critical Recovery Recovery` -> no id.
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
  current and next sharing one OCR line. It rebuilds synthetic rows from
  word-level slices; a proportional split inside one OCR word is recorded as
  estimated and never upgraded to measured, applied, planned or painted.
- Description source rows stop before the prerequisite/current/next rows. The
  master and prerequisite are separate labels and are never duplicated inside
  the description.
- A current/next header whose effect cannot be translated replaces only a
  measured header span. The effect pixels stay original and the section records
  header and effect status separately. A successful header is not a successful
  section.
- The prerequisite parser reads plain level, `at least`, `at most` and common
  OCR spellings. Unsupported relations preserve the row rather than defaulting
  to `至少`.
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

- A skill name is translated outside a detail panel only when the title,
  candidate row and neighbouring names are in one local list region with an
  explicit header, or at least two nearby same-column exact skill-name rows.
- A bare `Heal` with no list context, an ordinary sentence, a truncated name, a
  player line, a distant header/list, and a same-column but far row are rejected.
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

`tests/logic-skill-panel-001.ps1` (148 checks) covers:

- Position-aware names: `Critical Recovery` unique; `Magic Guard Recovery` and
  `Critical Recovery Recovery` ambiguous.
- Overview safety: negated and unrelated 30-second sentences are preserved;
  the exact Recovery sentence still translates with `30秒`; appended `and fly`
  and candidate-only extra actions are rejected.
- Conditions: `when`/`unless`, `before`/`after`, target relation and weapon scope
  are distinct; exact relation/scope still translates.
- Raw numbers: positive/negative sign, unit and HP/MP/damage association are
  checked before Normalize; exact negative control passes.
- R2b semantic tokens: `and`/`or`, `up`/`down`, `all`/`some` are bidirectional
  negatives and protected from one-character OCR tolerance; exact controls pass.
- Production plan/composition: appended action, condition and R2b semantic
  mismatches do not draw the wrong body, while safe current/next levels paint.
- Untranslated effect: header-only label, effect pixels untouched, separate
  header/effect status.
- Combined single word: estimated title/master/body not applied; a multi-word
  row splits and composes.
- Prerequisite: `at most` -> `至多`, `at least` -> `至少`, unsupported relation
  preserved.
- List locality: far header, distant local list and standalone player name
  rejected; local list name still translates with its level guard.
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
  body=40, current=26, next=37 across the available rows. Fourteen sections are
  preserved with reasons:
  - cases 4/6/14/16/19/20a/20b/20f/30/31/32 body: no safe unique/numeric body
    for the visible text;
  - cases 20e/22 required row: prerequisite text cannot be parsed safely;
  - case 28 required row: prerequisite name is ambiguous.

  These are visible-but-unsafe sections, not normal positive cases. The
  corpus-wide coverage below covers all 264 primary overviews separately.

### Corpus coverage (recomputed, R7)

```
skill ids                         264
names translated                  264/264
primary overview ids expected     264
primary overview ids observed     264
primary overviews planned         202
primary overviews painted         190
primary overviews preserved        74
primary overviews absent            0
supplemental visible variants     394
level effects                     3804
level effects translated           3804/3804
skills with >=2 distinct effects   164
multi-level current+next painted   164/164
```

The primary denominator is one visible overview per ID, selected from
`Henesys.gg当前技能说明`, then `资料站完整技能说明`, then a prose segment. No
`[Master Level]` prefix is required. Each row in
`evidence/coverage-r1.tsv` has `expectedOverview`, `observedOverview`,
`bodyStatus`, `bodyPlanned`, `bodyPainted`, `bodyPreserved`, `bodyReason`,
current/next status and `multiLevelCombinedPainted`. `bodyPainted` requires the
label to be in the real `simpleReady` set, not merely present in the plan.

The 74 preserved primary overviews are accounted for: 61
`body-translation-unavailable`, 12 `skill-section-does-not-fit` and 1
`section-boundary-estimated`. These are safe preserves, not absent rows or
silent omissions. The 394 non-primary visible variants remain in
`evidence/coverage-r1-supplemental.tsv` for review; they are not added to the
denominator as if each were a separate skill.

`evidence/coverage-r1.tsv` and `evidence/coverage-r1-summary.json` are generated from the
shipped corpus; the old 95-row master-prefix table is retained only as history
and is superseded by this run.

### Test commands / results

| Command | Result |
| --- | --- |
| `pwsh -NoProfile -File tests/logic-skill-panel-001.ps1 -ExePath .\枫语幕.exe` | PASS, 148 checks |
| `pwsh -NoProfile -File tests/logic-skill-coverage-001.ps1 -ExePath .\枫语幕.exe` | PASS, 8 checks; 190/264 painted, 74 preserved, 164/164 multi-level |
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
- `evidence/coverage-r1.tsv` - 264 primary rows with expected/observed/planned/
  painted/preserved/absent, per-section status and reasons.
- `evidence/coverage-r1-supplemental.tsv` - 394 non-primary visible variants.
- `evidence/coverage-r1-summary.json` - aggregate counts and preserved-reason
  breakdown for the primary denominator.
- `evidence/logic-skill-coverage-001.txt` - coverage run output.
- `evidence/logic-skill-panel-001-r1.txt` - 148 checks against the final EXE.
- `evidence/skill-title-matching-r1.txt` - 13/13.
- `evidence/skill-detail-semantics-r1.txt` - existing detail/semantics test.
- `evidence/logic-equipment-*-r1.txt`, `logic-simple-flow-r11-r1.txt`,
  `logic-quest-font-layout-r1.txt`, `logic-journal-and-item-r6-r1.txt` -
  accompanying regression logs.

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

R1-R7, R2a and R2b addressed; resubmitted for review. No push and no release were performed.
ITEM/CAPTURE work was not touched.
