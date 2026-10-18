# SKILL-001 evidence index and boundary

Candidate: `枫语幕.exe` SHA-256
`5A8FE3A85EFCD2837BB45999FB00919B720F0897D04A0DD6F96022DF9B32EBF4`.
Build record: `枫语幕.exe.build.json` SHA-256
`2009867E0FCE77726AC458716D54BFC4565FA5CE466CD7A24685428D1B5B796B`.
Implementation commit: `7dff2c634dc938b6d4c5a05af632ea0517363eef`; the evidence belongs
to the SKILL-R2a follow-up review.

## Evidence classes

| Evidence | What it proves | What it does not prove |
| --- | --- | --- |
| Actual built EXE reflection (`logic-skill-panel-001`, `logic-skill-title-matching`, `logic-skill-detail-semantics`) | The compiled production APIs resolve identity, split sections, preserve numeric associations and compose with the candidate binary. | Windows OCR on the real client or real game pixels. |
| Controlled OCR geometry + stand-in frame + native GDI+ font measurement | The exact production planner/compositor accepts the measured rows, uses one pixel typography, and paints every planned section. | Original screenshot pixels, icon/border collision in the real UI, or real DPI scaling. |
| Generated-source substitute `Graphics`/`Font` harness (`logic-equipment-paint`, `logic-simple-flow`, `logic-quest-font-layout`) | Composition, font selection and safety behaviour under controlled GDI+ stand-ins. | Native GDI+ over the game or Windows Graphics Capture. |
| Primary overview coverage (`coverage-r1.tsv`, `coverage-r1-summary.json`) | 264/264 primary visible overviews are attempted; 202 are planned, 190 are actually in the ready/painted set, 74 are preserved with a reason, 0 are absent, and 164/164 multi-level panels paint current+next together. | Rendering or OCR of the original screenshots. |
| Historical master-prefix probe (`skill-coverage.tsv`, `coverage-summary.json`) | Only the old 95/95 + 169-absent measurement. | **This denominator was wrong.** It required a `[Master Level]` prefix and is retained only as history; it is superseded by `coverage-r1.tsv`. |
| Original 36 screenshots / 41 panels | Not available in this checkout. | Original pixel replay is **UNVERIFIED**. |
| Windows native OCR / actual game | Not run. | **UNVERIFIED**. |

## Historical correction

The earlier statement that 95 IDs had an overview and 169 had none was a
measurement error: it selected only rows carrying a `[Master Level]` prefix. A
visible prose row does not require that prefix. The corrected primary denominator
is **264/264**, with every item classified as expected/observed/planned/
painted/preserved/absent in `coverage-r1.tsv`. The old files remain for audit
history only and must not be cited as current coverage.

## Files

- `coverage-r1.tsv` - 264 primary overview rows with expected/observed/planned/
  painted/preserved/absent, per-section status and reason.
- `coverage-r1-supplemental.tsv` - 394 non-primary visible variants; listed for
  audit, not added to the 264-ID denominator.
- `coverage-r1-summary.json` - aggregate counts and preserved-reason breakdown.
- `logic-skill-coverage-001.txt` - final coverage run output.
- `logic-skill-panel-001-r1.txt` - 130 checks against the final EXE, including
  the R2a negative cases and the safe current/next proof.
- `skill-title-matching-r1.txt` - 13/13 existing title/identity checks.
- `skill-detail-semantics-r1.txt` - existing Recovery detail semantics.
- `logic-equipment-paint-r1.txt`, `logic-simple-flow-r11-r1.txt`,
  `logic-quest-font-layout-r1.txt`, `logic-journal-and-item-r6-r1.txt`,
  `logic-equipment-fields-r1.txt` - accompanying regressions.
- `baseline-skill-panel-probe.txt` - baseline EXE (`32AB92CF...`) output showing
  the wrong 1200000/Sword Mastery/Drain bodies for Rush, Critical Recovery,
  Sword Booster, Slow and Arrow Bomb.
- `skill-panel-cases.json` - 32 section records across 9 controlled cases. Each
  record has caseId, inputHash, evidenceType, captureGeneration, panelIdentity,
  sectionRole, recognized/matched/planned/prepared/painted/preservedReason,
  source/content/protected bounds, font family/size/unit/scale, observed,
  expected and numericAssociations.
- `skill-coverage.tsv` / `coverage-summary.json` - **historical wrong 95/169
  denominator**, retained only for audit.

## Commands (final EXE)

```
pwsh -NoProfile -File src/build.ps1
pwsh -NoProfile -File tests/logic-skill-panel-001.ps1 -ExePath .\枫语幕.exe
pwsh -NoProfile -File tests/logic-skill-coverage-001.ps1 -ExePath .\枫语幕.exe
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

## Deliberate preserves

`coverage-r1.tsv` records every preserve with a reason. The 41-panel replay
currently preserves 15 sections (body text that cannot be completely covered or
fit, plus two prerequisite rows); the safe name/master/current/next sections are
still painted. No normal positive case is counted as a success merely by
preserving English.
