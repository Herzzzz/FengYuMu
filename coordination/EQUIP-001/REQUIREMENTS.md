# EQUIP-001 — approved generic equipment-completeness work brief

Task ID: **EQUIP-001**
Branch: `dsh/equip-001`
Base: `399cc8e` (accepted CHAT-001 + QUEST-001/FONT-001 records)

This file records the work brief as relayed to DSH. It is not a separate cloud
acceptance record.

## Goal

Make generic equipment attributes complete through recognition, parsing,
protected-region layout and painting. Add **Evasion** as a generic structured
attribute alias. Diagnose and fix why already-parsed Weapon Def., Magic Def.,
INT, LUK, Remaining Enhancements and attack/critical/speed/attack-speed fields
could still be omitted.

Every original numeric value, sign, percentage, parenthesized correction and
colour must be preserved, for example `+36 (40 -4)`.

## Required behaviour

- Generic aliases only; no equipment-name, item-id, class, player or
  server-specific runtime branches.
- Parse and paint requirements, defences, attacks, critical, evasion, speed,
  attack speed and upgrade slots.
- Keep combined OCR rows (multiple attributes on one line) complete.
- Protect the measured value pixels from erasure; replace only the translated
  key. If a key/value OCR word cannot be split safely, preserve the original
  instead of recolouring the value.
- Handle partial OCR: missing colons, missing value rectangles, one lost
  anchor row, OCR-confusable digits and multiple equipment type rows.
- Use a shared equipment-panel field/typography plan where relevant, consistent
  with the accepted FONT-001 principles (one family/unit/size per panel; no
  independent shrink to 8px); preserve the original when the complete key cannot
  be shown.
- Distinguish recognised / matched / prepared / painted / preservedReason in
  evidence.
- No extra OCR or AI rounds. Do not change chat classification, quest matching,
  the OCR model/protocol, or AI request logic.

## Required verification

Broad regressions for requirements, defences, attacks, critical, evasion,
speed, attack speed, upgrade slots, positive/negative values, percentages,
coloured modifiers, partial OCR and multiple equipment types.

Build on Windows, run the relevant equipment/font/global safety tests, commit
code and evidence, update `coordination/EQUIP-001/RESULT.md` and `STATUS.json`,
and report the commit SHAs and EXE SHA-256. Real in-game visual checks remain
**UNVERIFIED**.
