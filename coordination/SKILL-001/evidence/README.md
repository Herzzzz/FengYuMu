# SKILL-001 evidence index and boundary

Candidate: `枫语幕.exe` SHA-256
`45ED9F51C4274AD80977A99122CDECD58455F27A105C0A600B58F5DCF865DFE1`.
Build record: `枫语幕.exe.build.json` SHA-256
`437EECC71DAB158302B31B57BE59A0C50B6B6E7BCF98CBF1F2E0699FBEB7CC5F`.
Implementation commit `5b6c9b07e2f870f1d626f7060eca76e0436da100`.

## Evidence classes

| Evidence | What it proves | What it does not prove |
| --- | --- | --- |
| Actual built EXE reflection (`logic-skill-panel-001`, `logic-skill-title-matching`, `logic-skill-detail-semantics`) | The compiled production APIs resolve identity, split sections, preserve numeric associations and compose with the candidate binary. | Windows OCR on the real client or real game pixels. |
| Controlled OCR geometry + stand-in frame + native GDI+ font measurement | The exact production planner/compositor accepts the measured rows, uses one pixel typography, and paints every planned section. | Original screenshot pixels, icon/border collision in the real UI, or real DPI scaling. |
| Generated-source substitute `Graphics`/`Font` harness (`logic-equipment-paint`, `logic-simple-flow`, `logic-quest-font-layout`) | Composition, font selection and safety behaviour under controlled GDI+ stand-ins. | Native GDI+ over the game or Windows Graphics Capture. |
| Corpus probe (`skill-coverage.tsv`, `coverage-summary.json`) | 264 shipped names, 95/95 available overview rows and 3804/3804 level effects translate with ordered numeric preservation. | Rendering or OCR; 169 IDs have no overview row in the shipped corpus. |
| Original 36 screenshots / 41 panels | Not available in this checkout. | Original pixel replay is **UNVERIFIED**. |
| Windows native OCR / actual game | Not run. | **UNVERIFIED**. |

## Files

- `baseline-skill-panel-probe.txt` - baseline EXE (`32AB92CF...`) output showing
  the wrong 1200000/Sword Mastery/Drain bodies for Rush, Critical Recovery,
  Sword Booster, Slow and Arrow Bomb.
- `skill-panel-cases.json` - 32 section records across 9 controlled cases. Each
  record has caseId, inputHash, evidenceType, captureGeneration, panelIdentity,
  sectionRole, recognized/matched/planned/prepared/painted/preservedReason,
  source/content/protected bounds, font family/size/unit/scale, observed,
  expected and numericAssociations.
- `skill-coverage.tsv` - 264 shipped skill IDs with class/stage band, name
  status, overview availability/translation, level-effect count, distinct-effect
  count and level pass/fail counts.
- `coverage-summary.json` - aggregate counts and explicit UNVERIFIED markers.
- `logic-skill-panel-001-root.txt` - 81 checks against the final EXE.
- `skill-title-matching-root.txt` - 13/13 existing title/identity checks.
- `skill-detail-semantics-root.txt` - existing Recovery detail semantics.
- `logic-equipment-paint.txt`, `logic-simple-flow-r11.txt`,
  `logic-quest-font-layout.txt`, `logic-journal-and-item-r6.txt`,
  `logic-equipment-fields.txt` - accompanying regressions.

## Commands (final EXE)

```
pwsh -NoProfile -File src/build.ps1
pwsh -NoProfile -File tests/logic-skill-panel-001.ps1 -ExePath .\枫语幕.exe
pwsh -NoProfile -File tests/logic-skill-title-matching.ps1 -ExePath .\枫语幕.exe
pwsh -NoProfile -File tests/logic-skill-detail-semantics.ps1 -ExePath .\枫语幕.exe
pwsh -NoProfile -File tests/logic-equipment-fields.ps1
pwsh -NoProfile -File tests/logic-equipment-paint.ps1
pwsh -NoProfile -File tests/logic-equipment-panel-r6.ps1
pwsh -NoProfile -File tests/logic-equipment-body-r6.ps1
pwsh -NoProfile -File tests/logic-equipment-scope.ps1
pwsh -NoProfile -File tests/logic-simple-flow-r11.ps1
pwsh -NoProfile -File tests/logic-quest-font-layout.ps1
pwsh -NoProfile -File tests/logic-quest-panel-layout.ps1
pwsh -NoProfile -File tests/logic-journal-and-item-r6.ps1 -ExePath .\枫语幕.exe
pwsh -NoProfile -File tests/logic-chat-category-filtering.ps1
pwsh -NoProfile -File tests/logic-chat-continuity.ps1
pwsh -NoProfile -File tests/logic-chat-megaphone-pixels.ps1
pwsh -NoProfile -File tests/logic-ui-dpi-scaling.ps1
pwsh -NoProfile -File tests/logic-release-safety.ps1
pwsh -NoProfile -File tests/logic-followup-safety.ps1
pwsh -NoProfile -File tests/logic-scene-feedback-safety.ps1
pwsh -NoProfile -File tests/logic-capture-frame-policy.ps1
pwsh -NoProfile -File tests/logic-user-feedback-safety.ps1
```

All returned exit code 0 except the pre-existing failures listed in
`STATUS.json` and `RESULT.md`.

## Deliberate preserves in the 41-panel replay

- case 14 and 19 Rush body: the visible description does not uniquely match one
  of the shared Rush variants.
- case 20b and 32 Amazon's Judgement body: shared description variants differ in
  Chinese.
- case 22 prerequisite: the prerequisite text cannot be parsed safely.
- case 28 prerequisite: the prerequisite skill name is ambiguous.

These are visible-but-unsafe sections. They stay original and do not block the
safe name/master/current/next sections. No normal positive case is counted as a
success merely by preserving English.
