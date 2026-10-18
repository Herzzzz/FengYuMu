# EQUIP-001 result

- Task: EQUIP-001 (generic equipment completeness; Evasion alias; protected values).
- Tool: DeepSeek Harness (DSH) on Windows, PowerShell 7.6.5, .NET SDK 6.0.410 Roslyn.
- Branch: `dsh/equip-001`.
- Base commit: `399cc8e` (accepted CHAT-001 + QUEST-001/FONT-001 records).
- Implementation commit: `ce945b0947f7b1e81b8ac2e5e2d43c1ef9874669`.
- Result files commit: reported in the final relay message; this file is committed with the result.
- EXE: `D:\GPT\文件\枫语幕\work\dsh-chat-001\枫语幕.exe`; SHA-256 `638A2B3E2F57D43A8899E766B0F5A94901D4110844529D4A86D99861788D2B7D`.
- Build evidence: `枫语幕.exe.build.json`; SHA-256 `50720CBBB639116C724A5C657220481499C7BAB4FECBEFA120C1AB89498DDA61`; head `ce945b0`, 65 inputs, clean worktree at build.

## Diagnosis

The implementation was not failing only in the painter. A combined
requirements/defence row was already discoverable, but the structured builder
passed `equipment=false` whenever `BuildOcrPanels` had not classified the local
panel. In that mode the old parser dropped every field followed by more letters
on the row:

```
equipment=False fields=1 magic-defense=[+18]  <= Weapon Def.: +16  Magic Def.: +18
equipment=False fields=1 upgrades=[7]         <= INT: +1  LUK: +2  Remaining Enhancements: 7
equipment=False fields=1 fame=[0]             <= REQ LEV : 10 ... REQ FAME : 0
```

The same baseline probe also showed the missing generic aliases:

```
equipment=True fields=0  <= Attack Speed: Fast
equipment=True fields=0  <= Evasion: +4
```

See `evidence/baseline-grammar-probe.txt` (old grammar from `399cc8e`, not the
current EXE). The recognition gate was too narrow (req/remaining/two defences),
so partially OCR'd tooltips never reached the equipment path. The old whole-row
translation could also erase a value before the structured field label was
planned, and the old merged key/value branch repainted the value in an averaged
ink colour.

## What changed

### 1. `src/EquipmentFieldGrammar.cs`

- Generic alias table extended with `Evasion`/`Avoidance` -> avoidability,
  `Attack Speed` (numeric or Fast/Very Slow/Normal), `Upgrade Slots` /
  `Remaining Upgrades`, short `ATT`/`ATK` and `W.`/`M.` defence forms, and
  critical/crit rate/damage aliases. No item id, name, class, player or server
  value participates.
- Values are captured as the exact raw OCR substring:
  - signs `+`, `-`, U+2212 and full-width signs;
  - decimals, thousands separators and OCR-confusable digits such as `1O`;
  - trailing percentages and every parenthesized correction (`+36 (40 -4)`,
    `+36% (40% -4%)`).
- Combined rows keep every field. The old "letters after the value" prose guard
  still rejects prose, but a complete known field after separators is accepted.
- Overlapping aliases are resolved by longest field at a position
  (Critical Damage over Damage, Attack Speed over Speed, MaxHP over HP).
- Recognition is structural: requirement/upgrade/type/attack-speed anchors,
  defence/attack pairs, or a text consumed entirely by field spans. Ordinary
  prose such as `Intelligence +5 and Strength +3` is not an equipment panel or
  row.
- Partial key rows (`Weapon Def.`, missing value rectangle, missing colon for a
  signed stat) still produce a key-only plan and protect the rest of the row.

### 2. `src/EquipmentPanelLayout.cs` (new)

- Shared `EquipmentPanelTypography`:
  - one family (`Microsoft YaHei`) and one pixel unit for the panel;
  - base field size derived once from the measured row heights (floor 11px,
    cap 22px);
  - no independent 8px squeeze.
- Shared `EquipmentPanelPlan` and `EquipmentFieldPlan` carry:
  - the original name, exact raw value, translation and match source;
  - the key-only source region and the measured value protection guard;
  - the panel boundary, shared typography and build/paint/preserve status.
- Layout:
  - a separate key label and value guard; the value pixels are never sampled
    into the replacement;
  - a merged key/value OCR word is split before the measured value with a
    two-pixel gap;
  - if no safe split exists, the field is marked
    `preserved / merged-value-region-unsafe` and the original row is kept;
  - a partial value with no rectangle reserves the rest of its row.

### 3. `src/StructuredPanelOverlay.cs`

- Builds one panel plan and one typography decision per equipment panel.
- Parses each visual row with the row-level equipment context, not only the
  panel flag, so a missed panel anchor can no longer drop all but the last field.
- Emits only the translated key label; the raw value stays in place with its
  original sign, percentage, parenthesis correction and colour.
- Removes an earlier whole-row translation even when every field must be
  preserved, so the original value pixels are not overwritten.
- Records:
  - `equipment-panel recognized=... matched=... prepared=... painted=...
    preserved=... keys=... panel=... typography=... reason=...`;
  - `equipment-coverage recognizedPanels=... recognizedRows=... parsed=...
    prepared=... valuePixels=... partialValues=... mergedText=...
    preserved=... preservedReason=... rejected=... keys=...`.

### 4. `src/MapleOverlay.cs`

- `BuildOcrPanels` recognises equipment by grammar structure and excludes
  character-stat/skill shapes that share Attack/Def/Accuracy/Speed words.
- `AddEquipmentPanelLabels` lets the structured plan own every field row and
  translates a mixed-row prefix (`Type: Hat`) as a separate `item-type` label
  with its own boundary, so it cannot grow into the attribute columns.
- `LooksLikeEquipmentFieldText` is the grammar-aware equipment path. The chat
  classification gate and the prose-AI eligibility gate still use the original
  `LooksLikeEquipmentStatText` vocabulary. `tests/logic-equipment-scope.ps1`
  asserts this.

### 5. `src/SceneRecognition.cs`

- Equipment structure evidence delegates to the same generic grammar while
  preserving the old conservative fallbacks.

### 6. `src/SimpleFusionComposition.cs`

- Equipment fields use the panel plan's family/size (bounded to the 11px floor)
  instead of the generic 8px squeeze.
- A key that cannot fit at the floor is preserved with
  `equipment-field-does-not-fit`.
- Records `equipment-paint prepared=... painted=... preserved=...
  preservedReason=...` and updates each field plan's painted/preserved state.

### 7. Tests

- `tests/logic-equipment-fields.ps1` (63 checks): actual built EXE grammar and
  plan API over requirements, defences, attacks, critical, evasion, speed,
  attack speed, slots, positive/negative/Unicode/percent/corrected values,
  partial OCR, merged words, multiple equipment types and shared typography.
- `tests/logic-equipment-paint.ps1` (22 checks): generated-source substitute
  Font/Graphics composition. Paints a synthetic 12-row equipment panel, verifies
  every value marker stays transparent/original, one shared font size, the same
  panel plan, painted counts and preservation reasons.
- `tests/logic-equipment-panel-r6.ps1` (28 checks): exact production
  `AddStructuredFieldLabels` with controlled OCR geometry. Verifies recognised/
  matched/prepared counts, all fields survive on combined rows, one panel plan,
  one typography, key-before-guard geometry, unrecognised-row fallback,
  key-only partial rows and five equipment types.
- `tests/logic-equipment-body-r6.ps1` (7 checks): existing equipment description
  routing regression updated for the new builder boundary.
- `tests/logic-equipment-scope.ps1` (27 checks): chat gate and AI gate use the
  unchanged vocabulary; no item/class/player/server branch in the new plan;
  no OCR/AI dependency in the new files.

## Stage evidence

Definitions used below:

| Stage | Meaning |
|---|---|
| recognised | equipment panel or field row is identified from OCR text/geometry |
| matched | generic fields are parsed and exact raw value spans retained |
| prepared | key label + protected value guard planned under the shared typography |
| painted | composition accepted and drew the complete key label |
| preservedReason | original pixels/text kept because the plan/composition was unsafe |

Actual captured evidence (see `evidence/equipment-panel.txt` and
`evidence/equipment-paint.txt`):

```
equipment-panel recognized=1 matched=13 prepared=13 painted=0 preserved=0
 keys=level,strength,intelligence,weapon-defense,magic-defense,intelligence,
 luck,upgrades,weapon-attack,critical-rate,avoidability,attack-speed,upgrades
 typography=family=Microsoft YaHei unit=Pixel size=12 floor=11
 reason=grammar-structure sourceRows=8

equipment-coverage recognizedPanels=1 recognizedRows=8 parsed=13 prepared=13
 valuePixels=13 partialValues=0 mergedText=0 preserved=0 preservedReason=none
 rejected=0 keys=...

equipment-panel recognized=0 matched=3 prepared=3 ... reason=field-row-fallback

equipment-paint generation=1 prepared=12 painted=12 preserved=0
 preservedReason=none

equipment-paint generation=1 prepared=1 painted=0 preserved=1
 preservedReason=equipment-field-does-not-fit

plan status=preserved preservedReason=merged-value-region-unsafe
 (field test; the unsplittable merged word keeps the original row)
```

The value-preservation check is behavioural: after composition the synthetic
pixel under every measured value is transparent/original, and the full raw value
string is still attached to the field evidence. Merged safe splits leave a
two-pixel gap before the value; unsafe splits are not painted at all.

## Tests

See `evidence/test-results.json` and the individual logs. All PASS results
below are against the clean-build EXE `638A2B3E...` (implementation head
`ce945b0`, 65 build inputs, clean worktree).

Passing on this host/build:

- new equipment tests: 63 + 22 + 28 + 7 + 27 = **147 checks**;
- `tests/logic-simple-flow-r11.ps1` — 33 checks;
- `tests/logic-quest-font-layout.ps1` — 51 checks (substitute Font/Graphics);
- `tests/logic-quest-panel-layout.ps1` — 164 checks (actual GDI+);
- `tests/logic-journal-and-item-r6.ps1 -ExePath .\枫语幕.exe` — 23/23;
- `tests/logic-quest-text-scope-r12.ps1` — 17;
- `tests/logic-quest-matching-r12.ps1 -ExePath .\枫语幕.exe` — 28;
- `tests/logic-observed-quest-wording.ps1 -ExePath .\枫语幕.exe` — 12/12;
- `tests/logic-chat-category-filtering.ps1` — pass (chat source untouched);
- `tests/logic-chat-continuity.ps1` — 9/9;
- `tests/logic-chat-megaphone-pixels.ps1` — 164 checks;
- `tests/logic-ui-dpi-scaling.ps1` — 100/125/150/175/200% and mixed DPI pass;
- `tests/logic-visual-panel-detection.ps1` — pass;
- `tests/logic-independent-window.ps1` — pass;
- `tests/logic-settings-npc-dialogue.ps1` — 82 setting entries pass;
- `tests/logic-release-safety.ps1` — 22/22;
- `tests/logic-followup-safety.ps1` — 11/11;
- `tests/logic-scene-feedback-safety.ps1` — 32/32;
- `tests/logic-capture-frame-policy.ps1` — 13/13;
- `tests/logic-user-feedback-safety.ps1` — 20/20.

## Pre-existing failures / not run

Confirmed on this branch and/or the untouched baseline, and unchanged by
EQUIP-001 (details in `evidence/safety-regressions-pre-existing.txt`):

- `logic-overlay-label-layout.ps1` — pre-existing cross-screen geometry assertion;
- `logic-overlay-paint-safety.ps1`, `logic-overlay-dual-rendering.ps1` — stale
  source-guard strings;
- `logic-panel-context-isolation.ps1`, `logic-quest-body-and-summary.ps1` —
  stale source guards;
- `logic-quest-title-groups.ps1` — pre-existing dictionary/index assertion;
- `logic-quest-shell-resilience.ps1` — clean synthetic scene still reports an
  empty scene on this host's Windows OCR;
- `logic-characters-r15.ps1` — stale hard-coded `D:\workspace\...` path;
- `logic-hotkey-toggle.ps1` — first-F8 new-screenshot self-check fails
  identically on the untouched baseline `399cc8e` EXE
  `E49562E787D8AC3FE2D7D2AC41D86E5A944325F9D302FA1E113F8A6ED09B11DB`
  (`evidence/pre-existing-hotkey-baseline.txt`).

Stale-path tests not run: `logic-quest-paragraph-pipeline-r12.ps1`,
`logic-quest-journal-r13.ps1`, `logic-universal-panels-r14.ps1`.

## Runtime / scope

- No additional OCR or AI rounds. The equipment plan is built from the existing
  OCR lines and cached; composition does not capture or recognize again. The
  paint test records `Captures==0 && OcrCalls==0`.
- No chat classification, quest matching, OCR model/protocol or AI request logic
  changes. The chat and AI gates keep the old vocabulary and are asserted by the
  scope test.
- No equipment-name, item-id, class, player or server-specific runtime branches.
- Shared FONT-001 principles: one equipment-panel family/unit/size, measured
  field wrappers, and original preservation when the complete key cannot fit.
- Real in-game visual acceptance (live game tooltip, real coloured modifiers,
  physical F8, actual 100/125/150/200% game scaling) is **UNVERIFIED**. The 63
  field checks use the built EXE's grammar/plan API with synthetic rectangles;
  the 28 panel checks use the exact production structured builder compiled from
  source with controlled OCR geometry; the 22 paint checks use a generated-source
  substitute Font/Graphics harness.
