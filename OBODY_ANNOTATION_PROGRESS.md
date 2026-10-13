# OBody Body Type Profile -- Annotation Rule Progress

Cross-session tracker for hand-authoring the **default Body Type Profile annotation rules** that
ship with SynthEBD -- the measurement/region rules inside each body type's rule file
(`<SynthEBD root>\Body Type Rules\<name>.json` -> `MeasurementProfile.Rules[]`; before `D43` they lived in
`OBodySettings.json` `BodyTypeProfiles[].Rules[]`) that
auto-assign body-shape descriptors to BodySlide presets. Unit of work = **(profile x descriptor
category)**. Goal for this phase: drive every **CBBE 3BA** cell to **Approved**.

> **Workflow (important):** **CBBE 3BA is the source of truth.** CBBE is *not* being authored
> independently right now -- once 3BA is fully locked in, its rules get **transferred over CBBE,
> replacing CBBE's existing rules wholesale** (measurements are vertex-index-independent -- bounding
> boxes / regions -- so the transfer is expected to be clean, aside from documented nuances). So:
> **drive 3BA to all-green, then transfer -> then approve CBBE.** See **Transfer plan** below.

> When you resurface from a multi-day revision, **read "Current focus" and the grid first** --
> everything marked Approved is still approved; you do not need to start over.

---

## How to read this

- **Scope:** the profile `Rules[]` engine only. The separate raw-slider layer (the rule file's
  `SliderRules`; pre-`D43` `BodySlideClassificationRules`) is **not** tracked here (see `D3`) -- but
  note it holds the 3BA `Belly=Muscular` rule that has no CBBE equivalent (nuance `N1`).
- **Active profile:** **CBBE 3BA** -- every measurement, key-vertex and rule edit happens here.
  **CBBE is deferred** (⏳): the profile currently in `Body Type Rules\CBBE.json` is a **backup copy of the
  pre-3BA-work rules, kept untouched and slated for deprecation** -- the transfer overwrites it
  wholesale (rules, measurements, key vertices). Its column below is a snapshot of that backup,
  not an independently maintained profile. Don't spend effort on CBBE cells now.
- **Source files (since `D43`):** the rules live in the active rule file per body type,
  `S:\Dev\MO2\mods\SynthEBD Portable - Patcher Dev\SynthEBD\Body Type Rules\CBBE 3BA.json` and
  `...\CBBE.json` (key `MeasurementProfile`); the worklist verdicts stay in
  `...\SynthEBD\Settings\OBodySettings.json` (`PresetAnnotations`, tagged by `BodyTypeName`). Offline tools
  resolve both through `obody_arm_tools/layout.py`; offline edits start from
  `obody_arm_tools/splice_template.py` (recipe: `obody_arm_tools/KV_AUTHORING.md`).
- **Worklist verdicts are not preset labels.** Committing a verdict in the annotation queue only records
  training data; press **Apply Annotations to Presets** (queue panel) to write them onto the presets as
  manual descriptors.
- **Refresh (run at the start of every session):** `python obody_scan.py` from the repo root.
  It re-reads the active rule files and prints live rule counts + content hashes + the dependency map, and
  flags **drift** on any Approved cell whose rules changed since approval. It writes nothing.
- **Reality vs approval:** counts and hashes are ground truth from the scan; status is your
  judgement. A drifted Approved cell means the rules moved out from under a prior sign-off.

**Status legend**

| Glyph | Meaning |
|:-----:|---------|
| ⬜ | **Not started** -- no rules for this cell |
| ✏️ | **Draft** -- rules exist, not yet reviewed |
| 👀 | **Review** -- awaiting my classification opinion / your sign-off |
| 🔧 | **Revise** -- reviewed, changes needed |
| ✅ | **Approved** -- signed off; terminal until deliberately reopened |
| 🟢 | **Provisional** -- evaluated and accepted as-is, but it has many upstream dependencies; holds unless a preset's assignment moves while other rules are revised, in which case re-investigate (drift-checked like Approved; verdict baseline on disk) |
| ⚠️ | **Drift** -- was Approved, but the scan shows the rules changed -> re-review |
| ⏳ | **Deferred** -- CBBE cell; will be derived from 3BA by transfer, not authored here |

---

## ▶ Current focus

- **Phase:** locking in **CBBE 3BA**. CBBE is parked (⏳) until the transfer.
- **▶ Shape review (2026-10-08):** judge `obody_arm_tools/shape_worklist.txt` (18 cases, seed 6161,
  `make_shape_worklist.py`; key `shape_worklist_key.json`). It replaces the never-judged `shape_d44` / `shape_d46`
  lists: both changes are live and the cache already carries `D46`'s armpit values (verified 2026-10-08: 23,767 /
  23,810 rows match the fixed pair). Shape's scanner drift (`5b89a36bcd` -> `f9454a5942`) is `D44`'s rule refit,
  which only a review + re-freeze clears -- no rescan is needed.
- **Seeded/reframed:** 2026-07-14. Nothing approved yet -- all 3BA cells **Draft**.
- **2026-09-18:** the annotation queue (`D28`) is live, so re-judging Belly is now a bounded,
  repeatable task: pick Belly, set `Random` (or leave `Spread` with the 0.25 random share), build,
  and label. The open Belly question from `D25` is unchanged -- swap Chubby's companion term to
  `butt_projection <= 7.41` and re-check on a random draw.
- **Suggested next:** start on the 3BA **leaf** categories (they have no cross-refs, so they're
  safe to lock first), then `Chest`/`Realism`, then `Build` last. Or jump to whichever category
  you were mid-revision on -- the grid will hold everything else in place.
- **2026-08-22:** Arms and Build are in 🔧 Revise. Pending decision: add `Arm_FrontBack_Ratio`
  (see Arms notes / `D7`), rescan (partial fill), tune its threshold in-app with the histogram
  (anchor between Mother Knows Best(25) and Adrianne(0)), re-scope Arms:Large and Powerful(F),
  then re-check the 23 reviewed presets. Rules are unchanged so far.
- **2026-08-24:** `D9` is now fully applied -- the three Arms rules gate the bulge on
  `Arm_Bicep_Ratio >= / < 0.4264` instead of `Arm_FrontBack_Ratio >= 0.970`. All 31 judged rows
  separate correctly on the in-app values, including the old Wrongii miss. **Arms and Build stay
  🔧 Revise pending your in-app pass** over the new Athletic / Powerful / Thick split and the
  Powerful-vs-Athletic Build boundary. Open items: the `[Arms:Athletic] AND [Thighs:Thick]` branch
  of Build:Powerful (Build notes), whether to delete the now-unused `Arm_FrontBack_Ratio`, the
  missing Male `Build:Athletic` rule, and `D10` (how BodySlide clamps out-of-range sliders).
- **2026-08-30:** `D13` applied -- `Build:Powerful`'s `[Arms:Athletic] AND [Thighs:Thick]` branch is now
  shoulder-aware (wide-shoulder branch at `shoulder_width >= 31.2` OR bulge branch at
  `Arm_Bicep_Ratio >= 0.442`), and `Build:Athletic` lost its `![Thighs:Thick]` guard. 11/11 judged rows;
  Powerful 1428 -> 1075. **Next: the user's in-app pass over the new Powerful/Athletic boundary.** Also
  unblocked: `Delt_Cap_Ratio` now covers all 25470 rows, so `D12` phase 2 (`apply_d12_rules.py`) can run --
  it is independent of D13 (no judged row's delt crosses its 0.46 gate except `Dino_3BA_032_Muscles`, which
  is Powerful either way).
- **2026-08-30 (later):** `D12` phase 2 applied too -- `ShoulderMorph` / `ShoulderWidth` leaves added, the
  Arms cap route live (`Delt_Cap_Ratio >= 0.46` lowers the volume floor to 330), `Build:Athletic` gained the
  `[ShoulderMorph:Muscular]` branch. **Both open shoulder items are now applied and the in-app pass covers
  Arms, Build and the two new leaf categories together.** Remaining open: the numeric-on-Build placement from
  `D13` (vs the leaf-category refactor), the Male `Build:Athletic` rule, and whether to delete the now-unused
  `Arm_FrontBack_Ratio`.
- **2026-09-07: `Arms` is ✅ APPROVED** -- the first cell to land. Arc: `D16` (the single miss
  the user found across the whole set) -> `D17` (synthetic fixtures probing the boundary D16 left) ->
  `D18` (bicep floor on the cap branch; D16's volume discontinuity retired) -> `D19` (dead branches and
  `arm_thickness_to_torso` pruned). Arms hash **`1a2a524462`**, 5 rules, 41 measurements.
- **2026-09-13 (latest): `Hips` is ✅ APPROVED** -- the second cell to land. The user re-based the cell
  in-app on an absolute **`hip_width`** (X span between the re-boxed `L_HipSide` / `R_HipSide` bulge
  vertices) in place of the old `hip_to_torso` ratio, and cut **Narrow < 25.5 / Wide > 30.3**. The cache
  was verified current for the new boxes (its `hip_width` fingerprint equals the one recomputed from the
  live definition), so the recorded populations are on the re-boxed vertices. Hips hash **`57873f5afc`**,
  3 rules. Side effect logged: the re-box also moved the *values* of 8 other hip-relative measurements
  (read by Belly / Realism / Shape / Waist) without moving any rule hash -- nothing approved is affected,
  but see `D20` for why the scanner could not have flagged it, and `N2` for the CBBE transfer.
- **2026-09-18: Belly is mid-revision and its live rules are NOT trusted.** `D25` is applied
  but scored **8/14** on a random spot check; the open action there is to swap the Chubby companion term to
  `butt_projection <= 7.41` and spot-check *that* on a fresh random draw.
  84 verdicts were banked (`obody_arm_tools/belly_verdicts.json` + `PresetAnnotations`); 62 of them
  (the Thin / Normal ones) have since been retired by `D29`, leaving 39 (23 Chubby + 16 Pregnant).
  Do not start `Build` until Belly lands -- Build reads Belly 28 times.
- **2026-09-18 (current): `D29` splits the low end of Belly four ways.** The user re-scoped the
  category to **Thin / Flat / Average / Chubby / Fat / Pregnant**: `Flat` is new, `Normal` is renamed
  `Average`, and `Thin` is being redefined from "flat belly" to "very thin trunk". **Step 1 is applied**
  (descriptor catalog + rename + annotation cleanup -- behaviourally inert, populations unchanged).
  **Step 2 (the rules) is proposed, not applied:** judge `obody_arm_tools/belly_d29_worklist.txt`
  in the annotation queue (`List` policy), then run `obody_arm_tools/apply_d29_belly_rules.py`. `D26` is subsumed by `D29`. Chubby / Fat / Pregnant are deliberately
  untouched, so `D25`'s open Chubby item survives into the new scheme unchanged.
- **2026-09-19 (current): `Belly` is gone -- split into `BellyShape` x `TrunkThickness` x
  `BellyMorph` (`D33`, applied).** Thresholds are provisional except the Thin cut; round 3
  (`obody_arm_tools/belly_d33_worklist.txt`, 42 cases) is staged to fit them. `Build:Chubby` is now
  derived from the two axes and jumped 3.3% -> 14.2% of the corpus -- check that before approving
  Build.
- **Next after Belly: `Build`** -- now the only 🔧 Revise cell, and the last tier-3 aggregator before the CBBE
  transfer. Its inputs are all still ✏️ Draft, so the suggested order is to lock the remaining leaves
  first (Belly, BustSize, Butt, Cup, Shape, Thighs, Waist, BustHeight, and the new ShoulderMorph /
  ShoulderWidth / BicepBulge), then Chest/Realism, then Build. Open Build items: the missing **Male**
  `Build:Athletic` rule, and `Build:Powerful(M)` still reading `[Arms:Athletic]` / `[Thighs:Thick]`
  unconditionally (the measurement cache is female-only, so neither can be validated offline).
- **2026-08-30 (latest):** `D14` applied -- `Build` is pure DescriptorRef again (new `BicepBulge` leaf;
  `ShoulderWidth:Wide` reused at 31.5). **`Build` must stay leaf-only from here** -- see `D14` for the
  convention and the warn-first escape hatch. Nothing is staged; the in-app pass is the only open work.

---

## At-a-glance status grid

**CBBE 3BA** is the active column. CBBE is deferred (its cells will be replaced by the transfer).

| Category    | CBBE 3BA (active) | CBBE (deferred) | Notes |
|-------------|:-----------------:|:---------------:|-------|
| Arms        | ✅ 5 | ⏳ 3 | **APPROVED 2026-09-07.** leaf; Athletic / Powerful / Thick (`D8`), bulge = `Arm_Bicep_Ratio` (`D9`), cap route (`D12`,`D16`,`D18`), pruned (`D19`) |
| BicepBulge  | ✅ 2 | ⏳ — | **APPROVED 2026-09-23.** leaf (`D14`); **3 values since `D41`**: Normal / Pronounced `0.437-0.442` / Very Pronounced `>= 0.442` on `Arm_Bicep_Ratio`; Build reads Very Pronounced only; 42/45 on the D41 worklist |
| BellyShape  | ✅ 2 | ⏳ — | **APPROVED 2026-09-21.** Flat / Protruding on `sternum_to_belly` (`Protruding > 0.85`); `Sucked In` dropped (`D35`) |
| TrunkThickness | ✅ 5 | ⏳ — | **APPROVED 2026-09-22.** Thin <9.60 / Medium / Thick >=12.30 / Very Thick >=15.70 on `Arch_to_BellyFlab`; Pregnant = belly bump AND (hips <30.55 OR arms `Arm_Pinch` <7.74) (`D34`, `D40`); 55/60 on judged rows |
| BellyMorph  | ✅ — | ⏳ — | **APPROVED 2026-09-21.** slider-only (`MuscleAbs`): Low Definition <40 / Toned 40-70 / Muscular 70-195 / Very Muscular >=195 (`D39`, `D39b`); judged with normal maps off. No measurement rules, so absent from the scan |
| BustHeight  | ✅ 3 | ⏳ 3 | **APPROVED 2026-10-08.** leaf; refit on the breast-region key vertices (`D49`, `D50`): Sagging ratio `>= 1.20` at proj `>= 12`, `>= 2.65` at proj 5-6; Lifted unchanged; 36/40 judged in 3D |
| BustSize    | ✅ 7 | ⏳ — | **RE-APPROVED 2026-10-08** (hash `a574fefdc7`, after `D51`; earlier approvals `fad252271d`, `15bb88b441`). 3BA-only; CBBE gains it via transfer (`D2`). Flat = `sternum_to_chest < 0.80` AND vol `< 300` (`D50`); Petite `< 300` / Small `< 360` / Medium `< 600` / Large `< 1150` / Very Large `< 1600` / Enormous on `L_Breast_Vol` (`D51`) |
| Butt        | ✅ 4 | ⏳ 4 | **APPROVED 2026-10-08.** leaf; Flat `< 2.05` / Medium (default) / Large `>= 3.09` / Very Large `>= 4.07` AND `GluteFold_Projection >= 4.10` on `fold_to_butt` (`D47`) |
| Chest       | ✅ 3 | ⏳ 0 | **APPROVED 2026-10-08.** Petite = BustSize Flat/Petite/Small, Medium = default (BustSize Medium), Busty = Large and up; 17/18 judged in 3D after `D51`. 3BA reads `BustSize`; CBBE's Cup-based Chest rules removed (`D42`) -- the transfer restores Chest there |
| Hips        | ✅ 3 | ⏳ 3 | **APPROVED 2026-09-13.** leaf; now `hip_width` (absolute X span of the re-boxed hip-side KVs) -- Narrow < 25.5 / Wide > 30.3; see `N2`, `D20` |
| Realism     | ✏️ 6 | ⏳ 6 | 3BA also reads `Belly` |
| Shape       | 🟢 5 | ⏳ 5 | **PROVISIONAL 2026-09-13** (evaluated + accepted; re-investigate if any assignment moves -- `check_shape_provisional.py`). Reworked (`D21`): hip-retuned cuts, `NOT [Shape:Inverted Triangle]` routing, hip-free Inverted Triangle gate; now reads `ShoulderWidth` (tier 2); default `Ambiguous` |
| ShoulderMorph | ✅ 1 | ⏳ — | **APPROVED 2026-09-26.** leaf (`D12`); Normal / Muscular on `Delt_Cap_Ratio >= 0.435`; feeds Build |
| ShoulderWidth | ✅ 2 | ⏳ — | **APPROVED 2026-09-26.** leaf (`D12`); Narrow < 28.0 / Medium / Wide >= 31.5 on `shoulder_width` (apparent width, deltoid-contaminated); read by Shape and Build |
| Thighs      | ✅ 3 | ⏳ 3 | **APPROVED 2026-09-29.** leaf; Skinny `< 11.65` / Normal (default) / Thick `>= 14.5` on `thigh_thickness`; read by Build (Curvy, Slight, Powerful) |
| Waist       | ✅ 3 | ⏳ 3 | **APPROVED 2026-09-28.** leaf; Pinched `< 0.54` / Neutral (default) / Flat `>= 0.64` on `waist_to_butt_width` (waist width / width at the butt, both X) -- replaces `waist_to_hip` (`D45`) |
| Build       | 🔧 8 | ⏳ 7 | aggregator -- approve **last**; Powerful/Athletic split (`D8`); rules untouched by `D9` but populations moved with Arms |
| **Total**   | **62** | **37** | `Cup` removed 2026-09-26 (`D42`) |

---

## Suggested approval order & re-review triggers (CBBE 3BA)

Derived from the `DescriptorRef` cross-references (a rule in category A that reads category B's
result). Lock **leaves first** so aggregators sit on stable ground; **`Build` last**.

- **tier 1 (leaves):** Arms, Belly, BustHeight, BustSize, Butt, Hips, Thighs, Waist (+ BicepBulge, ShoulderMorph, ShoulderWidth)
- **tier 2:** Chest (reads BustSize), Realism (reads Belly), Shape (reads ShoulderWidth since `D21`)
- **tier 3:** **Build** (reads Arms, Belly, Chest, Hips, Realism, Shape, Thighs, Waist)

**Re-review triggers** (change the left -> re-check the right):

| Change this (3BA) | Re-check |
|-------------------|----------|
| BustSize | Chest, Build |
| Belly    | Realism, Build |
| Arms / Chest / Hips / Realism / Shape / Thighs / Waist | Build |
| ShoulderWidth | Shape, Build |

The scanner reprints this map each run.

---

## Transfer plan (3BA -> CBBE)

**Trigger:** once every CBBE 3BA cell is ✅.

**Action:** copy 3BA's `Rules[]` (plus the `Regions`, `KeyVertices`, and `Measurements` they
depend on) over the CBBE profile, **replacing CBBE's existing rules**. Because the measurements are
vertex-index-independent (bounding boxes / regions), the geometry resolves on the CBBE mesh without
per-vertex fixups.

**Acceptance test:** after the transfer, run `python obody_scan.py`. For every cleanly-transferred
category, the **CBBE hash should equal the 3BA hash** (the scan makes this checkable at a glance);
anything that doesn't match is either a nuance below or an incomplete transfer. Then flip CBBE cells
⏳ -> 👀, verify, and approve.

**What the transfer resolves automatically:** CBBE gains `BustSize` (`D2`), and CBBE regains `Chest`
(its Cup-based Chest rules were deleted by `D42`), now reading `BustSize` (`D5`) -- both just fall out of
adopting 3BA's rules. Until then, CBBE assigns no Chest value.

**Transfer nuances (do NOT transfer 1:1 -- accumulate here as we find them):**
- **`N1` Belly=Muscular.** 3BA assigns `Belly=Muscular` in the **slider** layer (`MuscleAbs >= 70`);
  CBBE has no muscle slider, so there is no CBBE equivalent. This is slider-layer (`D3`), outside the
  tracked measurement rules. Side effect to remember: 3BA's *measurement* Belly rules carry a
  `!Belly:Muscular` guard (to avoid fighting the slider assignment); transferred to CBBE that guard is
  harmless but moot (CBBE never sets Muscular).
- **`N2` Hip-side key-vertex boxes (from the Hips approval, 2026-09-13).** 3BA's `L_HipSide` / `R_HipSide`
  are `BulgeMaxX` / `BulgeMinX` BoundingBox vertices on shape `3BA` at X +-23.493471, Y 64.86367..76.42886,
  Z -7.7910147..11.488601. The backup CBBE profile's same-named vertices still carry the **old box**
  (X +-17.993471, Y 68.36367..81.92886, shape `CBBE`, resolved v1484 / v1479), which feeds 17 of its
  measurements -- all of which the transfer replaces. The transfer
  must carry 3BA's boxes with `ShapeName` retargeted to `CBBE`, and because `hip_width`'s cuts (25.5 / 30.3)
  are absolute units rather than ratios, re-verify both cuts on the CBBE mesh before approving CBBE Hips.
- _(add more as they surface during 3BA review)_

---

## Per-category detail (CBBE 3BA first = active; CBBE = current, to be replaced)

Each cell: current facts from the scan + space for rationale and my classification opinions. Fill
the **Notes** line as we review.

### Arms
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 5 | Small, Medium, Athletic, Powerful, Thick | `Arm_Volume`, `Arm_Bicep_Ratio` (+ disabled `arm_thickness_to_torso` / `Arm_Volume` band on Medium; `Arm_FrontBack_Ratio` still defined but now rule-unused) | -- |
| CBBE (defer) | ⏳ | 3 | Small, Medium, Large | `arm_thickness_to_torso` | -- |

**Notes / opinions (2026-08-22 review; analysis toolkit in untracked `obody_arm_tools/`):**
- User review of Arms:Large on 23 presets (mostly the 350-368 volume band, i.e. "match strength
  0.28 sigma" above the 350 threshold): Small OK, Medium mostly OK, Large mixes muscular arms with
  soft/fat arms (Debra, Thicc Poser, Nami, C6723, Mother Knows Best, Pumpkin, Wrongii should be Medium).
- Root cause (slider ground truth from every cached preset's XML, corpus `S:\Temp\BS Trainer\mods`):
  `Arm_Volume` tracks `ChubbyArms` (r=0.75) and `BigTorso` (0.69) far more than `MuscleArms` (0.40).
  Today's Large bucket is ~47% ChubbyArms-driven and ~8% MuscleArms-driven, while 94% of
  MuscleArms-dominant presets sit *below* 350 (median 292). Raising the volume threshold alone makes it worse.
- `Arm_Bulge_Ratio` (mid/top depth) is the right idea but is confounded by the thinning sliders
  (`Arms`, `VanillaSSELo/Hi`) and by `MuscleMoreArms_v2`; AUC vs slider truth ~0.90.
- **Proposed measurement** `Arm_FrontBack_Ratio` = RatioDistance [L_ArmUpper_F, L_ArmMid_F] /
  [L_ArmUpper_R, L_ArmMid_R] (no new key vertices). The front upper-arm segment lengthens with the
  bicep belly (MuscleArms +0.030 per 100), the back segment with fat/frame (ChubbyArms +0.007,
  BigTorso -0.008). AUC 0.98 vs slider truth, split-half stable. Offline values: all 7 "should be
  Medium" examples <= 0.965, all "Large" examples >= 0.976; the one miss is Wrongii (0.986 --
  MuscleMoreArms_v2 70 + ChubbyArms 60, a genuine blend).
- **Proposed Large rule:** `Arm_Volume >= 380` OR (`Arm_FrontBack_Ratio >= ~0.975` AND
  `Arm_Volume >= 320`). Agreement with the review 22/23. Population: Large 2084 -> 2138 rows
  (536 demoted: 66% chub; 590 promoted: 64% bicep-muscle); chub share 47% -> 16%, muscle 8% -> 35%.
  Threshold must be re-tuned in-app (offline mesh is un-skinned, ~0.2-0.4 unit bias on shoulder
  vertices): anchor between Mother Knows Best(25) [below] and Adrianne(0) [above].
- Optional 4-vertex variant that also fixes Wrongii (MuscleMore-insensitive) is documented in the
  2026-08-22 chat summary; not needed for the primary fix.
- **Applied 2026-08-22** (`obody_arm_tools/apply_d7.py`, user had a backup): measurement added; Large now
  `Arm_Volume >= 380` OR (`Arm_FrontBack_Ratio >= 0.975` AND `Arm_Volume >= 320`). Replaced content, for
  the record: `[disabled] arm_thickness_to_torso >= 0.32` | `Arm_Volume >= 350` | `Arm_Bulge_Ratio >= 1.14
  AND Arm_Volume >= 300` | `Arm_Bulge_Ratio >= 1.11 AND Arm_Volume >= 325`. (Disabled branches were not kept:
  the Match-strength scorer does not skip disabled groups, so they would pollute the margin display.)
- **Rescanned + tuned 2026-08-22 (later):** in-app `Arm_FrontBack_Ratio` tracks the offline values almost
  exactly (mean -0.003, all 23 examples within -0.004; Spearman 0.84 overall, AUC 0.978 on the thick band).
  Anchors in-app: Mother Knows Best(25) = 0.9639 (must fail), Adrianne(0) = 0.9747 (must pass) -> threshold
  set to **0.970** (`set_fb_threshold.py`; 0.975 dropped Adrianne). Refreshed-cache result: Arms 22/23,
  Build 16/17 (only miss: Wrongii). Population: Large 2325 (was 2084), Medium 20428, Small 2717; Large is
  ~34% MuscleArms-driven / ~16% ChubbyArms-driven (was 8% / 47%).
- **2026-08-23 (`D8` applied, `obody_arm_tools/apply_d8.py`):** `Large` replaced by three granular values,
  all on `Arm_FrontBack_Ratio` (bulge = >= 0.970) x `Arm_Volume`:
  `Athletic` = bulge & 280 <= vol < 380 | `Powerful` = bulge & vol >= 380 | `Thick` = no bulge & vol >= 380.
  Small (< 220) and the Medium default unchanged; every row gets exactly one value. Floor 280 because the
  bulge ratio's precision collapses on thin arms (60%/15% MuscleArms>=50/<=10 at 220-260 vs 80%/2% at 280-300).
  Refreshed cache: Medium 18413, Athletic 3240, Powerful 692, Thick 408, Small 2717. Catalog
  (`TemplateDescriptors`) updated to match (Large removed; no asset pack referenced it).
- **2026-08-23 review of Arms:Powerful (bottom of the list by match strength):** user calls 3BA Callista Apple
  Shape(25), AwChubbyGirl(100), why did i do this 1 alt(25) **Thick**; Bulked Up 2(100), CP Orc(100), Custom Thicc
  Amazon 2(75), Sporty and Realistic ver.1+(75), PopcornBody(100) **Powerful**. All three Thick calls sit at
  FrontBack 0.970-0.975 (no-muscle arms lifted to the line by VanillaSSEHi / ChubbyArms); CP Orc sits at 0.970
  too but from the other side (MuscleArms 100 with BigTorso 100 suppressing the ratio). Best with existing
  metrics: Arms:Powerful gate `FrontBack >= 0.975` (Thick = < 0.975) -> 7/8 (CP Orc becomes Arms:Thick, Build
  still Powerful via the Thick branch); population precision at vol>=380 80% -> 84%. No existing cue (bulge
  ratio, frame width, belly) separates CP Orc from AwChubbyGirl. Offline search (`analyze16-18.py`) found a
  far cleaner bulge ratio (numerator ending on the existing L_ArmMid_F, long back-of-arm denominator: Muscle
  +0.031 per 100 vs Chubby +0.002 / BigTorso -0.003 / VanillaHi +0.005; AUC 1.000 on both halves; 8/8) -- needs
  new Coordinate key vertices, pending decision.
- **Candidate replacement bulge measure (`D9`, proposed):** `Arm_Bicep_Ratio` = |A - L_ArmMid_F| / |C - D| with three
  new Coordinate key vertices on shape 3BA (raw-NIF zeroed coords -> estimated in-app coords, expected index):
  A = front of upper arm just below the armpit, raw (14.424, 100.821, 2.040) -> ~(15.20, 100.87, 1.72), v10319;
  C = outer-lower back of arm, raw (19.253, 93.023, 6.917) -> ~(20.01, 93.04, 7.06), v10196 (least certain);
  D = inner-upper back of arm, raw (12.895, 99.038, 8.136) -> ~(13.66, 99.06, 8.28), v10246.
  Single-slider response per 100: MuscleArms +0.031 vs ChubbyArms +0.002, BigTorso -0.003, VanillaSSEHi +0.002,
  VanillaSSELo +0.005, Arms +0.007, MuscleMoreArms_v2 +0.004 (FrontBack for comparison: +0.030 / +0.007 / -0.008 /
  +0.034 / +0.008 / +0.006 / +0.022). Offline: AUC 1.000 on both split halves in both the 280-380 and >=380 bands;
  at threshold ~0.426 precision 99%/0% and recall 81-92% vs slider truth; orders all 39 judged presets correctly
  (highest no-bulge verdict 0.4249 = why did i do this 1 alt(25); lowest bulge verdict 0.4277 = Uthgerd(100)).
  Would replace FrontBack in the Athletic/Powerful/Thick rules. Scripts: analyze17-19.py.
- **2026-08-24 -- `D9` key vertices + measurement APPLIED** (`apply_d9_kvs.py`; rules untouched so far).
  Root discovery on the way there: the viewer's zeroed space is the **raw positions of
  `S:\Temp\BS Trainer\mods\Zeroed Sliders - 3BA\...emalebody_0.nif`** (the app runs under the BS
  Trainer MO2 instance) -- no skinning at all; the offline pipeline re-based onto it now reproduces the
  measurement cache to 0.00000 (see `KV_AUTHORING.md`). Exact Coordinate KVs written:
  `L_ArmBicep_Top` = v10319 (15.308515, 100.870605, 1.710520), `L_ArmLower_Back` = v10196
  (20.135916, 93.074181, 6.962451), `L_ArmUpper_InnerBack` = v10246 (13.667002, 99.046089, 8.215790);
  measurement `Arm_Bicep_Ratio` = |L_ArmBicep_Top-L_ArmMid_F| / |L_ArmLower_Back-L_ArmUpper_InnerBack|.
  On the exact pipeline it separates **all 39 judged presets** (every no-bulge verdict <= 0.4255 = why did
  i do this 1 alt(25); every bulge verdict >= 0.4273 = Uthgerd(100)); threshold **0.4264** (midpoint);
  slider-truth precision 98-100% with 0% no-muscle across all volume bands, recall 75/81/92%.
- **2026-08-24 (post-rescan) -- `D9` rule swap APPLIED** (`apply_d9_rules.py`). The three Arms rules now read
  `Arm_Bicep_Ratio` (Athletic/Powerful `>= 0.4264`, Thick `< 0.4264`); volume gates unchanged; Build untouched.
  Arms hash 71f7769e5a -> **913dcd9c2f** (still 5 rules). In-app verification on the refreshed cache
  (`analyze20.py`, real engine, all 25470 rows): **all 31 judged rows on the correct side, 0 misses**; highest
  no-bulge 0.4255 (`why did i do this 1 alt` 25) < 0.4264 <= lowest bulge 0.4273 (`Uthgerd` 100) -- the in-app
  midpoint is exactly the staged threshold. Population Arms: Medium 18413 -> **19348**, Athletic 3240 -> **2305**,
  Powerful 692 -> **535**, Thick 408 -> **565**, Small **2717** unchanged (5.1% of rows move).
  Verdict changes on the judged set: all three "Thick" calls (`why did i do this 1 alt` 25, `AwChubbyGirl` 100,
  `3BA Callista Apple Shape` 25) Powerful -> **Thick**; `CP Orc`(100) **stays Powerful** (which the 0.975
  FrontBack fallback would have broken); **`Wrongii`(100) Athletic -> Medium**, closing the one long-standing
  miss, so the judged set is now 31/31. Everything else unchanged. (Earlier notes said "39 judged presets";
  the judged set recorded in `analyze19.py`/`analyze20.py` resolves to **31 rows** carrying an explicit
  bulge/no-bulge verdict plus 5 Build exemplars — 31/31 and the 5 exemplars are what was actually re-verified.)
- **Gate note (why the shipped check had to be rewritten).** `apply_d9_rules.py`'s original gate demanded
  max |in-app - predicted| < 0.002 over all 8692 predicted rows and failed at **0.18925**. Diagnosed
  (`diag_d9{,b,c,d}.py`): not a D9 or app fault -- 95% of rows are exact (p50=p75=p90=0.00000) and the tail is
  **150 preset labels, 99.3% of them carrying slider values outside 0..100** (median 26 each; mostly the
  "1000 Randomly Generated Body Presets (Tribute Mod)" pack), the rest duplicate preset names across corpus
  XMLs -- **0 unexplained**. BodySlide/the app clamp per slider; `body.py` extrapolates linearly. The same 150
  labels break the *pre-existing* `Arm_FrontBack_Ratio` (511 rows) and `Arm_Pinch`/`Arm_Bulge` (up to 5.8 units
  on 729 rows) just as badly. The gate now asserts what actually validates the threshold -- full measurement
  coverage, both anchors, and zero judged-preset misses at 0.4264 -- and merely *reports* bulk agreement. See `D10`.
- **2026-08-24 (post parser-fix rescan) -- populations refreshed, thresholds untouched.** The `D10`/F1 fix
  (fractional slider values were being dropped) went in and the cache was regenerated. Verified the fix is
  live: the cache now matches **float** parsing (`diag_d9e.py` -- `ousnius - Random Preset 0256` w100 went
  from 0.18925 error to 0.00000; the polarity flipped, 307 rows now deviate under int-parse emulation and
  only 5 under float). **Judged set unchanged: 31/31, 0 misses**, anchors still 0.4255 / 0.4273 to 4 dp,
  in-app midpoint still exactly 0.4264. Arms hash `913dcd9c2f`, Build `0349b4d0c5`, no drift.
  New Arms populations (`postfix_report.py`): Medium 19348 -> **18969** (-379), Athletic 2305 -> **2668**
  (+363), Powerful 535 -> **545** (+10), Thick 565 -> **568** (+3), Small 2717 -> **2720** (+3).
  **Every delta is fully attributable to the 1000 previously mis-parsed presets** -- splitting the cache by
  affected/unaffected: unaffected rows show Thick 565 (identical to pre-fix), Powerful 532, Small 2704,
  Athletic 2052, and the affected 5000 rows account for the rest exactly. Unaffected presets kept their
  cache entries (hashes unchanged), so their verdicts could not move. Direction is as expected: presets
  that had been rendering near-zeroed (thin arms -> Medium) now carry real slider values and read Athletic.
  Residual offline-vs-cache disagreement is down to **5 rows on 2 presets**, both defined in two corpus
  XMLs -- the duplicate-preset-name case where the offline loader picks a different block than MO2's VFS.
  Toolkit artifact, not an app issue.
- **2026-09-07 -- `D18` + `D19` applied; cell APPROVED.** The user reviewed Arms across the whole set and
  found exactly one miss (`D16`). The boundary D16 left behind was then probed with purpose-built
  fixtures (`D17`) and closed with a bicep floor rather than a hair's-breadth cap bar (`D18`), and the
  cell was pruned of dead branches and `arm_thickness_to_torso` (`D19`, behaviour-neutral).
  Final shape (hash `1a2a524462`, 5 rules):
  `Small` = vol < 220 | `Medium` = default only (rule retained with zero groups) |
  `Athletic` = bulge & [280,330) or bulge & [330,380) & cap < 0.46 |
  `Powerful` = bulge & vol >= 380, or cap >= 0.46 & vol >= 330, or cap >= 0.4576 & vol >= 380 & bicep >= 0.4183 |
  `Thick` = the exact complement at vol >= 380.
- Status: ✅ **Approved 2026-09-07** (user's call). Terminal until deliberately reopened. Re-open if
  `Arm_Volume`, `Arm_Bicep_Ratio` or `Delt_Cap_Ratio` is retuned, or if the CBBE transfer surfaces a
  nuance. Residual non-blocking item: `Arm_FrontBack_Ratio` is still defined but read by no rule
  (keep as a diagnostic or delete -- user's call), along with 18 other unreferenced measurements.

### BellyShape / TrunkThickness / BellyMorph  (`Belly` was split by `D33`)
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **BellyShape** | ✅ | 2 | Flat, Protruding | `sternum_to_belly` ⭐ (`Protruding > 0.85`) | self |
| **TrunkThickness** | ✅ | 5 | Thin, Medium, Thick, Very Thick, Pregnant | `Arch_to_BellyFlab` ⭐; Pregnant: belly terms + `hip_width` / `Arm_Pinch` (`D40`) | self |
| **BellyMorph** | ✅ | 0 | Low Definition, Toned, Muscular, Very Muscular | slider `MuscleAbs` only (40 / 70 / 195) | -- |

**Everything below this line predates the split and uses the retired `Belly` vocabulary.** Values
map as Thin->`TrunkThickness:Thin`, Average->`TrunkThickness:Medium`, Flat->`BellyShape:Flat`,
Pregnant->`TrunkThickness:Pregnant`, Muscular->`BellyMorph:Muscular`; `Chubby` and `Fat` have no
equivalent -- they became `Build:Chubby`. Kept for the evidence trail.

#### Belly (retired)
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✏️ | 5 | **Thin, Flat*, Average** (`D29`), Chubby, Fat, Pregnant  \* `Flat` has no rule yet | `Spine_to_BellyFlab` ⭐, `sternum_to_belly` ⭐, `Back_to_Belly` ⭐, `BellyVolume`, `belly_projection`(+`_2`), `hip_width`, `belly_width_to_proj`, `belly_proj_to_hip`, `waist_width` (+ `pubis_to_belly` / `Arch_to_BellyFlab` / `Sacrum_to_BellyFlab` cached, unused) | `!Belly:{Chubby,Fat,Thin,Muscular,Pregnant}` |
| CBBE (defer) | ⏳ | 5 | Flat, Normal, Chubby, Fat, Pregnant | `belly_projection`, `belly_width_to_proj`, `waist_width`, `arm_thickness_to_torso` ⚠️ | `!Thighs:Thick`, `!Belly:Pregnant` |

**Notes / opinions (2026-09-18 -- `D29` step 1 applied; the Thin/Flat/Average rules are PROPOSED only):**
- **`D29` re-scoped the low end into four bins.** Descriptor catalog is now
  `Thin, Flat, Average, Chubby, Fat, Pregnant, Muscular`. **Applied:** `Flat` added, `Normal` renamed
  `Average` (every reference, both profiles, the slider-layer default and the category default), and the
  62 `Thin` / `Normal` `PresetAnnotations` deleted -- those verdicts predate the re-division and cannot be
  mapped onto it. 39 annotations remain (23 Chubby / 16 Pregnant), still valid. The rename is
  behaviourally inert: populations identical, Belly hash `8ce915f100` -> **`81ef53de5f`**.
  Backup `OBodySettings - Copy (42) - before Belly Flat descriptor + Average rename.json`.
- **NOT applied -- the rule set is a proposal awaiting the user's spot check.** See `D29` for the
  definitions, the two measurement artifacts that drove the axis choice, and the simulated populations.
  Apply with `python obody_arm_tools/apply_d29_belly_rules.py` (defaults `--ts 0.05 --td 11.38 --tw 15.09`).

**Notes / opinions (2026-09-18 -- `D25` applied; rules live but NOT validated):**
- WARNING **`D25` 2026-09-18 -- Chubby + Pregnant rewritten on a new lower-belly measurement; a random
  spot check then scored 8/14.** The rules in the file now are the `D25` ones. They fit the judged set
  well (74/84) but a *random* draw from the 541 rows they changed was only 8/14 correct. Treat the
  current Belly rules as **provisional and known-imperfect**; see `D25` for the diagnosis and the
  leading replacement. Belly hash `6c86c3730a` -> **`8ce915f100`**.
- **`Flat` was renamed `Thin`** across all five references (both profiles' rules + the descriptor
  catalog). Pure rename: populations identical. The *redefinition* the user wants (Thin = narrow
  trunk, not merely flat belly) was **not** done then -- it is `D29`'s step 2. The live rule is still
  `belly_projection < 12.5 AND belly_projection_2 < 7.0 AND ![Muscular]`. See `D26`, superseded by `D29`.
- Verdict corpus was **84 judged slices** (23 Chubby / 45 Normal / 16 Pregnant, 12 flagged
  borderline) in `obody_arm_tools/belly_verdicts.json`, back-filled into `PresetAnnotations`.
  `D29` retired the 45 Normal + 9 Thin annotations (62 slices incl. alias siblings); the JSON file is
  kept intact as the historical record, so *it* still holds all 84 -- do not fit Thin/Flat/Average on
  its `Normal` rows.

**Notes / opinions (2026-09-13 -- `D22` depth floor applied):**
- **`D22` 2026-09-13:** group 1 (the modest-belly branch) gained `belly_proj_to_hip >= 0.41` AND
  `belly_projection_2 >= 5.4`. It had no floor on how far the belly protrudes -- `sternum_to_belly >= 2.0`
  is met by any non-flat belly, the waist / hip terms are *upper* bounds, and `belly_width_to_proj > 0.75`
  screens beach-balls, not shallow bellies -- so slim-waisted wide-hipped presets with ordinary bellies
  read Pregnant. Pregnant 138 -> **127** rows; the 11 the user reported drop and nothing else moves.
  Details + thresholds in `D22`; applied by `obody_arm_tools/apply_d22_pregnant_depth.py`.
  Belly hash `65bb844ae5` -> **`b69abe8512`**.
- The live group-1 hip gate is **33.85**, i.e. the `D21` retune suggestion is now in the file.
- **`D23` 2026-09-14 -- double-assignment policy settled.** Belly is a **tag set, not a single**
  **value**: `Fat + Pregnant` is deliberately eligible for dual assignment (9 rows), and the
  `Fat + Muscular + Pregnant` triple on `Samaelxx46 (Fat) Very Fat` w100 is *correct* -- the preset is
  not a physiologically possible body and the classifier has to emit something. What is **not** allowed
  is `Muscular + Flat`: `Belly:Flat` now carries `![Belly:Muscular]`, matching the guard `Chubby` has
  had since `N1`. Inert unless the slider layer seeds Muscular (0 of 25500 rows move without it);
  11520 rows drop Flat when it does. Belly hash `b69abe8512` -> **`6c86c3730a`**.
- `!Belly:Muscular` guard relates to nuance `N1` (slider-layer Muscular).
- ⚠️ **`D6`** is about the *CBBE* rule using `arm_thickness_to_torso`; since CBBE is being replaced, this
  is moot unless 3BA has the same artifact (it doesn't appear to).

### BustHeight
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 3 | Lifted, Typical, Sagging | `chest_projection`, `chest_sag_ratio` | -- |
| CBBE (defer) | ⏳ | 3 | Lifted, Typical, Sagging | `chest_projection`, `chest_sag_ratio` | -- |

**Notes / opinions (2026-10-08, `D49`/`D50`; APPROVED 2026-10-08, hash `0b2170926e`):** ratio = `chest_sag_ratio` (vertical drop / forward projection from `ClavicleCenter` to `L_ChestFront`), proj = `chest_projection`; both now measured to the breast-region front point (nipple excluded, `D49`). Lifted = ratio `< 1.15`, or `< 1.30` at proj `< 12`. Sagging = ratio `>= 1.20` at proj `>= 12`, `>= 1.9` at `>= 8`, `>= 2.3` at `>= 6`, `>= 2.65` at `>= 5`. Typical = default. 36/40 judged in 3D; accepted misses Devonix FinVahzahSlen @25 (twin of SyndiChips @100), Flying-Dutchman-09 @100 and VanityElf Slim to Thiccc @75 (proj 12.0 edge), ousnius 0049 @75. Small busts need a high ratio to read as sagging because the ratio divides by a small projection. No category reads BustHeight, but Realism's `UnrealisticChestLift` uses the same two measurements, so a re-measure of `L_ChestFront` moves both.

### BustSize  (3BA-only; CBBE gets it via transfer)
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 7 | Flat, Petite, Small, Medium, Large, Very Large, Enormous | `L_Breast_Vol`, `sternum_to_chest` | `!BustSize:{Flat,Petite}` |
| CBBE (defer) | ⏳ | -- | -- | -- | -- |

**Notes / opinions (2026-10-08, `D50` + `D51`; RE-APPROVED 2026-10-08, hash `a574fefdc7` -- earlier approvals `fad252271d` (`D48b`), `15bb88b441` (`D50`)):** Flat = `sternum_to_chest < 0.80` AND `L_Breast_Vol < 300` (on the `D49` breast-region front point; was `< 1.0` on the nipple-tip point); volume bands since `D51`: Small `< 360`, Medium `< 600`, Large `< 1150` (were 450 / 750), set from the 3D Chest judging and confirmed by the user on Spread; Petite = remaining `vol < 300`; Small and above are volume bands. Flat|Petite fits all 21 judged rows (judged in 3D -- the 2D panel hides projection). Anchors: Flat TxAssassin AA-DD V2 @0 (0.965) / Petite Dino_3BA_003_Slim @50 (1.046). The Petite|Small and larger boundaries were reviewed on Spread and look right. Feeds `Chest`.

### Butt
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 4 | Flat, Medium (default), Large, Very Large | `fold_to_butt`, `GluteFold_Projection` | -- |
| CBBE (defer) | ⏳ | 4 | Flat, Medium, Large, Very Large | `butt_projection` | -- |

**Notes / opinions (2026-10-04, `D47`; APPROVED 2026-10-08, hash `916739853e`):** Butt = depth past the fold (breadth is Hips). Flat `< 2.05` / Medium (default) / Large `>= 3.09` / Very Large `>= 4.07` AND `GluteFold_Projection >= 4.10` (else Large), on `fold_to_butt` = Z from Coordinate KV `L_GluteUnder` to `ButtBack`. 56/59 on the judged slices (57 with Onasha-Maki accepted as Very Large); accepted misses KDA Ahri @100 and TadThicc4U @100 (judged Flat, rule Medium, inside the 2.05-2.20 noise band). `thighback_to_butt` was rejected: it understates presets whose thigh retreats before the fold (SweetHIPS). Full record: `obody_arm_tools/BUTT_MEASUREMENT_STUDY.md`. Anchors: Flat|Medium Slim Redone 1 @75 (2.02) / SweetHIPS @100 (2.08).

### Chest
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 3 | Petite, Medium, Busty | (none -- pure DescriptorRef) | `BustSize:{Flat..Enormous}` |
| CBBE (defer) | ⏳ | 0 | -- | -- | -- (its 3 Cup-reading rules were removed by `D42`) |

**Notes / opinions (2026-10-08, APPROVED, hash `92221f8130`):** pure mapping from `BustSize` -- Petite = Flat / Petite / Small,
Medium = category default (BustSize Medium), Busty = Large / Very Large / Enormous; i.e. `L_Breast_Vol` `< 360` / `< 600` / `>= 600`
since `D51`. Judged in 3D on `chest_worklist.txt` (18 cases at both edges, seed 5151): 17/18, accepted miss my ragged new 2 @100
(Petite at vol 378). The judging moved BustSize's bands (`D51`) rather than giving Chest its own cuts, so Chest stays
measurement-free. Re-check Chest whenever the BustSize buckets move. The transfer gives CBBE this Chest (`D5`). Read by Build.

### Cup -- REMOVED 2026-09-26 (`D42`)
Deprecated in favour of `BustSize`. Its 10 `chest_projection` bands (Flat, A..H, byte-identical in
CBBE / 3BA) are gone from both profiles, as are the catalog entry and every reference to it; see `D42`.

### Hips
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 3 | Narrow, Normal, Wide | `hip_width` (`hip_to_torso` still defined, rule-unused) | -- |
| CBBE (defer) | ⏳ | 3 | Narrow, Normal, Wide | `hip_to_torso` | -- |

**Notes / opinions (2026-09-13 -- APPROVED):**
- The user reconfigured the hip measurement in-app before sign-off. The cell now reads **`hip_width`**
  (`AxisDistance` on X between `L_HipSide` and `R_HipSide`) instead of the earlier `hip_to_torso` ratio,
  which stays defined but is read by no rule. The two hip-side key vertices were **re-boxed**: BoundingBox
  on shape `3BA`, criterion `BulgeMaxX` / `BulgeMinX`, box X +-23.493471, Y 64.86367..76.42886,
  Z -7.7910147..11.488601 (resolved to v14309 / v11926 at the last scan; the index is not part of the identity).
- **Final shape** (hash `57873f5afc`, 3 rules): `Narrow` = `hip_width < 25.5` | `Normal` = default only
  (rule retained with zero groups) | `Wide` = `hip_width > 30.3`. Both comparisons are strict, so a row at
  exactly 25.5 or 30.3 reads Normal.
- **Cache verified current for the new boxes**: the cache's stored `hip_width` fingerprint equals the one
  recomputed from the live definition + boxes via the app's own `ComputeMeasurementFingerprint` recipe
  (`44c4ada1...`), so the numbers below are measured on the re-boxed vertices, not stale values.
  Populations (25500 rows): Narrow **882** (3.5%) / Normal **16291** (63.9%) / Wide **8327** (32.7%).
  `hip_width` p5 / p50 / p95 = 25.93 / 29.10 / 35.15. Density at the cuts: 268 rows within +-0.2 of 25.5,
  **1075** within +-0.2 of 30.3 -- the Wide cut sits in the thick of the distribution, so even a small
  retune there moves many rows.
- **What the re-box silently moved.** `hip_width` is also read by `Belly:Pregnant` (`hip_width < 32.0`;
  4594 rows now sit at or above 32.0 and can never read Pregnant), and the two hip-side vertices feed
  8 more measurements -- `shoulder_to_hip`, `waist_to_hip`, `butt_proj_to_hip`, `belly_proj_to_hip`,
  `thigh_thickness_to_hip`, `hip_to_torso`, `armpit_to_hip`, `waist_height_ratio` -- read by the Belly,
  Realism, Shape and Waist rules. Their rule hashes did not change, but their values did, so review those
  Draft cells on the post-re-box cache. Arms is unaffected (`Arm_Volume` / `Arm_Bicep_Ratio` /
  `Delt_Cap_Ratio` touch no hip vertex).
- Status: ✅ **Approved 2026-09-13** (user's call). Terminal until deliberately reopened. Re-open if the
  25.5 / 30.3 cuts, the hip-side boxes, or their criteria change. Transfer nuance: `N2`.

### Realism
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✏️ | 6 | Unrealistic, UnrealisticChest, UnrealisticChestLift, UnrealisticButt, UnrealisticThighs, UnrealisticWaist | `chest_projection`, `chest_sag_ratio`, `butt_projection`, `thigh_thickness_to_hip`, `waist_to_hip` | self + `TrunkThickness:{Thin,Medium}` + `BellyMorph:Muscular` |
| CBBE (defer) | ⏳ | 6 | (same 6 values) | + `chest_proj_to_chest_width` | self only |

**Notes / opinions:** _(to fill)_ -- 3BA Realism additionally reads `Belly`, so Belly changes on 3BA
can flip Realism (tier-2, after Belly).
- **`D24` 2026-09-14:** `UnrealisticChest` gained a fourth group, `chest_projection > 16.0 AND
  `[Belly:Muscular]`, so a slider-assigned Muscular belly is treated like a Flat one instead of
  falling through to the unconditional `> 19`. Purely additive (138 rows gain the value when Muscular
  is seeded; 0 change otherwise). Realism `cdb43053d5` -> `79bd14de40`.
- **Watch for the seeded-category trap when re-judging:** any branch gated on `[Belly:Average]` or
  `[Belly:Thin]` silently stops applying to rows the slider layer labels `Muscular`, because a seeded
  category suppresses its own default. `UnrealisticChest` was the only Realism rule affected.
- **`D29` 2026-09-18 -- the same trap, re-armed by a new Belly value.** `UnrealisticChest`'s groups
  enumerate Belly values (`Average` 17.0 / `Thin` 16.0 / `Muscular` 16.0); adding `Belly:Flat` gives
  ~53% of rows a value none of them name, so they fall through to the unconditional `> 19.0` and
  `UnrealisticChest` drops **94 -> 67** with no Realism edit at all. `apply_d29_belly_rules.py` adds a
  fifth group (`chest_projection > 16.5 AND [Belly:Flat]`, between Thin's 16.0 and Average's 17.0),
  which puts it at **99**. **Any future Belly value must re-check this rule.** Realism hash moved
  `6c1c09c19d` -> `d0fbbb2e91` on the step-1 rename alone.

**▶ PARKED QUERIES -- settle these when Realism is taken up (user's call, 2026-09-19).** Belly work
is finished and deliberately did not touch Realism beyond keeping it compiling. Three things are
waiting here:

1. **`UnrealisticChest` g3 has lost its premise.** `D24` added
   `chest_projection > 16.0 AND [Belly:Muscular]` *because* a slider-seeded Muscular row never
   materialized a morphology value and so matched none of the other groups. Since `D31`/`D33` that
   is false: a muscular row now carries its own `TrunkThickness` and `BellyShape`, so g1
   (`> 17.0 AND [TrunkThickness:Medium]`) and g2 (`> 16.0 AND [TrunkThickness:Thin]`) fire on their
   own. Leaving g3 means **every** `BellyMorph:Muscular` row uses the lowest chest threshold
   regardless of how thick its trunk is. Decide: delete g3, or keep it as a deliberate "abs make a
   big chest read as unrealistic" rule.
2. **The group set enumerates thickness values and will silently go dead again.** g1/g2 name
   `Medium` and `Thin`; `Thick`, `Very Thick` and `Pregnant` match nothing and fall through to the
   unconditional `> 19.0`. That is the same trap `D24` and `D29` each hit once. Either cover every
   value or restate the rule as a numeric threshold that does not enumerate.
3. **Realism was never re-validated after the Belly split.** Its hash moved twice
   (`6c1c09c19d` -> `d0fbbb2e91` -> current) purely from reference rewiring, and no verdict has ever
   been collected for a Realism value. It is still ✏️ Draft and tier-2, so it must be re-checked
   after Belly lands regardless.

### Shape
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | 🟢 | 5 | Hourglass, Pear, Rectangle, Inverted Triangle, Apple (+ default `Ambiguous`) | `waist_to_hip`, `waist_width`, `shoulder_to_hip`, `armpit_to_hip`, `belly_projection`, `waist_to_shoulder` | `ShoulderWidth:Wide`, `!Shape:Inverted Triangle` |
| CBBE (defer) | ⏳ | 5 | (same 5 values) | (old metrics / old cuts) | -- |

**Notes / opinions (2026-09-13 -- reworked, `D21`; 🟢 PROVISIONAL the same day):**
- **Hip re-box retune applied consistently** (the user had started by hand and the half-applied state was
  broken: 6101 rows with *no* Shape in the 0.592-0.62 waist gap and 2173 rows with *two* Shapes from the
  1.115 gate vs 1.18 guards). Now: `waist_to_hip` 0.62 -> **0.592** (Hourglass <, Rectangle >=),
  `armpit_to_hip` 0.65 -> **0.618** (Pear <, Hourglass/Rectangle >=), `shoulder_to_hip` 1.18 -> **1.115**
  (Inverted Triangle >). These are the reclassification-minimising values from the hip-shift simulation
  (Shape movers 8885 -> 2540 vs the old box; coordinate optimum confirmed by a joint local search).
- **Routing:** Pear / Hourglass / Rectangle no longer carry a `shoulder_to_hip <=` guard; they carry
  `NOT [Shape:Inverted Triangle]` instead, so whatever the IT gate rejects falls through on the waist /
  armpit ratios. Apple is untouched (`belly_projection >= 17 AND waist_width > 21`; every other rule
  requires the complement), so a fully-measured preset always gets **exactly one** Shape -- verified
  0 multi-value rows and 0 empty rows over all 25500 cache rows.
- **Hip-free Inverted Triangle confirmation.** Prompted by `ousnius - Random Preset 0407`(75) (true V:
  shoulders 2.1x waist) vs `(YMAFRC) Rectangle-Shaped`(0) (average shoulders, waist ~ hips, but hips so
  narrow that `shoulder_to_hip` = 1.34): the old rule's only positive evidence was a ratio whose
  denominator is the hips. IT now also requires `waist_to_shoulder <= 0.5128` (== shoulder/waist >= 1.95;
  an existing, fully cached measurement, so **no rescan**) **OR** `[ShoulderWidth:Wide]` (>= 31.5). The
  taper keeps proportionally V-shaped small frames; the leaf keeps broad frames without waist taper.
  Population: IT 2971 -> **2007** at the 1.115 cut (964 demoted: 833 -> Rectangle, 131 -> Hourglass);
  final Shape populations Apple 234 / Hourglass 9510 / IT 2007 / Pear 10763 / Rectangle 2986.
- **Open judging set** (decides whether the `ShoulderWidth:Wide` branch stays -- drop it for "B" if broad
  but straight bodies read Rectangle): narrow frame + strong taper: `Body 5` w0, `Super Slim` w75,
  `CP HighElves` w0; broad frame + straight torso: `CBBE SportyBoy` w0, `Sporty and Realistic CBBE 3BA
  ver.1+` w25, `subtly angela (outfits)` w25; on the taper boundary: `Body 1` w50 (2.03),
  `Natural & Realistic Bodies - Annabelle` w0 (1.70).
- **`Ambiguous`** added to the Shape catalog and set as the category default. Logically unreachable for a
  fully measured preset (see routing); it only catches rows with a null Shape measurement (0 today).
- Applied by `obody_arm_tools/apply_shape_rules.py` (self-verifying; backup `OBodySettings - Copy (37) -
  before Shape rules edit.json`). Shape hash `892a87f521` -> **`5b89a36bcd`**. Shape now reads
  `ShoulderWidth`, so it is tier 2 and `ShoulderWidth` retunes must re-check Shape. Waist / Belly
  retune suggestions (0.521/0.590; Pregnant hip gate 33.85) are **not** applied -- see `D21`.
- Status: 🟢 **Provisional 2026-09-13** (user's call). The user evaluated the current assignments and is
  satisfied with them, but deliberately did **not** approve because Shape reads so many upstream
  measurements (hip / waist / shoulder / armpit vertices, `belly_projection`, `waist_width`) plus
  `ShoulderWidth`. **Standing condition:** it holds only while no preset's Shape assignment moves. Baseline
  verdicts for all 25500 rows are frozen in `obody_arm_tools/shape_verdicts_2026-09-13.json`; after
  editing any rule or measurement Shape depends on, run `python obody_arm_tools/check_shape_provisional.py`
  -- any moved row means Shape must be re-investigated (and the baseline re-frozen once it is). The
  scanner drift-checks the rule hash `5b89a36bcd` for provisional cells exactly as for approved ones.
  The judging set above stays available but is no longer blocking.

### Thighs
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 3 | Skinny, Normal, Thick | `thigh_thickness` | -- |
| CBBE (defer) | ⏳ | 3 | Skinny, Normal, Thick | `thigh_thickness` | -- |

**Notes / opinions (2026-09-29, APPROVED):** approved as-is after the user's review, hash `f03fecf044`
frozen. Skinny `thigh_thickness < 11.65`, Thick `>= 14.5`. The Normal range rule (`11.65-14.5`) is
disabled, so Normal comes from the category default. No rule edits. Build reads `Thighs:Thick`
(Slight guard, Powerful(F) branches 3-4, Powerful(M)) and `Thighs:Skinny` (Curvy guard, Slight branch 2),
so a later Thighs retune must re-check Build.

### Waist
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✅ | 3 | Pinched, Neutral, Flat | `waist_to_butt_width` | -- |
| CBBE (defer) | ⏳ | 3 | Pinched, Neutral, Flat | `waist_to_hip` | -- |

**Notes / opinions (2026-09-28, APPROVED, `D45`):** Pinched `waist_to_butt_width < 0.54`, Flat `>= 0.64`,
Neutral = default. Moved off `waist_to_hip` because hip width (inflated by thighs rising into the hips on
many CBBE presets) dominated that ratio. Waist width comes from the zeroed-space region `Back` (`D44`);
butt width from `L/R_WidthAtButt`. Judged by Spread, not worklists -- waist form needs neighbours for
comparison. Dependents: Build (`Build:Curvy` g1 reads `Waist:Pinched`). The CBBE transfer must carry the
`Back` region, the `WidthAtButt` key vertices and `waist_to_butt_width`.

### Build  (aggregator -- approve last)
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | 🔧 | 8 | Slight, Medium, Curvy, Chubby, Exaggerated, Powerful, Athletic | (none -- pure DescriptorRef) | Arms, Belly, Chest, Hips, Realism, Shape, Thighs, Waist |
| CBBE (defer) | ⏳ | 7 | (same 6 values) | (none -- pure DescriptorRef) | + also reads `Cup` |

**Presets to watch when Build is reviewed:**
- **`Dino_3BA_032_Muscles`** (all weights) -- user, 2026-09-23: *"most definitely looks Powerful rather
  than Chubby; it's not even close."* D13 judged it Powerful @75, but it now reads `Build:Chubby` at every
  weight: Chubby g0 `[TrunkThickness:Thick] AND [BellyShape:Protruding]` fires (both leaves post-date D13,
  from the `D33` Belly split), and Powerful's `![Build:Chubby]` guard then blocks it although its
  Powerful inputs hold (`Arms:Athletic`, `Thighs:Thick`, `BicepBulge:Very Pronounced`). Not addressed
  yet by the user's choice -- fix it when Build is reworked.

**Notes / opinions (2026-08-22):** roll-up of nearly everything; approve only after its inputs are
stable. (CBBE's current Build reads `Cup`; the transfer replaces it with 3BA's, which doesn't.)
- Powerful(Female) = Arms:Large AND Thighs:Thick AND not-chubby. The **Thighs gate** is why
  Rectangle / Inverted-Triangle bodies with muscular arms (Sporty, Fit&Realistic, Uthgerd, Adrianne,
  Proper Beef, WP Strong, DaughterOfMalacath, Strong Redone, Orc GravBusty) fall to Medium: all have
  `thigh_thickness` < 14.5. The user's "Rectangle + Arms Large -> Powerful" trend is exactly this.
- **Proposed:** Powerful(F) = [Arms:Large] AND ![Build:Chubby] AND ![Belly:Chubby] AND ![Belly:Fat]
  AND ![Belly:Pregnant], with Arms:Large re-scoped (muscle-aware, see Arms). Agreement 16/17 on the
  reviewed set (miss: Wrongii). Population Powerful 1151 -> ~1810 rows (4.5% -> 7.1%); Powerful
  composition chub share 14% -> 10%, bicep-muscle 32% -> 41%. Pear + muscular arms -> Powerful only
  when the belly is not Chubby/Fat (the existing belly guards); Nordic Curvy Amazonian stays Chubby.
- Alternative considered: pure-size Arms:Large (>= 380) + a co-firing `Arms:Muscular` value
  (needs the explicit Medium rule re-enabled with `![Arms:Large]`). Matches Build 16/17 too, but
  contradicts the user's own Arms verdicts (muscular 320-380 arms were called Large).
- Discrepancy to verify in-app: the engine mirror says Bulked Up 2 (75) is *already* Powerful under
  the current rules, yet it was listed as a miss.
- **Applied 2026-08-22:** Powerful(F) = `[Arms:Large] AND ![Build:Chubby] AND ![Belly:Chubby] AND
  ![Belly:Fat] AND ![Belly:Pregnant]` (the `[Thighs:Thick]` condition was removed; nothing else changed;
  the Male rule is untouched).
- Refreshed cache (threshold 0.970): Powerful = 1992 rows of 25470 (7.8%; was 1151 = 4.5%). Build 16/17 on the
  reviewed set; Bulked Up 2 (75) confirmed by the user as already Powerful before the change.
- **2026-08-23 -- Powerful vs Athletic split proposed (`D8`).** User exemplars: Dommy Mommy Thicc(100) =
  Powerful (warhammer barbarian), Valkyrie Shape - 3BA(75) = Athletic (fit rogue/tanner). Both are fully
  toned (FrontBack 1.04 / 0.98, all muscle sliders ~100), so the split axis is **mass**, not tone:
  Arm_Volume 488 vs 321, thigh_thickness 17.3 vs 13.8, shoulder 32.7 vs 30.5. Slider regression:
  `thigh_thickness` is pure mass (MuscleLegs ~0), `Arm_Volume` mostly mass (Chubby +135, BigTorso +81 per 100,
  MuscleArms +46), but `shoulder_width` is deltoid-contaminated (MuscleMoreArms_v2 +2.6 vs BigTorso +2.05).
  Proposal: Powerful(F) = [Arms:Large] & (Arm_Volume >= 380 OR [Thighs:Thick]) & not-chubby;
  Athletic(F) = FrontBack >= 0.970 & 280 <= Arm_Volume < 380 & ![Thighs:Thick] & ![Build:Powerful] & not-chubby
  (floor 280: precision of the tone gate 80%/2% there vs 60%/15% at 220-260). Population: Powerful 1254,
  Athletic 2302, none both. Optional frame knob `shoulder_width >= 33.0` flips Sporty/Fit&Realistic/
  Proper Beef/Wrongii to Powerful; knob V_big=360 flips most of the earlier 350-368 group. Awaiting decision.
- **2026-08-23 (`D8` applied):** Build now keys purely off the granular Arms values:
  Powerful(F) = [Arms:Powerful] | [Arms:Thick] | ([Arms:Athletic] & [Thighs:Thick]), each with the belly guards;
  **Athletic(F)** (new) = [Arms:Athletic] & ![Thighs:Thick] & ![Build:Powerful] & guards;
  Powerful(M) = [Arms:Powerful] | [Arms:Thick] | [Arms:Athletic] | [Thighs:Thick] (translation of the old
  `Arms:Large OR Thighs:Thick`; no Male Athletic rule yet). Catalog gained Build:Athletic.
  Refreshed cache: Powerful 1697, Athletic 2302, none both. Exemplars: Dommy Mommy Thicc w75/w100 Powerful,
  w50 Athletic; Valkyrie Shape Athletic at every weight. **Deviation from the D8 simulation (1254 Powerful):**
  the pure-DescriptorRef branch [Arms:Athletic] & [Thighs:Thick] also covers toned arms at 280-320 volume
  (the old numeric muscle gate started at 320) -- +443 rows; see the 2026-08-23 chat summary for their makeup.
- **2026-08-24 -- unchanged by `D9`, but the populations moved with Arms** (Build is pure DescriptorRef, hash
  still `0349b4d0c5`). Rows carrying **Athletic** 2302 -> **1684**, rows carrying **Powerful** 1697 -> **1404**
  (4.3% of all rows change Build). Exemplars hold: `Dommy Mommy Thicc` Powerful at w75/w100, `Valkyrie Shape`
  Athletic at every weight. One drift to eyeball: `Dommy Mommy Thicc` **w50** went Athletic -> Medium (its arms
  now read no-bulge); that weight was never judged.
- **2026-08-24 (post parser-fix rescan):** Build rules untouched (hash `0349b4d0c5`), populations moved with
  Arms: rows carrying **Athletic 1684 -> 2018** (+334), carrying **Powerful 1404 -> 1428** (+24). Exemplars
  hold. All movement is on the 1000 formerly mis-parsed presets; see the Arms notes.
- **2026-08-25 -- new review data bearing on the open `[Arms:Athletic] AND [Thighs:Thick]` question (`D12`).**
  User judged five rows currently Medium: `Dino_3BA_055_10_SlimMuscles_Weight_3` w75/w50 and
  `Dino_3BA_054_09_SlimMuscles_Weight_3` w50 -> **Powerful (low end)**; the same two presets at w25 ->
  **Athletic**. **All five are already `Thighs:Thick`**, so the existing Thighs branch cannot produce that
  split -- it would promote all five equally. The discriminator in the user's own calls is **`Arm_Volume`:
  Athletic at 305/314, Powerful at 339/348/376 -- a boundary at ~320-330**, which is exactly the numeric gate
  the D8 simulation assumed and the pure-DescriptorRef rewrite dropped. This is direct evidence for option
  (b) in the list below: re-add `Arm_Volume >= 320` to that branch only.
  - Consequence if adopted: `Build:Athletic`'s `![Thighs:Thick]` guard must also go, or the demoted w25 rows
    fall through to Medium instead of Athletic. `![Build:Powerful]` already keeps the two mutually exclusive.
  - ⚠️ **Blocked on `D12` regardless:** all five fail the bulge gate today (`Arm_Bicep_Ratio` 0.4216-0.4262
    vs 0.4264), so they are `Arms:Medium` and no Build branch reaches them. The volume-gate change only
    matters once the shoulder axis exists.
- **Open (raise before approving):** the `[Arms:Athletic] AND [Thighs:Thick]` branch of Powerful(F) still reaches
  toned arms at 280-320 volume, below the 320 gate the `D8` simulation assumed -- options: (a) leave it (mass
  comes from the thighs, so "Powerful" is arguably right), (b) re-add a numeric `Arm_Volume >= 320` condition to
  that branch only, (c) drop the branch so Powerful means Arms:Powerful|Thick alone. Also still missing: a
  **Male `Build:Athletic`** rule (the measurement cache is female-only, so it cannot be validated offline).
- **2026-08-30 -- `D13` applied: the `[Arms:Athletic] AND [Thighs:Thick]` branch is now shoulder-aware,
  which closes the long-standing (a)/(b)/(c) question above.** Neither (a) nor (b) nor (c): the branch was
  split in two rather than left, volume-gated, or dropped. `Build:Powerful(F)` branch 3 ->
  `... AND shoulder_width >= 31.2` OR `... AND Arm_Bicep_Ratio >= 0.442`; `Build:Athletic(F)` lost
  `![Thighs:Thick]` so demotions land on Athletic instead of falling through to Medium. 11/11 judged rows,
  Powerful 1428 -> 1075, hash `0349b4d0c5` -> `87a1b3ac79`. Full rationale, the judging set, the axis
  bake-off and the open placement question are in `D13`.
- Status: 🔧 Revise -- awaiting the user's in-app pass (Powerful vs Athletic split), now over the D13 split.

---

## Decisions & loose ends

Stable IDs (mirroring `CODE_REVIEW_NOTES.md`). Resolve during sessions; record the outcome inline.

- **`D1`** -- `Cup` is used by the rules (10 each) but was **not defined in `TemplateDescriptors`**.
  **RESOLVED 2026-08-30 -- added to the catalog** (`apply_d1_cup_catalog.py`), 10 values mirroring the
  live `chest_projection` thresholds. This was not cosmetic: `RebuildRuleTree` only builds tree nodes
  for catalog-backed (Category, Value) pairs, so all 20 Cup rules (10 per profile) were **orphans** --
  invisible and unreachable in the Rules tab, and `Cup:*` could not be picked in a DescriptorRef
  dropdown even though CBBE's `Build`/`Chest` rules reference it. No rule was touched. _(resolved)_
- **`D2`** -- Does CBBE need `BustSize`? **Effectively resolved by the transfer:** CBBE inherits 3BA's
  BustSize cluster. _(resolved-pending-transfer)_
- **`D3`** -- The `BodySlideClassificationRules` (raw-slider) layer is near-empty (one rule: 3BA
  `Belly=Muscular` at `MuscleAbs >= 70`, which is nuance `N1`). Populate it, or retire it? Out of this
  tracker's scope, noted for completeness. _(open)_
- **`D4`** -- These profile rules have **no delivery mechanism** to ship with the patcher yet; they
  only persist in the working `OBodySettings.json`. Needed before release. Downstream task. _(open)_
  - **2026-09-26 -- half done by `D43`:** the rules now live in shareable per-body-type files. What is
    still missing is shipping them -- see `D43` TODO 1.
- **`D5`** -- CBBE-vs-3BA content divergence is **expected and temporary** -- CBBE will be overwritten
  by the transfer, so don't reconcile CBBE now. Known 3BA choices to carry over: Chest reads BustSize
  (not Cup); Realism reads Belly; Build does not read Cup. _(resolved-pending-transfer)_
- **`D6`** -- CBBE `Belly` references `arm_thickness_to_torso` (likely a copy artifact). Moot for CBBE
  (being replaced); only relevant if the same pattern exists in 3BA -- it doesn't appear to. _(low priority)_
- **`D7`** -- 2026-08-22: proposed `Arm_FrontBack_Ratio` measurement + muscle-aware Arms:Large
  (vol >= 380 OR ratio-gated vol >= 320) + Thighs-free Powerful(F). Offline evidence in untracked
  `obody_arm_tools/` (README there). **Applied 2026-08-22** via `apply_d7.py` (JSON splice, validated: only the
  3BA Measurements list and the two rules changed). Rescanned and threshold tuned to 0.970 from the in-app
  anchors the same day; 22/23 Arms, 16/17 Build on the refreshed cache. _(applied + tuned; user review pending)_

- **`D8`** -- 2026-08-23: split Build:Powerful into Powerful (massive + toned) vs new **Build:Athletic** (toned,
  not massive). Proposal + evidence in the Build notes and `obody_arm_tools/analyze11-13.py`. Needs a
  `TemplateDescriptors` entry for Build:Athletic and, later, a Male Athletic rule (cache is female-only).
  **Applied 2026-08-23** via `apply_d8.py` together with the granular Arms values (Athletic/Powerful/Thick
  replacing Large) so Build stays pure DescriptorRef. Arms 3 -> 5 rules, Build 7 -> 8. _(applied; review pending)_

- **`D9`** -- 2026-08-23: replace `Arm_FrontBack_Ratio` with the cleaner `Arm_Bicep_Ratio` (3 new Coordinate key
  vertices; details in the Arms notes). Motivation: the Powerful-vs-Thick review showed FrontBack is lifted by
  VanillaSSEHi/ChubbyArms and suppressed by BigTorso, so CP Orc(100) and AwChubbyGirl(100) are inseparable on it.
  Key vertices + measurement applied 2026-08-24 (`apply_d9_kvs.py`); **rule swap applied 2026-08-24 after the
  in-app rescan** (`apply_d9_rules.py`, threshold 0.4264, Arms hash -> `913dcd9c2f`). 31/31 judged rows correct
  in-app; `Wrongii` fixed. _(applied; user review pending)_
  - Follow-up: `Arm_FrontBack_Ratio` is still defined (and cached) but **no rule reads it**. Keep as a diagnostic
    or delete it in the editor -- user's call.

- **`D10`** -- 2026-08-24: slider-value fidelity. **Full plan: [SLIDER_FIDELITY_PLAN.md](SLIDER_FIDELITY_PLAN.md)**
  (written for a separate session). The original framing -- "the app must be clamping out-of-range values" -- was
  **wrong**; root cause found the same day. Two independent items:
  - **F1 (confirmed bug, code fix).** `Settings_OBody.cs:261` reads slider values with `int.TryParse`, so any
    value written with a decimal point is **silently dropped** and that slider stays 0. Re-running the offline
    pipeline with int-only semantics reproduces the app's cache **exactly** on all 295 previously-deviating rows
    (`ousnius - Random Preset 0256` w100: app 0.42282, int-parse 0.42282, float-parse 0.61206). Affects **1000 of
    5094 cached presets (19.6%)**; worst keep 4 of 320 slider entries. Hits the renderer and the measurements
    together -- measurements are read off the renderer's deformed mesh (`BodySlideMeasurementEvaluator.cs:146`),
    and there is one parse site and one deform site.
  - **F2 (open, needs the in-game A/B).** Out-of-range values are applied **unclamped** by SynthEBD, the renderer
    and `body.py` alike (only *weight* is clamped, `BodySlideDeformer` L58/97). **4347 of 5094 cached presets
    (85.3%)** carry at least one. Whether BodySlide/the runtime morph path clamp is untested.
  - **F1 FIXED 2026-08-24** (staged, uncommitted): `BodySlideSlider.Big/Small` are now `float`, parsed via
    `Settings_OBody.TryParseSliderValue` (invariant culture, rejects non-finite). Tests in
    `SynthEBD.Tests/BodySlideSliderValueParsingTests.cs`; full suite green. Verified against the live cache that
    exactly the 1000 mis-parsed presets re-hash and no others (`verify_hash_stability.py`).
  - **F2 ANSWERED 2026-08-24 -- BodySlide does NOT clamp; SynthEBD was already correct.** Built `TopHeavy4`
    (stock `CBBE 3BBB Body Amazing`, 95 morph sliders, 31 out of range, all integer): literal RMS **0.00014**
    (w0) / **0.00026** (w100) vs clamped **2.27** / **7.71** (`test_clamp_answer.py`). Also re-confirms the
    renderer's deformation math reproduces real BodySlide output vertex-for-vertex -- as does the shipped
    `Hanna 3BBB Body Preset` export (RMS 0.0000 both weights, `verify_vs_built_nifs.py`). No deformer change.
  - **Net effect on this tracker: none of the thresholds move.** The feared branch (a clamp re-measuring 85% of
    presets and invalidating 0.4264 + the 220/280/380 volume gates) is dead. The pending rescan carries only the
    F1 fix, which touches no judged preset (`diag_judged_exposure.py`: 0 of 36).
  - Runtime path now also confirmed from source: OBody parses slider values as `as_float()` and applies them
    with no clamp (`PresetManager.cpp` `SliderSetFromNode`, `Body.cpp` `ApplySlider`). _(resolved)_

- **`D12`** -- 2026-08-25: **add a `Shoulders` leaf category driven by a new `Delt_Cap_Ratio` measurement**
  (proposed, nothing applied). Prompted by five newly-judged rows on `Dino_..._SlimMuscles_Weight_3` presets
  that read Athletic/Powerful but classify Medium, plus the observation that the whole `Dino_*_Muscles`
  family has trained-looking "capped" shoulders no rule can see.
  - **Root cause: the deltoid axis is unmeasured.** Every one of those presets has `MuscleMoreArms_v2 = 100`
    (deltoid) with `MuscleArms` only 30-39 (bicep). `Arm_Bicep_Ratio` was deliberately built to *ignore*
    `MuscleMoreArms_v2` (D9: +0.004 per 100), so nothing in the profile responds to shoulder cap.
  - **The bicep threshold cannot be retuned to fix it.** `Dino_3BA_055_10_..._Weight_3`(75), which the user
    wants promoted, has `Arm_Bicep_Ratio` **0.4255** -- identical to `why did i do this 1 alt`(25), which the
    user judged Thick/no-bulge. Any threshold move promotes both. A new axis is required.
  - **Proposed measurement** `Delt_Cap_Ratio` = `|L_DeltCap - L_DeltIndent| / |L_ArmLower_Back - L_ArmUpper_InnerBack|`
    (RatioDistance; denominator reused from `Arm_Bicep_Ratio`, which is what cancels the mass confounders).
    Two new Coordinate KVs: **`L_DeltCap` v10559** `(13.584476, 105.488136, 7.283933)` nn-margin 0.772,
    **`L_DeltIndent` v10605** `(11.127712, 102.033989, 0.643843)` nn-margin 0.215.
  - **Evidence** (`analyze23/24/25_delt*.py`): per-100 response `MuscleMoreArms_v2` **+0.2593** vs worst
    confounder `BigTorso` 0.0770 -- **3.4x margin**; **AUC 0.999** vs slider truth, 0.999 on both split
    halves. Separation on the judged/reference set is huge: capped 1.1335-1.1700 vs plain 0.8900-0.9438,
    **midpoint 1.0386** (vs the bicep ratio's 0.4255/0.4273). Population at that threshold: 718 rows (2.8%),
    81% with `MuscleMoreArms_v2 >= 50`, 3% with <= 10.
  - **Note on the box criteria:** `PinchMaxX`/`BulgeMaxX` bin by *height* and pick the extreme X per bin.
    The upper arm is diagonal in the bind pose, so its max-X-per-Y silhouette is monotonic (measured,
    `analyze22_silhouette.py`) and no cap/indent extremum exists to find. Coordinate KVs are the right
    tool here, as in D9 -- do not try to use an adaptive criterion.
  - **2026-08-25 -- DESIGN AGREED, PHASE 1 APPLIED.** User's answers: the five rows are **both** Arms and
    Build verdicts; two new leaf categories rather than a numeric condition on Build.
    - **`ShoulderMorph`** [Normal | **Muscular**] <- `Delt_Cap_Ratio >= 1.04`; feeds Build, so Build stays a
      pure DescriptorRef aggregator.
    - **`ShoulderWidth`** [Narrow < 28.0 | Medium | Wide >= 31.5] on the existing `shoulder_width`.
      ⚠️ That measure is deltoid-contaminated (`MuscleMoreArms_v2` **+3.077** per 100 vs `BigTorso` +1.814),
      so "Wide" means broad-*looking*, not broad-framed. `armpit_width` is no better (2.258 / 1.452); a
      frame-only axis would need a new clavicle-span measurement.
    - **Arms gains a capped-shoulder route to Powerful**: the cap lowers the volume floor from 380 to 330.
      `Powerful += [Delt_Cap_Ratio >= 1.12 AND Arm_Volume >= 330]`; `Athletic` regrouped into
      `[bulge, 280-330)` and `[bulge, 330-380, Delt < 1.12]`; `Thick += Delt < 1.12`. Mutually exclusive --
      every row still gets exactly one Arms value.
    - **Two thresholds on one measurement, deliberately.** 1.04 = "has a visible cap" (the descriptor);
      1.12 = "pronounced enough to call the whole arm powerful". A single 1.04 gate moved **7** judged rows
      including `Wrongii`(100) Medium -> Powerful, contradicting the verdict D9 had just fixed. At 1.12 only
      **4** move, all originally judged "bulge (Large/Powerful)": `WP Strong`(100), `3BA_Orsimer_Muscle_Mother`(75),
      `Uthgerd`(100), `3BA Proper Beef`(50). Volume cannot separate them (they sit at 350-366, inside the
      339.5-375.7 range of the user's own Powerful calls); the deltoid threshold is the only axis that can.
      User accepted the four movers.
    - Simulated result (`analyze27_simulate_delts.py`): **10/10** on the five newly judged rows (Arms and
      Build). Population: Arms Powerful 545 -> 634, Athletic 2668 -> 2622, Thick 568 -> 531, Medium -6;
      Build rows carrying Athletic 2018 -> 2234, Powerful 1428 -> 1464.
    - **Phase 1 applied 2026-08-25** (`apply_d12_kvs.py`): the two KVs + `Delt_Cap_Ratio` are in the JSON,
      **rules untouched** (Arms `913dcd9c2f`, Build `0349b4d0c5` unchanged). Offline predictions for all
      25470 rows written to `delt_exact_rows.json`.
    - **Phase 2 staged** (`apply_d12_rules.py`): adds both categories, the Arms regrouping, the Build branch,
      defaults and catalog shells. Self-verifying -- it checks cache coverage + four anchors against the
      predictions, then simulates the edited profile over the whole cache and refuses to write unless the
      five judged rows land as judged, exactly the four expected movers move, and no row gains a second Arms
    - **Phase 2 APPLIED 2026-08-30** (`apply_d12_rules.py`, dry-run then write; backup taken). The coverage
      gate cleared -- `Delt_Cap_Ratio` now on all 25470 rows -- and **all four anchors reproduce the offline
      predictions to 0.00000** (bulk: p90 0.00000, 29 rows > 0.002, the usual out-of-range-slider tail).
      Gates passed: five judged rows all as judged, **0 rows with more than one Arms value**, and exactly the
      expected mover set (`WP Strong (3BA)`, `3BA_Orsimer_Muscle_Mother`, `zGranhadd_3BA#005(Uthgerd)`,
      `3BA Proper Beef`, `Maestrotuna (Petite) SandwichFighter LB`); 83 rows change Arms in total.
      Rules 65 -> 68; Arms hash `913dcd9c2f` -> **`b44f21b3ba`**, Build `87a1b3ac79` -> **`47e750caae`**
      (the new `Build:Athletic` shoulder branch), plus the new `ShoulderMorph` (1 rule, `921369a744`) and
      `ShoulderWidth` (2 rules, `7954e4e2c1`) cells.
      Populations: Arms Athletic 2668 -> **2598**, Powerful 545 -> **628**, Thick 568 -> **561**,
      Medium 18969 -> **18963**, Small 2720 unchanged. Build Athletic 2371 -> **3015**, Powerful
      1075 -> **1126**, Medium 14700 -> **14175**. New leaves: ShoulderMorph Muscular **1192** (4.7%);
      ShoulderWidth Narrow **3889** / Medium **18570** / Wide **3011**.
      **D13 regression check: all 11 D13-judged rows still land as judged** under the combined profile
      (only `Dino_3BA_032_Muscles` reads `ShoulderMorph:Muscular`, and it was Powerful either way).
      Note the phase-2 `Build:Athletic` branch was authored without a `![Thighs:Thick]` guard, which D13
      had already removed from that rule's other branch -- the two changes are consistent.
      _(phase 1 + phase 2 applied; user review pending)_
    - **ShoulderMorph APPROVED (user, 2026-09-26)**, hash `921369a744` frozen. The live cut is
      `Delt_Cap_Ratio >= 0.435` (the `1.04` above predates the metric's rescale).
    - **ShoulderWidth APPROVED (user, 2026-09-26)**, hash `7954e4e2c1` frozen. Narrow `shoulder_width
      < 28.0` / Wide `>= 31.5` (Medium default). Both D12 leaves are now approved.

- **`D24`** -- 2026-09-14: **`Realism:UnrealisticChest` assumed Belly is always Flat or Normal**
  (applied, `apply_d24_unrealistic_chest_muscular.py`). Its two lower branches were
  `chest_projection > 17 AND [Belly:Normal]` and `> 16 AND [Belly:Flat]`. A category seeded from the
  slider layer counts as **covered**, so a `Belly=Muscular` row never materializes the `Normal`
  default -- and after `D23` it carries no `Flat` either, leaving only the unconditional `> 19`.
  Fixed with a fourth group, `chest_projection > 16.0 AND [Belly:Muscular]`: same threshold as the
  Flat branch, because a muscular abdomen is as un-round as a flat one for this heuristic. The `> 19`
  and `Normal` branches are untouched.
  - Verification: 0 of 25500 rows change unseeded; seeded, 138 rows gain `UnrealisticChest` (3 of
    them `Build:Exaggerated` in consequence) and **every** change is purely additive -- no Realism,
    Build or Belly value is lost anywhere.
  - This restores the 11 rows `D23` cost (`Finale` w25/50, `Golden Rounded` w0/25, `Ku Box SUPER
    SHIZO my fake CBBE 3BA` w75, `Lexi` w75/100, `Lineage` w100, `Norman73Spear's (Cursed) Dorita
    Body` w50, `Pyramid True` w0/25) **and** fills the older hole predating it: Muscular rows whose
    Belly would have been Chubby (suppressed by Chubby's own `![Belly:Muscular]` guard) never had the
    branch either. Net vs pre-`D23`: +127 Realism rows, +1 Build row. In the 10%-coverage measured
    subset, 4 of 761 genuinely-Muscular rows change today.
  - Realism `cdb43053d5` -> **`79bd14de40`**. Backup `OBodySettings - Copy (40) - before
    UnrealisticChest muscular branch.json`. Realism stays ✏️ Draft and is to be re-judged in a
    later session; this only makes the rule mean what it says before that pass starts. _(applied)_

- **`D23`** -- 2026-09-14: **Muscular excludes Flat** (applied, `apply_d23_muscular_excludes_flat.py`).
  The post-`D22` double-assignment audit found 9 `Fat+Pregnant` rows plus a cross-layer
  `Muscular + Flat` overlap. User's calls: **keep** Fat+Pregnant dual (and the `Samaelxx46 (Fat) Very
  Fat` w100 triple, which describes an impossible body honestly); **exclude** Muscular+Flat. So
  `Belly:Flat` gained `![Belly:Muscular]` -- the same guard `Chubby` carries -- and Fat / Pregnant /
  Chubby / Normal were left byte-identical.
  - No belly measurement separates the one plausibly pregnant row (`Nero Preggo`) from the three fat
    ones (`JS Fat`, `KoolCattt's SSBBW`, `Very Fat`): the only candidates were arm / chest / thigh /
    waist-height measurements, i.e. overfitting 1 preset against 3. That is *why* the pair is policy,
    not a threshold -- do not revisit it as a tuning problem.
  - Verification: 0 of 25500 rows change without a Muscular seed (the guard is inert in the current
    Label-by-Measurements view); 11520 rows drop Flat when seeded; 218 rows / 73 presets are affected
    today in the 10% of the corpus whose preset XMLs resolve on this machine. Backup
    `OBodySettings - Copy (39) - before Belly Flat muscular guard.json`. Knock-on -> `D24`.
  - **Coverage caveat:** `obody_arm_tools/sliders.py` resolves only 1048 of the 5100 cached presets
    here (two roots are stale scratchpad paths), so every "how many rows are Muscular today" figure is
    a sample. Repoint those roots for a census. _(applied)_

- **`D22`** -- 2026-09-13: **`Belly:Pregnant` group 1 gains a depth floor** (applied,
  `apply_d22_pregnant_depth.py`). The user reported 11 rows as wrongly Pregnant -- the `Z-O-E` family
  (`27-29` w50/75/100, `29 push` all five, `29-31` w0, `87-89` w100) and `CBBE Gen` w100 -- all of them
  via group 1, none via group 2. Fix = two appended conditions:
  - `belly_proj_to_hip >= 0.41` -- frame-scaled depth floor (belly projects >= ~41% of hip width).
    10 of the 11 cap out at **0.3992**; lowest true positive **0.4205** (`Curse of Eden` w50), so 0.41
    is mid-window. Preferred over `belly_projection >= 13.5` (window 0.95% wide) and a
    `belly_width_to_proj <= 1.31` ceiling (1.05%): ~5x the margin and it normalises by frame size.
  - `belly_projection_2 >= 5.4` -- catches the 11th, `CBBE Gen` w100 (**4.716**), whose belly barely
    reaches the clavicle plane while its narrow 29.6 hips clear the ratio. Next-lowest value in the
    Pregnant set is 6.115, so that window is 23% wide.
  - Group 2 deliberately untouched, so the 11 big-belly rows that only match it are safe by construction.
  - Whole-cache verification: Pregnant 138 -> 127, exactly the 11 reported rows leave (10 -> Normal,
    `Z-O-E 87-89` w100 -> Flat), Realism and Shape unchanged, and **one** accepted knock-on:
    `CBBE Gen` w100 `Build: Medium -> Powerful` (its arms already qualify; only the `![Belly:Pregnant]`
    guard held it back). Backup `OBodySettings - Copy (38) - before Belly Pregnant depth floor.json`.
  - **Tightest keeper:** `ousnius - Random Preset 0042` w100 (`belly_proj_to_hip` 0.4321,
    `belly_projection_2` 6.144) looks geometrically like a Z-O-E; the user reviewed it and judged it
    justifiably Pregnant, so it stays. A stricter single condition (`belly_projection_2 >= 6.6` or
    `belly_width_to_proj <= 1.16`) would have dropped it -- do not "simplify" the pair into one.
  - **Not applied, user's call:** a `![Belly:Flat]` guard on both Pregnant groups. `Z-O-E 87-89` w100
    was labelled **Flat and Pregnant at once** (Pregnant has no Flat guard; Chubby has its own), which
    D22 fixes only incidentally. The guard is a no-op on today's corpus and would pre-empt a petite
    narrow-hipped preset re-triggering the overlap. _(applied; guard pending)_

- **`D21`** -- 2026-09-13: **Shape rework after the hip re-box** (applied, `apply_shape_rules.py`).
  Three things in one edit: (1) the reclassification-minimising retune of the three hip-relative cuts,
  applied to *every* condition that shares them (the hand edit had only done half); (2) the lower rules
  route on `NOT [Shape:Inverted Triangle]` instead of a duplicated `shoulder_to_hip <=` guard, so the IT
  gate can be redefined freely without orphaning bodies; (3) IT requires a hip-free confirmation,
  `waist_to_shoulder <= 0.5128` OR `[ShoulderWidth:Wide]` (candidate "C"; "B" = taper only, "A" = an
  absolute shoulder floor were the alternatives -- the floor is scale-dependent and demotes petite true
  V's such as `Body 5`). Plus a `Shape:Ambiguous` default. Details + judging set in the Shape notes.
  - **Not applied, for the user's call:** the same simulation's Waist retune (Pinched < **0.521**, Flat >
    **0.590**; movers 9078 -> 2041) and the Belly:Pregnant hip gate (**33.85**, anything in 33.75-33.95;
    movers 27 -> 5). Residual movers per row: `obody_arm_tools/hip_shift_retune_residual.csv`; the
    unrecoverable rows are the presets whose hip shift is atypical (47% / 39% outside the 0.9-2.1 band
    vs 21% overall). Full pre-retune mover list: `obody_arm_tools/hip_shift_movers.csv`.
  - Method note: old-box values came from the Aug-25 cache snapshot in `MeasurementCache\New folder`
    (fingerprints match `OBodySettings - Copy (34)`), so nothing was re-measured offline. _(applied)_

- **`D20`** -- 2026-09-13: **the scanner's drift detector is blind to measurement / key-vertex edits.**
  `category_hash` in `obody_scan.py` covers a category's rules + default value only. The Hips re-box
  changed `hip_width`'s value on every row -- and the values of 8 other measurements sharing the hip-side
  vertices -- while moving no hash except Hips' own, and that moved only because the *rules* changed too.
  An approved cell whose measurement definition or key-vertex box is retuned later would still print
  "No drift". The app already has the right identity, `MeasurementCacheStore.ComputeMeasurementFingerprint`
  (rule-independent; reproduced in Python for the Hips check, see the Hips notes). Proposed fix: per
  category, fold the fingerprints of every measurement its rules read (transitively through DescriptorRefs)
  into a second "inputs" hash recorded at approval, and flag when *that* moves. Not implemented. Until
  then, re-open approved cells by hand whenever their measurements are touched (Arms' note lists its three;
  Hips' note lists its two boxes). _(open)_

- **`D19`** -- 2026-09-07: **Arms cell pruned before approval** (applied, behaviour-neutral).
  `apply_d19_prune.py`: dropped `Arms:Small`'s disabled group, both of `Arms:Medium`'s (leaving that
  rule with **zero** groups -- it can never fire, and `Arms=Medium` was already produced by the profile
  default), and the measurement `arm_thickness_to_torso`, which only those groups referenced. The
  deferred CBBE profile keeps its own copy (Measurements are per-profile) -- asserted.
  Simulated over the whole cache: **0 of 25500 rows change their Arms value**. Measurements 42 -> 41;
  Arms hash `8f62026f34` -> **`1a2a524462`**. No rescan needed: hydration drops values for measurements
  no longer defined, and completeness is checked against the CURRENT set.
  - **Correction recorded:** the `HANDOFF.md` guardrail "disabled branches still feed the Match-strength
    scorer, so delete rather than mute" was **stale** -- the scorer skips disabled groups
    (`VM_BodyTypeProfileEditor` `if (group.IsDisabled) continue;`). So the pruning was hygiene, not a
    display fix, and `Arms:Medium` had no match strength either way. Guardrail since corrected.
  - That gap prompted the **default-value margin score** feature: a category default now scores as
    `-max(rival rule scores)`. Designed and implemented in a separate session -- see
    **[DEFAULT_MARGIN_SCORE_PLAN.md](DEFAULT_MARGIN_SCORE_PLAN.md)** (commit `c0e75c82`). Post-D19,
    `Arms:Medium` scores on all 25500 rows (74% positive, deepest +1.43σ). _(applied)_

- **`D18`** -- 2026-09-07: **the deltoid-cap route to `Arms:Powerful` now requires some bicep**
  (applied). The `D16` branch `[cap >= 0.4576 AND vol >= 400]` was too loose. Fixture pass on the
  `D17` presets: user judged `ClaudeTest_02_Band_HiVol` w100 (cap 0.4583, bicep 0.4148, vol 411.9)
  **Thick**, while `Dino_3BA_054_09_SlimMuscles_Weight_3` w100 (cap 0.4599, bicep 0.4217, vol 423.6)
  stays **Powerful**.
  - **Why bicep and not cap.** Separation is 0.0016 on cap vs **0.0068 on bicep**. The user superimposed
    the two presets in the new Compare window: the cap/tricep difference is barely perceptible and the
    eye was actually reading the bicep. Raising the cap bar instead would have left the branch covering
    cap in [0.4591, 0.46) -- a **0.0009-wide sliver** under the main gate, i.e. a hand-fit for one row.
  - **Applied** (`apply_d18_bicep_floor.py`):
    `Arms:Powerful g3 = [Delt_Cap_Ratio >= 0.4576 AND Arm_Volume >= 380 AND Arm_Bicep_Ratio >= 0.4183]`;
    `Arms:Thick` rewritten as the exact complement -- g1 `[bicep < 0.4264, vol >= 380, cap < 0.4576]`,
    g2 `[bicep < 0.4264, vol >= 380, 0.4576 <= cap < 0.46, bicep < 0.4183]`. The volume floor drops
    400 -> 380 as a side effect, **retiring the D16 discontinuity** (same cap, same absent bicep,
    opposite labels across ~15 volume units) that neither the user nor the fixtures could see.
  - 0.4183 is the midpoint of (0.4148, 0.4217]; it also sits in the 0.0109-wide natural gap between the
    real capped/no-bulge rows (Tomboy Firmbutt / ThiccBabe 0.4070-0.4107 vs the Dino rows 0.4216-0.4253),
    though those all clear the main 0.46 gate and are unaffected either way.
  - **Result:** 3 rows change, **all fixtures, 0 real presets**; 0 rows carry two Arms values; the 7-row
    D18 judged set holds, as do D12 (5/5) and D13/D14 (11/11). Arms hash `36a8b54e85` -> `8f62026f34`.
  - **Structural note:** of the 8 real rows that are big + capped + no-bicep-bulge, seven clear the main
    0.46 gate on their own -- the cap branch's entire real-world footprint is **one row**
    (`Dino_054_09` w100). Worth remembering before spending more precision on it. _(applied)_

- **`D17`** -- 2026-08-30: **synthetic test presets are now a standing tool.** New MO2 mod
  `S:\Temp\BS Trainer\mods\Claude Test Presets` (`CalienteTools\BodySlide\SliderPresets\*.xml`, plus a
  `README.md` documenting each batch). Purpose: when analysis finds an edge case no real preset occupies,
  generate presets that land in it and let an in-app **Match Presets** scan show what the rules actually do.
  Generator: `obody_arm_tools/gen_test_presets.py`. **Enable the mod in MO2 and rescan** to use a batch.
  - **Batch 1 probes `D16`'s residual band**: bicep < 0.4264, cap in [0.4576, 0.46), 380 <= vol < 400 reads
    `Arms:Thick`, while the same cap/bicep at vol >= 400 reads `Arms:Powerful`. Empty in the corpus today;
    nearest row `Themmis Grappler 05` w100 (vol 392.8, cap 0.4564) is 0.0012 short.
  - Six presets off that base, w100 the row of interest, all bicep ~0.4147-0.4149 (no bulge branch reachable):
    `01_Band_LoVol` (Chubby 31 / MMA 77, cap 0.4585) vs `02_Band_HiVol` (41 / 80, cap 0.4583) is the matched
    pair -- same cap, volume either side of 400. `03_Band_HiVol2` (51 / 84) is insurance,
    `04_Band_MidVol` (36 / 79) sits on the boundary, `05_BelowBand` (31 / 74, cap 0.4564) and
    `06_AboveGate` (31 / 81, cap 0.4614) are the controls.
  - **Method note.** Slider *names* are kept identical to the base preset: `ImportBodySlides` routes by the ML
    slider-catalog classifier over the slider-name set alone (preset name is trace-only; `<Group>` tags are a
    gender fallback used only when the classifier returns Unknown). `Arm_Bicep_Ratio` / `Delt_Cap_Ratio` are
    solved exactly offline (pipeline reproduces the cache to 0.00000, re-verified on this base at w75/w100);
    `Arm_Volume` is a RegionVolume, not computable offline, hence the volume ladder. _(awaiting the scan)_
  - **Pending decision it feeds:** whether to drop the D16 branch's volume floor 400 -> 380, which removes the
    band entirely and was verified to move **zero** existing rows.

- **`D16`** -- 2026-08-30: **a larger arm clears a lower deltoid-cap bar** (applied).
  User's Arms review found exactly one miss in the whole set: `Dino_3BA_054_09_SlimMuscles_Weight_3` **w100**
  read `Arms:Thick`, should be `Powerful`. `Delt_Cap_Ratio` = **0.4599** against the 0.46 gate -- it missed by
  **0.0001**.
  - **Not a stray row -- a drift.** The preset's cap ratio falls monotonically as it gains mass while its
    volume climbs, so it walks across a flat gate purely by getting bigger:
    w0 0.4890/281 Medium | w25 0.4808/314 Medium | w50 0.4732/348 **Powerful** | w75 0.4663/385 **Powerful** |
    w100 0.4599/424 **Thick**. Powerful at w75 and Thick at w100 cannot both be right regardless of taste.
    Not a global artifact (`Delt_Cap_Ratio` vs `Arm_Volume` r = +0.116 over all rows, median within-preset
    change 0.0000, 32% decreasing) -- but the flat gate is what turns this family's drift into a wrong label.
  - **Applied** (`apply_d16_delt_volume_branch.py`):
    `Arms:Powerful += [Delt_Cap_Ratio >= 0.4576 AND Arm_Volume >= 400]`; `Arms:Thick` split so it stays
    mutually exclusive -- g1 `[bicep < 0.4264, 380 <= vol < 400, Delt < 0.46]`, g2
    `[bicep < 0.4264, vol >= 400, Delt < 0.4576]`. Same staircase idiom as `D12`, inverted: there a
    pronounced cap lowers the volume floor (380 -> 330), here high volume lowers the cap bar.
  - **Chosen over the simpler flat-gate drop** (0.46 -> ~0.4596), which also moves only this row but has a
    **0.0005**-wide safe window vs **0.0046** here -- the rows that bound the flat move,
    `zGranhadd_3BA#111(Rakel)` (0.4594, vol 336) and `NoElectrolytesToBrawndo` (0.4592, vol 350), are
    excluded by the volume floor. Next competitor is `Hughiele1604's (BustyXThick) ThiccBabe` w75
    (0.4553, vol 430); **0.4576** is the midpoint. A flat drop also would not stop the next drifting preset.
  - **Result: exactly 1 row of 25470 changes any descriptor in any category** -- the target, `Arms:Thick` ->
    `Arms:Powerful`. Build does not move at all (the row was already `Build:Powerful` via the `[Arms:Thick]`
    branch and stays so via `[Arms:Powerful]`); 0 rows carry two Arms values; the D12 (5/5) and D13/D14 (11/11)
    judged rows all still land as judged. The preset's own other weights are untouched.
    Arms hash `b44f21b3ba` -> **`36a8b54e85`** (still 5 rules).
  - For context, that cap sits at the **98.9th percentile** of all rows; among the 787 rows with
    `Arm_Volume >= 400` the median cap is 0.4023 and only 24 are at or above it. _(applied)_

- **`D15`** -- 2026-08-30: **rules-only ("pseudo-descriptor") categories are now a real feature.**
  User's call: `ShoulderMorph`, `ShoulderWidth` and `BicepBulge` should not appear in the distribution-facing
  descriptor list, and `Cup` should. `Cup` was simple (`D1`). The other three were not: `TemplateDescriptors`
  is **one list with two consumers** -- the distribution pickers *and* the Body Type Profile editor's Rules
  tree / DescriptorRef dropdowns -- so removing a category from it orphans its rules (the state `Cup` was in).
  The editor itself blocks the equivalent operation: `CanDeleteSelectedTreeNode` refuses while any rule still
  produces the descriptor, "the user would silently orphan those rules".
  - **Implemented instead of spliced**: new `BodyShapeDescriptorShell.IsRulesOnly` flag (default false, so
    existing settings deserialize unchanged). `VM_BodyShapeDescriptorSelectionMenu` skips flagged categories
    in both its constructor and `UpdateShellList`; the profile editor is untouched, so flagged categories keep
    their Rules-tree nodes and stay selectable in DescriptorRef conditions. The flag round-trips through
    `DumpToViewModels` / `DumpSelectedToViewModels` / `MergeInMissingModels` (an already-present category keeps
    its own flag on merge -- silently hiding a category the user already distributes on would break their
    asset packs). Checkbox on `UC_BodyShapeDescriptorShell.xaml`; `UiDocs` entry `OBody.DescriptorRulesOnly`.
  - **Applied to the settings** (`apply_rulesonly_flag.py`): the three categories are flagged; `Build`, `Arms`
    and `Cup` asserted still distribution-facing. Build green, full suite 430 passed / 1 skipped.
  - **Documented** in `README.md` (→ Body Shape Descriptor Menu → *Rules-Only ("Pseudo-Descriptor")
    Categories*), including the warning not to hide a category by deleting it. The `Rule Descriptor Reference`
    UiDocs entry now states that rules-only categories are deliberately still offered there. _(applied)_

- **`D14`** -- 2026-08-30: **`Build` is a pure DescriptorRef aggregator -- keep it that way.**
  **Standing convention (user's instruction):** every condition in a `Build` rule must be a
  `DescriptorRef` into another category, never a measurement comparison. If a category `Build` needs
  does not exist yet, add the **leaf category** and have `Build` reference it. Only depart from this if
  a desired manual annotation genuinely **cannot** be expressed in leaf form -- and in that case
  **warn the user and get agreement before applying the change**, rather than reaching for a numeric
  condition on `Build`.
  - **Why it matters concretely:** `obody_scan.py` builds its "change this -> re-check that" table by
    walking `DescriptorRef` edges. A measurement condition on `Build` is invisible to that map, so
    retuning the measurement later would silently skip Build's re-review. Leaf form also keeps one
    threshold in one place and makes the value addressable by asset packs / distribution rules.
  - **Conversion applied 2026-08-30** (`apply_d14_leafform.py`), retiring the numeric conditions `D13`
    had introduced:
    - new leaf **`BicepBulge`** [Normal | **Pronounced**] <- `Arm_Bicep_Ratio >= 0.442` (default Normal),
      with a `TemplateDescriptors` shell. It is a *second* threshold on the same measurement `Arms`
      already gates at 0.4264 -- Arms answers "is a bulge visible at all", `BicepBulge` answers "is it
      pronounced" -- the same two-thresholds-on-one-measurement pattern `D12` used for `Delt_Cap_Ratio`.
    - **`ShoulderWidth:Wide` reused unchanged at 31.5.** The concern logged when `D13` went in (that the
      descriptor's cut and the Build gate would fight, 31.5 vs 31.2) **did not materialise**: 31.5 sits
      inside the feasible window the judged rows allow, (30.66, 31.90], and reproduces all 11 verdicts.
      No re-cut, and no need for a second value to separate descriptor from gate.
    - `Build:Powerful(F)` branches 3/4 now read `[ShoulderWidth:Wide]` and `[BicepBulge:Pronounced]`.
  - **Result:** 11/11 judged rows; the only behavioural delta is the 31.2 -> 31.5 shoulder cut, which
    demotes **10 rows** (all inside the 31.25-31.47 sliver, all landing on `Build:Athletic`, zero
    promotions). Powerful 1126 -> **1116**. Rules 68 -> 69; Build hash `47e750caae` -> **`3c920599f1`**,
    new `BicepBulge` cell (1 rule, `4d741b847e`). The scan's dependency map now shows
    `Build -> ... BicepBulge(1), ShoulderMorph(1), ShoulderWidth(1) ...`.
  - The apply script asserts the invariant directly (every condition in every `Build` rule is
    `Kind == "DescriptorRef"`) -- reuse that check in any future Build edit. _(applied)_

- **`D13`** -- 2026-08-30: **shoulder-aware `Build:Powerful`** (proposed, judged and **applied** the same day).
  Prompted by five presets the user re-called from Powerful to Athletic. All five fired the open
  `[Arms:Athletic] AND [Thighs:Thick]` branch -- **619 of 1428 Powerful rows (43%)** -- which promoted any arm
  that cleared the binary Arms bulge gate, however narrowly (`The Tinraa Body edits A-C Screens` = CustomPreset78
  = CustomPreset82, one body under three names, cleared 0.4264 by **0.0002**).
  - **Judging set (the standing workflow).** 8 presets picked as a grid over (`shoulder_width` x
    `Arm_Bicep_Ratio`), all currently Powerful via that branch, at w75 (`analyze33/34_shoulder_*.py`). Verdicts:
    Powerful = `Ku Box Camel`(27.33/0.4440), `Black Widow`(29.86/0.4499), `Chubby Bunny`(31.32/0.4501),
    `Dino_3BA_032_Muscles`(31.40/0.4449), `Athena Shell`(31.90/0.4281); Athletic = `Beautiful Angel`(30.04/0.4362),
    `Lydia`(29.84/0.4302), `Dibella's Hips`(30.66/0.4398). Anchors from 2026-08-29: Powerful
    `TheSuperMuscularInstagramBody`(29.94/0.4765); Athletic `OMG GRAVATY`(30.57/0.4317), `Tinraa`(28.37/0.4266).
  - **A flat bicep bar is impossible**: min(Powerful) 0.4281 < max(Athletic) 0.4398. The user's own note on
    `Athena Shell` ("shoulders are broader than #2") is the inversion in words. Direction confirmed: **broader
    shoulders lower the bicep bar**, matching the `D12` precedent where the delt cap lowers the volume floor.
  - **The engine cannot express a sloped boundary** (conditions are `measurement <cmp> constant` or
    DescriptorRef, DNF -- no arithmetic across measurements; a ratio measurement only encodes the *opposite*
    direction, since the trade-off is a sum). So it is a two-step staircase, as `D12`'s Arms branch is.
  - **Axis choice.** `shoulder_width` fits with the widest margin; `armpit_width` fits with a 0.59-unit window,
    `Delt_Cap_Ratio` with a 0.0016 window (too thin), and **`shoulder_to_hip` has no solution at all** -- the
    read is absolute shoulder width, not a shoulder-to-hip proportion. `shoulder_width` being partly
    deltoid-driven (`MuscleMoreArms_v2` +3.08 / 100) is a feature here, not the confound it is for frame work.
  - **Applied 2026-08-30** (`apply_d13_rules.py`, dry-run then write; backup taken):
    `Build:Powerful(F)` branch 3 -> `[Arms:Athletic] AND [Thighs:Thick] AND shoulder_width >= 31.2` **OR**
    `[Arms:Athletic] AND [Thighs:Thick] AND Arm_Bicep_Ratio >= 0.442`, both with the unchanged belly/chubby
    guards. `Build:Athletic(F)` lost `![Thighs:Thick]` (verified side-effect-free: of the 631 rows with
    `Arms:Athletic` + `Thighs:Thick`, 619 are the branch and the other 12 fail the belly guards that
    `Build:Athletic` shares). Rules 8 -> 8; Build hash `0349b4d0c5` -> **`87a1b3ac79`**; Arms untouched.
  - **Result**: 11/11 judged rows; rows carrying `Build:Powerful` **1428 -> 1075** (-25%; 164 kept by the
    shoulder branch, 167 by the bicep branch, 65 by both), all **353 demotions land on `Build:Athletic`**,
    zero rows promoted, zero rows carrying both values.
  - **Chosen from the feasible windows**: shoulder cut anywhere in (30.66, 31.90] -> **31.2**; bicep bar in
    (0.4398, 0.4440] -> **0.442**. The wide branch carries **no** bicep condition because nothing constrains
    its floor (no Athletic verdict above 31.2) and `Arms:Athletic` already requires >= 0.4264.
  - **Soft spot**: the 0.0042-wide bicep window rests on two bodies (`Dibella's Hips` below, `Ku Box Camel`
    above). Four presets sit in or beside the gap at sub-31.2 shoulders if it wants firming up:
    `Pitaya Bod - Nude`(29.29/0.4457), `MAT - Thicc Women`(27.81/0.4414), `DamnMrsClausUTHICC`(29.24/0.4389),
    `Husados's (BustyXXThick) Curvaceous II`(30.44/0.4384).
  - **Open**: placement is numeric-on-Build, so `Build` is **no longer a pure DescriptorRef aggregator**. The
    leaf-category refactor (a rules-only `BicepBulge` plus a re-cut `ShoulderWidth`) stays available and would
    restore that property -- note `D12`'s staged `ShoulderWidth` cuts (28.0 / 31.5) do not match this data,
    which wants a boundary at 31.2. Also still missing: a Male `Build:Athletic`, and `Build:Powerful(M)` still
    reads `[Arms:Athletic]` / `[Thighs:Thick]` unconditionally (cache is female-only). _(applied; user review pending)_

- **`D11`** -- 2026-08-24: **per-NPC body types are defeated by a shared `BODYTRI` string.** Full write-up:
  **[MULTIBODY_MORPH_PLAN.md](MULTIBODY_MORPH_PLAN.md)**. RaceMenu resolves a mesh's morph data from a
  `BODYTRI` NiStringExtraData baked *inside the NIF* (`BodyMorphInterface.cpp`, `MorphCache::ApplyMorphs`
  -> `CreateTRIPath` = `"meshes\" + string`), matches morphs to geometry **by shape name**, and guards with
  `m_maxIndex < vertexCount`. But **16 installed mods bake the identical string**
  `actors\character\character assets\femalebody.tri`, and **35 body NIFs carry no `BODYTRI` at all** (those
  NPCs receive no OBody morphs whatsoever). So today two NPCs with different body meshes both morph against
  one global `.tri` -- whichever mod won the VFS conflict. Proposed feature has two modes: **(A)** pre-flight
  RaceMenu's own two gates at startup and report which mods conflict; **(B)** "stamping mode" -- copy the
  body's `_0`/`_1` NIFs per *body type* (not per NPC), rewrite their `BODYTRI` to a body-specific `.tri`, and
  repoint the WornArmor ARMA model. Biggest unknown: whether niflysharp can round-trip
  `NiStringExtraData` edits. Relevant to the annotation work only indirectly -- it does not affect
  measurements, which are taken against the profile's own reference body. _(open)_

- **`D25`** -- 2026-09-18: **lower-belly depth (`BellyFlab_F`) rebuilt Chubby/Pregnant; then a random
  spot check exposed overfitting.** _(applied; NOT validated -- next action)_
  - **What went in.** The user authored a new key vertex `BellyFlab_F` (BoundingBox, MinZ, Y 70.6-78.1,
    X +/-12.7) = the frontmost point of the **lower** belly. Three `SignedAxisDistance`(Z) measurements
    were added and the cache rescanned: `Spine_to_BellyFlab` (vs `SpineAtNavelHeight`),
    `Arch_to_BellyFlab` (vs `BackArch_Frontmost`), `Sacrum_to_BellyFlab` (vs `SacrumBack`). All are
    positive = lower belly protrudes. Rules written:
    `Belly:Chubby = Spine_to_BellyFlab >= 16.20 AND sternum_to_belly >= 0.68 AND ![Pregnant] ![Fat] ![Muscular] ![Thin]`;
    `Belly:Pregnant` g0 re-based on `BellyVolume >= 890 AND belly_projection >= 14.70 AND
    belly_projection_2 >= 6.55` + frame gates + the `D22` terms (kept -- they change 0 rows but are
    retained so a later retune cannot silently re-admit the 11 rows D22 removed), g1 verbatim, and a
    new g2 `belly_projection_2 >= 8.5 AND BellyVolume >= 1100 AND waist_width <= 19.5` (a high-volume
    escape for arched-back pregnancies, e.g. `[Anuketh] Moon Bride Pregnant` @0/@25 whose lordosis
    suppresses every depth measure). Populations
    Thin 11520 / Normal 12987 / Chubby **801** / Pregnant **168** / Fat 33; 0 `Thin`+`Chubby` rows.
  - **Why it looked good.** 74/84 judged (6 hard). On the 70-verdict set it was 66/70 with 1 hard error,
    and leave-one-out *rose* from 34/39 to 43/47 as verdicts were added -- the opposite of the earlier
    cache-only rules, whose ceiling collapsed from 0 to 5 errors when 6 rows arrived.
  - **Why it is wrong anyway.** A **random** sample of 14 rows drawn from the 541 the rules changed
    scored **8/14**. Three independent faults: (a) **butt contamination** -- `SpineAtNavelHeight` is a
    back-*surface* point, so buttock mass pushes it rearward; corpus-wide
    `(Spine_to_BellyFlab - Arch_to_BellyFlab)` vs `butt_projection` is Spearman **+0.684**, and
    `Spine_to_BellyFlab` itself **+0.487** vs `Arch_to_BellyFlab`'s **+0.176** (the user spotted this by
    eye on `[DevonixS] - Sanguine's Ultimatum` @75: "unrealistically large butt projection");
    (b) the `sternum_to_belly >= 0.68` companion was never doing the work its [0.42..0.93] window
    implied -- it was incidentally screening butt-contaminated rows, and it wrongly rejects genuine
    chubby bodies (`3BA Willendorf` @75 has `Spine_to_BellyFlab` 19.36 but `sternum_to_belly` 0.45);
    (c) Pregnant over-reaches into Chubby (`Nami Extra Curvy` @100, `CustomPresetChubby2` @25).
  - **Leading replacement, not yet applied or spot-checked:** re-fitting on all 57 hard Chubby/Normal
    rows gives `Spine_to_BellyFlab >= 16.20 AND butt_projection <= 7.41` at **3 errors**
    (2 missed / 1 false) -- i.e. the search independently chooses the de-contamination term the user
    named. Runner-up `Spine_to_BellyFlab >= 15.93 AND BellyVolume >= 622.13` at 4 (0 missed / 4 false).
    Best single axis is `Spine_to_BellyFlab >= 16.20` at 8 errors, so the conjunction is essential.
  - **Rejected on evidence:** `Sacrum_to_BellyFlab` (57/70 vs 66/70; all 8 rows in a targeted judging
    round came back Normal). Also rejected earlier in the same investigation: a `BellyFrontDownFat_v2`
    slider branch (the slider is neither necessary nor sufficient -- `CP Anime` and `Estella` both sit
    at 100 with opposite verdicts), and a `ProfileConcavity` "ledge detector" MeasurementKind (the
    offline profile-scan crease hit AUC 0.934 on Chubby-vs-Pregnant, but `Spine_to_BellyFlab` reaches
    **0.987** with no engine change, so the new Kind is unnecessary).
  - **Method note that generalizes:** every judging round before this one sampled the decision
    boundary, which is why in-sample accuracy kept flattering the fit. The random draw is what exposed
    the real error rate. Alternate boundary-sampling (to pin a cut) with random sampling (to measure).

- **`D26`** -- 2026-09-18: **rename `Belly:Flat` -> `Belly:Thin` is done; the redefinition is not.**
  _(rename applied; redefinition open)_ The user's rationale: many rows called `Normal` have a flat
  stomach but a thick trunk, while `Flat` is meant to capture a *narrow trunk*. Evidence supports it --
  using `waist_width * Back_to_Belly` as a trunk cross-section proxy, current Thin has median 176 vs
  Normal 209 and they separate well (a random Thin row is thicker than a random Normal only 13% of the
  time), but **17% of current Thin rows have a thick trunk** and **22% of Normal rows (2940) have a thin
  one**. `belly_projection` already correlates +0.78 with trunk cross-section, which is why the current
  rule half-works. Redefining Thin on trunk size would move ~3000 rows and change what `Normal`
  contains, so the Chubby boundary must be re-judged after it, not before. Rename mechanics for the
  record: `Flat` is also a value in **Cup, Butt, Waist and BustSize**, so any rename must be
  Belly-scoped (a global find/replace breaks four categories); 0 annotations were stored on presets, so
  nothing needed migrating.

- **`D27`** -- 2026-09-18: **Label-then-Suggest cannot synthesize Belly-shaped rules.** _(assessment)_
  Read of the subsystem: the annotation table (one row per preset x weight, from the scan or the shared
  measurement cache), the annotation editor (multi-select descriptor menu -> persisted
  `BodyTypeProfile.PresetAnnotations`), **Suggest Measurements** (Phase 5: ANOVA F / Cohen's d /
  information gain) and **Suggest Rules** (Phase 6: Youden's J / median split / decision stump).
  `PresetAnnotations` is read *only* by the editor and the two Suggest panels -- never by
  `BodySlideMeasurementEvaluator` or `BodySlideAnnotator` -- so back-filling it is safe and cannot
  affect classification (verified: populations unchanged after writing 84 annotations).
  **The blocker:** `VM_SuggestRulesPanel` wraps each threshold in a *single-condition*
  `AndGatedMeasurementGroup` and OR-combines them, so it can only emit `A >= x OR B >= y`. All three
  synthesis algorithms are univariate (DecisionStump is depth-1). Belly needs AND conjunctions (Chubby
  is a 2-term AND; Pregnant g0 is a 7-term AND) plus `DescriptorRef` guards, none of which it produces.
  **Verdict: use the tab for labeling + storage and for Suggest *Measurements* (useful -- it is the
  in-app equivalent of the AUC ranking done by hand); do not Accept its rule suggestions for Belly.**
  Missing for throughput: there is no preset queue, no sampling policy, and no auto-advance -- rows are
  picked by hand in a DataGrid. Handoff drafted for that feature
  (`obody_arm_tools/HANDOFF_annotation_queue.md`).

- **`D28`** -- 2026-09-18: **the annotation queue is built; the sampling policy is enforced in the**
  **UI, not by discipline.** _(implemented; not yet exercised on a real labelling run)_
  The handoff in `obody_arm_tools/HANDOFF_annotation_queue.md` is implemented on the `UIupdate`
  branch. What it means for this tracker:
  - **Sampling is now a setting with a number attached.** Every queue mixes an exact
    `round(n x RandomFraction)` uniformly-random draws into the `Spread` / `Uncertainty` ordering
    (default 0.25), each served slice is flagged as a random draw or not, and the session counters
    report how many *labelled* slices were random. Zeroing the fraction raises a banner and a
    confirmation dialog. This is `D25`'s method note turned into a mechanism: the 8/14 result came
    from the fact that nothing in the tool distinguished a boundary sample from a measurement, so
    the distinction is now structural rather than something to remember.
  - **Verdict exports carry their provenance.** `Copy verdicts` / `Export verdicts` write the
    category's labels plus `policy`, `seed` and `randomFraction`. A future `belly_verdicts.json`
    can therefore state how it was drawn instead of leaving it to be reconstructed from this log.
  - **Alias families are collapsed.** Slices whose profile measurements match to three decimals are
    served once and the verdict propagates to every member, with the siblings recorded on each
    annotation. Offline fits should collapse a family to one observation -- the export already does.
  - **Rules-only categories are labellable.** `ShoulderWidth`, `ShoulderMorph` and `BicepBulge` were
    invisible in the annotation editor (the descriptor menu hides `IsRulesOnly` categories, which is
    right for distribution pickers and wrong for recording a judgment). They now appear there, so
    their rules can be fitted on verdicts rather than by hand.
  - **Unchanged on purpose:** no classification behavior moves. `PresetAnnotations` is still read
    only by the editor and the two Suggest panels, nothing writes to
    `BodySlideSetting.BodyShapeDescriptorsByWeight`, and no queue operation triggers a rescan.
  - **Still open:** `D27`'s blocker stands -- `Suggest Rules` remains univariate and single-condition,
    so it still cannot synthesize Belly-shaped rules. The queue raises labelling throughput and
    fixes the sampling problem; it does not fix rule synthesis.
  - **`List` sampling mode + keyboard (added same day, on request).** A fourth mode serves exactly
    the slices named in a hand-authored worklist, in the order written, ignoring every other
    sampling option (random fraction, weight grouping, de-duplication, skip-already-annotated) and
    saying so in the status line -- a worklist is already a deliberate sample. Lists load from a
    file or straight from the clipboard, as plain text (`Preset | weight | note`, tabs, or a bare
    trailing number; `#` / `//` comments) or as JSON (bare array or a `rows` array,
    case-insensitive fields, unknown fields ignored -- so a **verdict export feeds straight back in
    for re-judging**). Each case carries an optional **note shown while that body is on screen**.
    Keyboard: **Space** commits and advances (Enter also), **Tab** skips (S also), Backspace / Left
    go back.
  - **This is the tool for `D29` step 2.** The proposed Thin / Flat / Average rules in
    `apply_d29_belly_rules.py` are awaiting a spot check; a worklist of the rows they move -- with a
    per-case note saying what each one is supposed to demonstrate -- can be loaded and worked
    through in one pass, and the export comes back carrying its own provenance.
  - **Observed state, consistent with `D29`:** the queue's per-value tally reads
    `Chubby: 23  Pregnant: 16` for Belly, i.e. exactly the 39 verdicts `D29` left after retiring the
    62 Thin / Normal ones. Worth knowing while the re-scope is in flight: with only two values
    annotated, in-app **`Suggest Measurements` on Belly fits Chubby-vs-Pregnant**, not the
    Chubby-vs-Average contrast under revision. Not a defect -- a consequence of the deliberate
    mid-revision state -- but it means the panel's output should not be read as advice about the
    Chubby boundary until the low end is re-judged.
  - **Next action is unchanged:** `D25`'s leading replacement for Belly:Chubby
    (`Spine_to_BellyFlab >= 16.20 AND butt_projection <= 7.41`) is still unapplied and unchecked.
    The queue is the tool to check it with -- a `Random` or high-`RandomFraction` pass over Belly is
    now a repeatable, quotable operation.

- **`D29`** -- 2026-09-18: **the low end of Belly is re-divided four ways: Thin / Flat / Average.**
  _(step 1 applied; step 2 proposed, awaiting the user's spot check -- supersedes `D26`)_
  - **The distinction the user wants.** `Thin` = a *very thin profile* (roughly the bottom decile of
    trunk depth). `Flat` = a belly that **does not protrude past the sternum**, on a body whose
    proportions are *not* very thin. `Average` = ordinary, allowing a very slight paunch. `Chubby` =
    a more pronounced paunch. So the low end is a 2x2 on two independent axes -- **protrusion**
    (Flat vs Average) and **trunk size** (Thin vs Flat) -- not one ordered scale, which is why one
    measurement was never going to separate it.
  - **Step 1 (applied).** `Belly:Flat` added to `TemplateDescriptors` (order: Thin, Flat, Average,
    Chubby, Fat, Pregnant, Muscular); `Belly:Normal` -> `Belly:Average` at every site (both profiles'
    rule descriptors, the `Realism:UnrealisticChest` DescriptorRef, `DefaultDescriptorValuesByCategory`,
    and the `BodySlideClassificationRules["CBBE 3BA"]` Belly default); the 62 `Thin`/`Normal`
    `PresetAnnotations` deleted. Verified inert -- populations byte-identical, no cyclic rules.
    Belly `8ce915f100` -> `81ef53de5f`, Realism `6c1c09c19d` -> `d0fbbb2e91`.
  - **Why the annotations had to go.** Every old `Normal` verdict is ambiguous under the new scheme
    (it could be Flat or Average) and every old `Thin` verdict likewise (Thin or Flat). They were
    recorded against a binary the new scheme abolishes, so keeping them would train the fit on a
    question the user was never asked. `belly_verdicts.json` keeps all 84 rows as the historical record.
  - **Proposed rules (step 2, `obody_arm_tools/apply_d29_belly_rules.py`).** Nothing about Chubby,
    Fat or Pregnant changes, and no measurement is added, so **no rescan**:
    - `Belly:Thin  = sternum_to_belly <= 0.05 AND Back_to_Belly < 11.38 AND waist_width < 15.09 AND ![Fat] ![Pregnant] ![Muscular]`
    - `Belly:Flat  = sternum_to_belly <= 0.05 AND ![Thin] ![Chubby] ![Fat] ![Pregnant] ![Muscular]`
    - `Belly:Average` = the category default (its rule stays disabled, as `Normal`'s was).
    Simulated: **Thin 2761 (10.8%) / Flat 13519 (53.0%) / Average 8225 (32.3%) / Chubby 803 (3.1%) /
    Pregnant 168 / Fat 33**, no double-assignment below Chubby.
  - **Why `sternum_to_belly` for the protrusion axis.** It *is* the user's criterion, literally
    (`SignedAxisDistance(Z)` from `LowerSternumFront` to `BellyFront`; positive = belly forward of the
    sternum), and its zero is anchored on reference bodies: the whole `CBBE Slim` ramp sits at
    -0.06..-0.07 and `CBBE Curvy` at -0.06..+0.03. The cut is **0.05, not 0.00**, so the entire
    `CBBE Curvy` ramp stays Flat; at 0.00 its w75/w100 flip to Average on a 0.00/0.03 reading.
  - **Why NOT `belly_projection` for the thinness axis** (the user's "spine-to-navel distance").
    `SpineAtNavelHeight` is a back-*surface* point, so buttock mass inflates it -- the same
    contamination `D25` found on `Spine_to_BellyFlab`. Corpus-wide,
    `Spearman(belly_projection - Back_to_Belly, butt_projection) = +0.549`. Worked example:
    `TopHeavy` @100 reads `belly_projection` **16.62** (above p90 = "thick trunk") while
    `Back_to_Belly` reads **10.47** (bottom decile) -- its `butt_projection` is 12.49, past p99.
    `[DevonixS] - Desecration of the Nine` @100 is the same story at a 14.06 waist. Conversely
    `Archer2` is called thin by `belly_projection` (10.55) purely because its back is flat.
    `Back_to_Belly` (`BellyFront` vs `BackArch_Frontmost`) carries +0.200 butt correlation vs
    `belly_projection`'s +0.461, and +0.730 with `waist_width` -- it tracks trunk size, which is the
    thing being measured.
  - **`waist_width` is the second Thin term on purpose.** `D26` proposed `waist_width * Back_to_Belly`
    as a trunk cross-section; the rule engine has no product, and the axis-aligned box that best
    approximates its bottom decile tops out at ~0.77 recall / 0.83 precision. Requiring *both* narrow
    and shallow is arguably the better definition of "thin profile" anyway, and the width term is what
    stops a lordotic back (which shortens `Back_to_Belly`) from reading as a thin body on its own.
  - **Known artifact to watch during the spot check: the bust shifts `sternum_to_belly`.**
    `LowerSternumFront` is the rear-most midline vertex at Y 86.8-89.0 -- the floor of the sternal
    valley -- so breasts that reach that low push it backward and inflate the reading.
    Over the 264 presets whose belly is provably static across the weight ramp (|delta| < 0.15) while the
    bust grows by >100 volume units, `|drift|` in `sternum_to_belly` is median 0.006 but **p90 0.161,
    max 0.570**; `pubis_to_belly` drifts p90 0.053 and `belly_projection_2` p90 0.181. The worked case
    is **`Archer2`**: belly identical at every weight (`Back_to_Belly` 12.19 -> 12.21,
    `pubis_to_belly` 1.08 -> 1.05) but `sternum_to_belly` 0.69 -> **1.26**, so all five weights land in
    Average despite a flat stomach. **If the spot check shows flat-bellied, modest-bust presets in
    Average, that is this.**
  - **The obvious fix for it does not work as-is.** A second OR group `pubis_to_belly <= 1.75` rescues
    Archer2 but also moves **1187** slices, including `CBBE Gen` @100 (`sternum_to_belly` 2.02,
    `Back_to_Belly` 14.72) -- bodies with a prominent pubic mound read low on `pubis_to_belly` even
    with a real belly. Adding `AND Back_to_Belly < 12.07` cuts it to 738 and excludes `CBBE Gen` @50+.
    Not proposed for v1; revisit after the judging round rather than guessing now.
  - **Knock-on: `Realism:UnrealisticChest`** -- see the Realism notes. Introducing `Flat` silently
    costs it 94 -> 67 rows unless the fifth group is added. Handled by the same apply script.
  - **Chubby moves by +2 rows** (801 -> **803**) with no Chubby edit at all: it carries `![Thin]`, so
    shrinking Thin from 11520 to 2761 un-suppresses `1 Ultimate` @100 and `Golden Rounded` @0, both of
    which have `Spine_to_BellyFlab` >= 18. No new-Thin row is Chubby-eligible at any candidate
    threshold, so Chubby's `![Thin]` guard stays inert and the rule is left exactly as `D25` wrote it
    -- `D25`'s open Chubby item (`butt_projection <= 7.41`) is unaffected and still next.
  - **There are no verdicts for the Flat/Average boundary** -- nobody has been asked that question yet,
    so the thresholds are anchored on reference bodies, not fitted. Judge a `Random` draw in the
    annotation queue (`D28`) before treating any of them as settled.
  - **Judging round staged.** `obody_arm_tools/make_d29_worklist.py` builds
    `belly_d29_worklist.txt` (45 cases, seed 4127) for the queue's `List` policy, plus
    `belly_d29_worklist_key.json` (proposed labels + measurements -- the scoring key, not to be
    read before block 1). Four blocks in serve order: **1** 21 blind draws sampled uniformly
    *within* each proposed label (7/7/7, so the 11% `Thin` class is auditable; weight by
    `block1_population_weights` for the corpus rate), **2** a 10-rung `sternum_to_belly` ladder with
    trunk size held near the pool median, **3** a 6-rung `Back_to_Belly` ladder at fixed flat
    protrusion plus a narrow/wide pair testing whether `waist_width` belongs in Thin at all, **4**
    the `Archer2` / `CBBE Gen` / `TopHeavy` artifact pairs. Block 1 is first on purpose -- its notes
    are deliberately contentless, because the later blocks' notes calibrate the reviewer's eye and
    would destroy the measurement. Regenerate with a different `--seed` for an independent round.
  - **ROUND 1 IS IN (2026-09-19) -- see `D30`.** All 45 cases judged. The proposal above scored
    **29/45**; the refit scores **36/45**. `Thin` moved to `Arch_to_BellyFlab < 10.20`, the
    Flat/Average cut moved 0.05 -> 0.65 and is provisional. `apply_d29_belly_rules.py` now carries
    the refitted defaults (`--tt` replaced `--td`/`--tw`).

- **`D30`** -- 2026-09-19: **round 1 of Belly judging: `Thin` is settled, `Flat`/`Average` is not,**
  **and two defects surfaced that are not about Belly rules at all.** _(verdicts in
  `obody_arm_tools/belly_verdicts_d29.json`; round 2 staged, unapplied)_
  - **Scoring.** 45 cases judged. The `D29` proposal scored **29/45**; scored faithfully (the app
    seeds `Belly:Muscular` from the slider layer, which the offline engine does not, so seeded rows
    must be scored as `Muscular`) that is **12 measurement-rule errors + 4 slider-layer errors**.
    The refit below scores **36/45 (5 rule errors + the same 4 slider errors)**.
  - **Blind block, per-label precision** (block 1 sampled uniformly *within* each proposed label):
    Thin 5/7, Flat 5/6, **Average 1/4**, Muscular 2/4. Weighting by population, the user's verdicts
    imply corpus shares of roughly **Flat 70% / Thin 16% / Muscular 10% / Average 5%** -- against the
    proposal's 11/53/32. `Average` was the whole error budget.
  - **`Thin` refitted: `Arch_to_BellyFlab < 10.20`.** The `D29` pair is retired. Ranked by rank
    inversions against the 30 usable verdicts: `BellyVolume` 0, `Spine_to_BellyFlab` 1,
    `Arch_to_BellyFlab` 2, `belly_projection_2` 3, `belly_projection` 8, **`Back_to_Belly` 14** --
    the axis `D29` chose was the worst belly measure tried, which is what the block-3 ladder was
    reporting (`CustomPreset68` 10.85 and `Character based pressets- Untroubled` 10.90 both came back
    Flat). `Arch_to_BellyFlab` wins on the combination: 1 error at the cut, **9.8% of the corpus**
    (the user's "bottom 10%"), per-weight spread 3.3pp, and it is the butt-clean member of the
    BellyFlab family (`D25`: +0.177 vs `butt_projection`, against `Spine_to_BellyFlab`'s +0.488).
    `TopHeavy` is the case that shows why the contamination matters *and* why it is not always
    harmful: `Spine_to_BellyFlab` reads 15.73 where `Arch_to_BellyFlab` reads 10.38, a 5.3-unit
    butt-driven gap -- and the user called it **Average**, so both axes keep it out of Thin.
  - **`Flat` vs `Average` cannot be fitted yet, and the round says so clearly.** Only **6 Average
    slices from 4 distinct presets** (`Archer2` and `TopHeavy` each contributed two weights of one
    body). No belly measurement separates the classes on those rows -- `sternum_to_belly` has Flat
    spanning -0.37..+0.59 and Average -0.11..+1.26, and the best single cut over the whole axis is
    +0.65 at 25/29, catching only 2 of 6 Average. The apparent top discriminators were
    `Delt_Cap_Ratio`, `armpit_width`, `shoulder_width` and `chest_proj_to_chest_width` -- shoulder
    and chest measures, i.e. what overfitting to four bodies looks like. **The 0.65 cut is applied as
    the least-wrong interim, flagged provisional; do not treat it as fitted.**
  - **DEFECT -- `BellyVolume` is inflated ~21% at weight 25 only.** Testing each interior weight
    against the midpoint of its neighbours over 5100 presets: w25 **+21.4%**, w50 -9.5% (dragged down
    by w25 as its neighbour), w75 -0.0%. Every other belly measurement is exactly linear in weight
    (median deviation 0.00%), as it must be when sliders interpolate linearly -- so this is specific
    to the `RegionVolume` kind over `BellyBroad`, not a property of volume. Consequence: a
    `BellyVolume < 353` Thin rule would fire on 9.9% of w25 rows against ~20% at every other weight.
    **`BellyVolume` had the single best separation on the verdicts (0 inversions) and was rejected
    only because of this.** Worth fixing on its own account -- `BellyVolume` is also read by
    `Belly:Pregnant` g0 (>= 890) and g2 (>= 1100). Not investigated further here.
  - **DEFECT -- the slider-layer Muscular seed (`N1`, `MuscleAbs >= 70`) is wrong ~9% of the time.**
    4 of 45: `ousnius - Random Preset 0808` w100, `Dino_3BA_035_Weight` w100 and `Nyr Fantasy 3BA
    Body` w0 all seed Muscular at `MuscleAbs` 100 but were judged **Flat**; `LamaKreis' (Busty) TNB
    Contra V2` w50 was judged **Muscular** at `MuscleAbs` **0**. This is not a rounding matter --
    `Muscular` suppresses `Thin` and `Flat` outright via their `![Muscular]` guards and covers
    **17.4%** of the corpus, against the ~10% the verdicts imply. Round 2 carries a `MuscleAbs`
    ladder to place the cut.
  - **Knock-on already visible:** with Muscular seeded the way the app runs it, `Chubby` is **635**
    rows, not the 803 quoted in `D29` -- that figure was computed without the seed, because the
    offline engine does not apply it. Any population quoted from `obody_arm_tools/engine.py` without
    `external=[("Belly","Muscular")]` is overstated for every category Muscular guards.
  - **Round 2 staged:** `obody_arm_tools/make_d30_worklist.py` -> `belly_d30_worklist.txt`
    (41 cases, seed 8311) + key. Block 1 = 15 blind draws over the revised Flat/Average pools,
    over-sampling Average 10:5 because a proportional draw would never sample a 3% class. Block 2 =
    an 18-row 3x3 grid over `sternum_to_belly` x `Spine_to_BellyFlab`, deliberately assuming no axis
    -- if the cells come back mixed, that is the answer. Block 3 = an 8-row `MuscleAbs` ladder
    (45 / 65 / 72.5 / 75 / 81 / 100 / 100 / 120) for `N1`.
  - **Superseded by `D31`** on the same day: `Muscular` stops being exclusive, the Flat/Average/Chubby
    split is redefined, and round 2 is rebuilt around both plus a repeat-reliability block.

- **`D31`** -- 2026-09-19: **Belly becomes two independent axes, and the Flat/Average/Chubby split**
  **is redefined.** _(structure applied to the apply script; rules still unapplied; round 2 staged)_
  - **Muscular is no longer mutually exclusive with anything.** The user's reasoning: any 3D
    morphology can carry `MuscleAbs > 70` and show abs, so abs are not a shape. Every row now gets
    **one morphology value** (Thin / Flat / Average / Chubby / Fat / Pregnant) from
    Label-by-Measurements **and, independently, `Muscular`** from Label-by-Sliders. This restores a
    clean separation: measurements own morphology, sliders own abs. **It reverses `D23`**, which
    settled that `Muscular + Flat` was disallowed -- that ruling is void.
  - **The mechanical catch, and why `Average` had to change.** Dropping the `![Muscular]` guards is
    not sufficient. `RunClassifierRules` treats a Category seeded from the slider layer as
    **covered**, so its configured default is neither materialized for aggregators nor emitted
    (documented in that method: *"a slider-labeled Belly:Muscular preset must not ALSO fall to the
    measurement side's Belly:Normal"*). Measured: guards intact -> **4425 rows (17.4%) carry no
    morphology value at all**; guards dropped but `Average` still the category default -> **412
    rows (1.6%)** still carry none; guards dropped **and `Average` promoted to a positive
    all-negative rule** (`![Thin] ![Flat] ![Chubby] ![Fat] ![Pregnant]`) -> **0**. So the promotion
    is required, not cosmetic. The category default is left in place as an inert safety net.
  - **Populations after the restructure:** Thin 2511 / Flat 20708 / Average 1286 / Chubby **803**
    (back up from 635 -- the `![Muscular]` guard was suppressing those) / Fat 33 / Pregnant 168,
    with `Muscular` on 4448 rows alongside whatever morphology they carry.
  - **Knock-on to re-check: `Realism:UnrealisticChest` g3.** `D24` added
    `chest_projection > 16.0 AND [Belly:Muscular]` precisely because a seeded Muscular row never
    materialized a morphology value. That premise is now false -- such a row also carries Flat /
    Average / Thin, so g1/g2/g4 fire on their own. Leaving g3 in means **every** Muscular row uses
    the lowest threshold regardless of morphology. Not changed yet; decide when Belly lands.
  - **Flat / Average / Chubby redefined (user, 2026-09-19).** `Flat` = **no flab at all**;
    `Average` = **a thicker trunk OR a small amount of flab**; `Chubby` = a **pronounced** paunch.
    The earlier paradigm sent any visible flab straight to Chubby, so **the 23 banked Chubby
    verdicts are partly stale** -- the same way the Thin/Normal ones were at `D29`. They are NOT
    deleted this time: block 2 of round 2 re-asks the low end instead, because the rows are still
    informative and re-judging is cheaper than discarding. This also explains why `Average` came
    back at only ~5% in round 1: it was being squeezed from above by an over-broad Chubby.
  - **Label noise is now measured, not assumed (user's note 3).** The user flagged that borderline
    verdicts are not perfectly repeatable and suggested re-serving judged rows unannounced. Block 1
    of round 2 does exactly that: **9 unmarked repeats** stratified over the round-1 verdicts
    (2 Flat / 2 Thin / 2 Average / 3 Chubby, each taken nearest a rule boundary) plus one
    far-from-boundary control, shuffled into 10 fresh draws with identical notes.
    **The disagreement rate this produces is the accuracy ceiling for every Belly rule** -- no
    threshold should be tuned past it, and a fit that beats it is fitting noise. The control row
    distinguishes ordinary borderline flutter from something worse. The block header does say that
    repeats are present (the user proposed the design, so concealment was never available); it does
    not say which.
  - **Round 2 = `belly_d30_worklist.txt`, 55 cases, seed 8311.** Block 1 measure+repeat (19),
    block 2 Chubby re-ask (10, of which 4 already judged Chubby, at `Spine_to_BellyFlab`
    15.96-16.30), block 3 the Flat/Average axis grid (18, still assuming no axis -- round 1 found
    none), block 4 the abs ladder judged **from the front** (8), since round 1 was judged mostly in
    profile and may have missed abs. If cut short, blocks 1 and 2 are the ones that decide anything.
  - **`apply_d29_belly_rules.py` carries the new structure**, with `--keep-muscular-exclusive` to
    get the old regime back.

- **`D32`** -- 2026-09-19: **round 2 is in. The label-noise floor is 7%, and the evidence says Belly**
  **is genuinely two axes.** _(verdicts in `obody_arm_tools/belly_verdicts_d31.json`, 87 rows;
  design decision pending the user)_
  - **Self-consistency: 3 of 45 repeats flipped = 7%.** So **~93% is the accuracy ceiling** for any
    Belly rule; a fit that beats it is fitting noise. All three flips moved *toward* `Average`
    (`CBBE Gen` w0 Flat->Average, `S4rMs' (BustyThick) ProtoTHICC` w100 Flat->Average,
    `HomoErotic's (Natural) Stockpot` w50 Chubby->Average -- the user separately flagged that last
    one as a coin-flip: "if the belly protruded a centimeter more I'd call it chubby"). The
    far-from-boundary control did not flip. **All three sit where the thickness and shape axes
    disagree**, which is the same finding the grid below reaches from the other direction.
  - **The 2-D grid.** Binning the 87 verdicts by thickness (`Arch_to_BellyFlab`) x shape
    (`sternum_to_belly`):

    | thickness | sucked in | flat | protruding |
    |---|---|---|---|
    | thin | Thin x2 | Average x1 | **Thin x1** |
    | medium | Flat x5, Thin x3 | Flat x8, Thin x3 | Flat x3, Average x2 |
    | thick | -- | Flat x3, Chubby x1 | Pregnant x10, Chubby x9 |
    | very thick | **Flat x1** | -- | Chubby x18, Pregnant x7 |

    Thickness sets the coarse label and shape moves it *within* a band -- neither axis alone
    explains the verdicts. The two bolded cells are where 1-D breaks outright: a thin trunk that
    protrudes, and a very thick trunk that does not.
  - **Root cause of every unfittable boundary so far:** the existing value set already mixes the two
    axes. `Thin` is a **thickness** word, `Flat` is a **shape** word, `Chubby` is both. `Flat`
    currently spans thickness bands medium -> very thick and `Thin` spans all three shape bands, so
    no single measurement could ever have fitted them. That is why `D29`'s Flat/Average search kept
    landing on shoulder and chest measures: there was no belly axis to find.
  - **`BestBodNoSupport` w100 measures exactly as the user described it** ("thin, chubby and
    muscular at once... the upper belly is so thin"): bottom of the corpus on every thickness
    measure -- `Back_to_Belly` 0.7th percentile, `BellyVolume` 3.1st, `Arch_to_BellyFlab` 3.3rd,
    `waist_width` 4.6th -- while sitting at the **94th** percentile on `sternum_to_belly`. Its two
    shape measures even disagree with each other (`pubis_to_belly` 2nd percentile), which is what an
    upper-thin / lower-protruding profile looks like numerically.
  - **`Very Muscular` is supported and cheap.** `TheSuperMuscularInstagramBody-Cleavage` w50 reads
    `MuscleAbs` **200**; every preset the user called plain `Muscular` this round runs 62.5-123.
    Corpus: `MuscleAbs >= 120` is 2.38% of rows, `>= 150` is 1.57%, max 300. A slider-layer band at
    **>= 150** separates the one judged example with room to spare. One data point so far -- worth a
    handful of confirmations at 120-200 before committing.
  - **Open decision: split `Belly` into `BellyShape` x `TrunkThickness`.** Proposed by the user;
    recommended. **Taken up and applied as `D33`.**

- **`D33`** -- 2026-09-19: **`Belly` is dissolved into three independent categories.** _(applied;
  `obody_arm_tools/apply_d33_belly_split.py`, backup
  `OBodySettings - Copy (43) - before Belly split into Shape-Thickness-Morph.json`)_
  - **The split.** `BellyShape` (Sucked In / Flat / Protruding, on `sternum_to_belly`),
    `TrunkThickness` (Thin / Medium / Thick / Very Thick / Pregnant, on `Arch_to_BellyFlab`),
    `BellyMorph` (Normal / Muscular / Very Muscular, slider-only on `MuscleAbs`). `Chubby` and
    `Fat` leave the belly vocabulary entirely at the user's direction.
  - **Three categories, not two, for a mechanical reason.** `BellyMorph` has to be its own
    category: a slider-seeded descriptor marks its Category covered and suppresses that Category's
    default, so folding abs into `BellyShape` would blank the shape value on every muscular preset
    -- the bug `D31` had just removed. Verified: 0 rows now lack a shape or thickness value.
  - **`Build:Chubby` rebuilt from the two axes**, exactly as the user asked:
    `[TrunkThickness:Thick] AND [BellyShape:Protruding]` OR `[TrunkThickness:Very Thick]`. It
    catches **26 of the 28** round-2 Chubby verdicts and wrongly catches 9 Flat/Average rows --
    though 6 of those 9 are `Average`, which at the *Build* level is arguably right.
    **Population jumped 3.3% -> 14.2%**, because the old `Belly:Chubby` needed
    `sternum_to_belly >= 0.68` where `BellyShape:Protruding` fires at 0.20. **The only lever is the
    BellyShape cut** -- `D14` forbids a numeric term on `Build`. Sweep for reference: Protruding at
    0.35 -> 12.0%, 0.50 -> 10.2%, 0.68 -> 8.8%, all still catching 27/28.
  - **Other rewiring:** 12 negative `![Belly:Chubby]`/`![Belly:Fat]` guards across Build:Curvy /
    Slight / Powerful / Athletic became `![Build:Chubby]`, or were dropped where that guard was
    already present; 8 `[Belly:Pregnant]` refs went to `TrunkThickness:Pregnant`;
    `Realism:UnrealisticChest` g1/g2/g3 now read `[TrunkThickness:Medium]` / `[TrunkThickness:Thin]`
    / `[BellyMorph:Muscular]` -- behaviour-preserving, but **g3 is still the `D24` branch whose
    premise is void** (a muscular row now has its own thickness value, so g1/g2 fire on their own);
    decide it when Realism is taken up. The 39 surviving `PresetAnnotations` were dropped -- they
    name a category that no longer exists; `belly_verdicts_d29/d31.json` keep the history.
  - **Thresholds: one fitted, the rest provisional.** `TrunkThickness:Thin < 11.41` is fitted on
    **round 2 only** (1 error of 87). Round 1's Flat/Thin verdicts are deliberately excluded: in the
    overlap zone (`Arch_to_BellyFlab` 9.97-11.40) the labels split **perfectly by round** -- 9
    round-2 rows Thin, 8 round-1 rows Flat, zero mixing -- so they record the superseded definition,
    not noise. Merging the two rounds turns a 1-error fit into an 8-error one; don't.
  - **The Thin cut is the biggest open risk.** It makes **40% of the corpus Thin**. Round 2 drew its
    sample from pools defined by the old `Arch_to_BellyFlab < 10.20` rule, so it under-sampled the
    genuinely thin end and may have pulled the cut upward. Round 3 block 2 is a 10-rung ladder from
    9.4 to 15.5 built to settle it.
  - **Populations after the split:** BellyShape Flat 52.4% / Sucked In 23.9% / Protruding 23.7%;
    TrunkThickness Thin 39.9% / Medium 34.3% / Thick 20.3% / Very Thick 4.8% / Pregnant 0.7%;
    BellyMorph Normal 82.6% / Muscular 16.5% / Very Muscular 1.0%. No cycles; Arms and Hips
    (approved) show no drift.
  - **`Very Muscular` shipped at `MuscleAbs >= 150`** with `Muscular` narrowed to 70-150 so the two
    are exclusive. Still one judged example (`TheSuperMuscularInstagramBody-Cleavage` at 200);
    round 3 block 4 ladders 110-300 to confirm.
  - **No migration written**, per the standing fact that nothing on the Label-by-Measurements
    surface has shipped. Re-examine at the next version bump.

- **`D34`** -- 2026-09-19: **round 3 judged all three new categories; thresholds retuned. `Sucked In`**
  **does not survive the evidence.** _(applied via `obody_arm_tools/apply_d34_retune.py`, backup
  `OBodySettings - Copy (44) - before D34 belly retune.json`; verdicts in `belly_verdicts_d33.json`)_
  - **How it was collected.** The queue's target Category is single-select and governs the digit
    keys, the tally, **alias propagation** and the export, so the 42-case list was run three times,
    once per category, and the three exports merged per (preset, weight). `BuildFromCaseList`
    skips the "Include annotated" filter, so the passes do not interfere. Worth remembering for
    every future multi-category round.
  - **Retuned cuts** (error count = best single split on the 42 judged rows):
    `TrunkThickness:Thin < 9.60` (3 errors; **was 11.41**), `Thick >= 12.30` (2; was 12.60),
    `Very Thick >= 15.70` (0 anywhere in 15.17-16.28; was 14.60);
    `BellyShape:Protruding > 0.85` (1; **was 0.20**);
    `BellyMorph` Muscular >= 70 (fitted 68, left alone), **Very Muscular >= 170** (0 anywhere in
    161-180; was 150).
  - **The `D33` Thin cut was as wrong as feared.** 11.41 made 40% of the corpus Thin; the verdicts
    put it at 9.60 = **4.1%**. The sampling bias called out in `D33` was real and it was large.
  - **Accuracy now: TrunkThickness 37/42, BellyMorph 41/42, BellyShape 35/42.** Against the **7%
    label-noise ceiling** measured in `D32`, thickness and morph are at or near ceiling; shape is
    not, and all of the gap is `Sucked In`.
  - **`Sucked In` is not a shape the measurements can see.** `sternum_to_belly` separates it from
    `Flat` at only **6 errors of 37**. Every concavity proxy tried did worse or no better
    (`pubis_to_belly` 8, stb+pub 9, stb-pub 7, `min(stb, pub-2.4)` 7, stb/Back_to_Belly 6). The
    only measures that separate it are `Back_to_Belly` and `Arch_to_BellyFlab` at **2 errors** --
    both THICKNESS measures `TrunkThickness` already owns. **Recommendation: drop `Sucked In`,
    leaving BellyShape = Flat / Protruding.** Not applied; it is a descriptor-set change for the
    user to approve. The live rule is parked at the best single cut on its own axis (-0.39), which
    is not a fit.
  - **The two axes are strongly correlated as judged, but not redundant.** Block 1 was blind and
    did NOT control shape, and its 18 rows came back **perfectly diagonal** (Thin->Sucked In,
    Medium->Flat, Thick/Very Thick->Protruding); over all 42 only one cell is off-diagonal. So for
    the bulk of the corpus the two labels move together. The split still earns its keep in the
    tails -- `BestBodNoSupport` sits at bottom-3% thickness and 94th-percentile protrusion
    (`D32`) -- and in letting `Build:Chubby` be expressed at all. But do not expect the two
    categories to carry independent information on an ordinary body.
  - **Populations now:** TrunkThickness Medium 64.4% / Thick 28.5% / Thin 4.1% / Very Thick 2.4% /
    Pregnant 0.7%; BellyShape Flat 87.6% / Protruding 6.3% / Sucked In 6.1%; BellyMorph Normal
    82.6% / Muscular 17.0% / Very Muscular 0.5%. **`Build:Chubby` fell 14.2% -> 5.4%**, close to
    the 3.3% it had before the split -- the `D33` overshoot was entirely the 0.20 Protruding cut.

- **`D35`** -- 2026-09-19: **`BellyShape:Sucked In` removed; BellyShape is Flat / Protruding.**
  _(applied, user-approved; `obody_arm_tools/apply_d35_drop_suckedin.py`, backup
  `OBodySettings - Copy (45) - before dropping BellyShape Sucked In.json`)_
  - **The whole of BellyShape's error was in that one value.** Accuracy on the 42 round-3 rows goes
    **35/42 -> 41/42** by deleting it -- no threshold moved. With the 7% noise ceiling (`D32`)
    putting the practical maximum at ~39/42, all three categories are now at or above it:
    TrunkThickness 37/42, BellyShape 41/42, BellyMorph 41/42.
  - **Existing verdicts were remapped, not discarded.** The 9 `Sucked In` annotations became `Flat`:
    with the value gone, "not protruding" is exactly what `Flat` means, so the judgment survives.
    `belly_verdicts_d33.json` keeps the original word as the record of what was actually asked.
  - **Populations:** BellyShape **Flat 93.7% / Protruding 6.3%**; everything else unchanged
    (TrunkThickness Medium 64.4 / Thick 28.5 / Thin 4.1 / Very Thick 2.4 / Pregnant 0.7;
    BellyMorph Normal 82.6 / Muscular 17.0 / Very Muscular 0.5; Build:Chubby 5.4%). No cycles,
    0 rows missing a shape or thickness value, no drift on Arms or Hips.
  - **Worth knowing before distributing on it:** at 93.7% `Flat`, BellyShape is now close to a flag
    rather than a spread. It still earns its place -- `Build:Chubby` cannot be expressed without it,
    and it is what separates a deep trunk that protrudes from one that does not -- but a config
    author gets little selectivity from it on an ordinary body. `TrunkThickness` is the axis with
    real spread.
  - **Still open for Belly, all handed off 2026-09-19:** the `BellyVolume` w25 +21% defect from
    `D30` and the `Very Muscular >= 170` confirmation are written up in
    `obody_arm_tools/HANDOFF_bellyvolume_and_verymuscular.md`. The `Realism:UnrealisticChest` g3
    question is **parked in the Realism section** by the user's decision -- it and the other two
    Realism queries get settled when that descriptor is taken up, not before.
  - **New lead on the `BellyVolume` defect, found while writing the handoff:** the other two
    `RegionVolume` measurements are clean at w25 (`L_Breast_Vol` -0.38%, `Arm_Volume` -0.01%), so
    the fault is in the **`BellyBroad` region**, not in the volume math. `BellyBroad` carries 28
    `VertexEdits`; `BellyBroad_Bak` is the identical box with 0. That A/B pair is the cheap test.

- **`D36`** -- 2026-09-20: **the `BellyVolume` w25 defect is a scan bug, not a region bug. Fixed in**
  **code; the cache repair is staged and waits on one in-app rescan.** _(fix in
  `VM_BodyTypeProfileEditor.cs` scan prep; repair = `obody_arm_tools/strip_w25_bellyvolume.py`,
  applied 2026-09-20 -- backup `CBBE 3BA.measurement_cache - Copy - before D36 w25 BellyVolume
  strip.json`; rescanned in-app 2026-09-20 -- **DONE**; no rules changed)_
  - **The `_Bak` A/B test was not needed.** `RegionVolumeEvaluator`'s resolve and FlatPlane volume
    were ported to Python (scratch; induced-rule + clip paths, triangles read from the 3BA
    `NiSkinPartition`). Offline it reproduces the cached `BellyVolume` to ~0.1% at w0/50/75/100,
    and its own w25 is exactly linear -- e.g. `KiraSuzuki's Body` 270.5 / **285.7** / 301.0 against
    a cached w25 of **352.1**. On the zeroed 3BA body the region resolves identically at every
    weight (the _0/_1 NIFs don't differ inside the box), so a correctly-resolved region cannot
    produce a one-weight jump. The 28 edits and the box are innocent.
  - **What the w25 slot was actually baked on:** `(SDZ21) My Exquisite Body (Armor)` -- the first
    preset of the scan -- **deformed at weight 0**. Resolving `BellyBroad` against that mesh
    reproduces the cached w25 values at **0.00% error** on five probe presets. On that mesh all 28
    edits miss their vertices (matching is by position within 0.001), so the region silently fell
    back to a plain box clip: 1158 patch vertices, a 262-vertex loop, a bigger solid.
  - **Mechanism.** The batch scan's region prep used to prime the viewer with
    `ApplyBodySlide(firstPreset, firstWeight)`, then per weight push an empty `ApplyMorphSet` and
    read back the live `CpuPositions`. With a scene rebuild still in flight at scan start, the prime
    parked in `SynthEbdViewerHostState`'s queue and the w25 `ApplyMorphSet` parked in the VM's own
    queue. On commit the VM drains its queue first (sliders-0 @ 25) and **then** fires
    `SceneCommitted`, which replays the host's parked prime on top (preset @ 0). Two queues,
    replayed in the reverse of call order.
  - **Fix:** the prep now takes its reference from `viewer.GetZeroedShapePositions(shape, weight)`
    -- non-destructive, no queues -- which the editor's interactive region resolves already used.
    The prime and the `ApplyMorphSet` round-trip are gone, and unmatched vertex edits are now
    logged (`logMisses`), so a reference-mesh mixup can no longer be silent: it would have printed
    28 misses here. The two-queue order inversion itself is left in place; it only bit here because
    this pass read live geometry. **Worth fixing on its own if another caller hits it.**
  - **Blast radius: `BellyVolume` at w25 only.** Every other cached measurement is linear at all
    three interior weights, including `L_Breast_Vol` and `Arm_Volume` -- they were last filled in a
    different pass (the edits changed only `BellyVolume`'s fingerprint, so only it was re-filled in
    the faulty one). The `BellyFront` Region key vertex's measurements are clean too.
  - **Consequence for rules (previewed with the linear midpoint substituted for w25, BellyMorph
    seeded):** `TrunkThickness:Pregnant` **168 -> 165** -- three w25 rows were false Pregnant (two
    become Very Thick, one Thick); `Build:Chubby` 1385 -> 1387. Nothing else moves.
  - **`BellyVolume` is NOT the better Thin axis after all.** Re-fitted on the 42 round-3 verdicts:
    corrected `BellyVolume` has **18 rank inversions, best cut 4 errors**; `Arch_to_BellyFlab` has
    6 and 3 (and 3/42 is the 7% ceiling). Its round-1 advantage (0 inversions, `D30`) was measured
    against the superseded Thin definition. No threshold change.
  - **To finish (acceptance):** rebuild, close SynthEBD, run
    `python obody_arm_tools/strip_w25_bellyvolume.py --apply` (backs up the cache, removes the 5100
    bad cells), open the editor and **Scan All Presets** -- the partial-fill pass recomputes only
    `BellyVolume` @ 25. Then re-run the deviation test from the handoff (expect ~0.00%, `L_Breast_Vol`
    and `Arm_Volume` unchanged) and re-quote TrunkThickness. `BellyBroad_Bak` can be deleted from the
    profile at any time; it was never needed.
  - **Side defect found on the first app launch after this (2026-09-20):** OBodySettings.json
    would not load -- `apply_d34_retune.py` had written the two BellyMorph slider cuts as `170.0`,
    and `SliderClassificationRule.Value` is `int`, so Newtonsoft rejected the file. So **`D34`'s
    BellyMorph retune had never actually been loaded by the app.** Fixed in place (both -> `170`,
    parsed content otherwise identical; backup `OBodySettings - Copy (46) - before int fix of
    BellyMorph 170.0.json`) and the script's constants made ints. No other int-typed field holds a
    non-integer.
  - **Rescan result (2026-09-20) -- acceptance met.** Interior-weight median deviation of
    `BellyVolume`: w25 **-0.00%** (was +21.41%), w50 -0.00% (was -9.51%), w75 -0.00%. `L_Breast_Vol`
    (-0.38 / -0.36 / -0.34%) and `Arm_Volume` (-0.01% x3) unchanged. The rescanned values match the
    offline port to ~0.5 units, and the populations land exactly on the preview: **TrunkThickness
    Medium 16419 / Thick 7260 / Very Thick 603 / Pregnant 165 / Thin 1053; `Build:Chubby` 1387**.
    The remaining |dev| > 1% tail (64 / 54 / 32 rows at w25 / 50 / 75; `Arm_Volume` 55 / 35 / 23) is
    genuine volume convexity on extreme presets (plus-size bodies all bend -2..-8%), present at every
    weight, not the defect. One oddity for someday: `ousnius - Random Preset 0670` reads
    146 / 41 / 48 / 122 / 180 across weights -- the region collapses under that deformation.
    No throwaway measurement was ever added, so there is nothing to remove.

- **`D37`** -- 2026-09-20: **round 4 staged: a blind `BellyMorph` block to place the `Very Muscular`**
  **cut and settle the 27.5 anomaly.** _(`obody_arm_tools/make_d37_worklist.py` ->
  `belly_d37_worklist.txt`, 20 cases, seed 9173 + key; unjudged)_
  - **The corpus decides what "the cut" can mean.** Above `MuscleAbs` 150 there are two clusters --
    **exactly 150 (122 rows)** and **exactly 200 (86 rows)** -- with ~30 rows scattered between and
    nothing unjudged above 220. Any cut in 151-199 labels the same two clusters the same way; what
    moves is only the sparse middle. So the ladder samples each cluster twice (two distinct presets)
    and the middle at 160 / 175 / 180 / 190, plus 118 and 135 below.
  - **The 27.5 anomaly (`Z-O-E 77 x 03A (Closed Clothes)` w25) has no single-slider explanation.**
    Its candidates -- `RibsMore_v2` 52.5, `TummyTuck` 30, negative `BellyFrontDownFat_v2` /
    `WideWaistLine` / `ChubbyWaist`, `BackArch` -35 -- each also occur on rows judged Normal
    (`C6723's Realistic` RibsMore 50, `Decent of Kai` 57, `Ku Box redonkulous` BackArch -40). So the
    round tests it directly: the row is **re-served unmarked**, beside two fresh rows with the same
    lean-belly slider pattern and two plain rows at similar `MuscleAbs`. Flips to Normal -> noise.
  - **Design, one blind block, shuffled:** 10 ladder + 4 low (20-65) + 4 unmarked repeats (the
    anomaly and the three rows bracketing the cut: 152, 160, 180) + 2 zero-abs controls. Every note
    is the same contentless "judge from the FRONT; tick Normal if no abs show". Fresh draws exclude
    every preset judged in any round **and its near-duplicate variants** (this caught
    `TheSuperMuscularInstagramBody-...-NoNipples`, a variant of the round-2 example).
  - **Correction to past practice:** round 3's abs block named its threshold in the notes ("Very
    Muscular currently fires at >= 150") -- exactly the `D25` calibration leak. Its four abs rows are
    still usable, but weigh them as in-sample.
  - **Run it as ONE pass** with Target Category = `BellyMorph`, Sampling = `List`, Load list. Tick
    `Normal` explicitly on rows without abs, or they count as skipped.

- **`D38`** -- 2026-09-20: **round 4 is in; `BellyMorph` gains `Toned`, and round 5 is staged to
  place it.** _(round 4 verdicts: `obody_arm_tools/belly_verdicts_d37_export.json`; catalog change +
  verdict clearing via `apply_d38_add_toned.py`, backup `OBodySettings - Copy (47) - before D38 add
  BellyMorph Toned.json`; round 5 = `belly_d38_worklist.txt`, 48 cases, seed 3821, unjudged)_
  - **Round 4 (`D37`) results.** Low end settled: all 6 low rows (`MuscleAbs` 21-56) and both zero
    controls came back Normal, and the 27.5 anomaly (`Z-O-E 77`) came back Normal -- the `Muscular >= 70`
    cut stands. Upper cut: 150-199 is a mixed band. On the 16 Muscular / Very Muscular rows, the live
    `>= 170` makes 4 errors, `>= 195` makes 2 (Very Muscular 116 -> 99 rows). The misses follow other
    sliders -- belly fat hiding the abs (`DibellianArtsPresetNude` 150 judged Normal: `Belly` +100),
    `BigBelly` softening them (Aurelia 175, AllBodiesMatter 180 judged Muscular), `MuscleMoreAbs_v2`
    adding to them (ousnius 0905 at 160 judged Very Muscular). **Recommended `Very Muscular >= 195`; not
    applied, awaiting the user.** A combined slider score is not worth fitting on 16 rows.
  - **Found: queue repeats are not blind.** A Category's values are a tag set and the stored verdict is
    shown pre-ticked when a slice is served (by design, `D23`). That is how `Z-O-E 77` came back
    Normal + Muscular: the round-3 Muscular was pre-ticked and Normal was added. So round 4's three
    stable repeats were judged with the old answer visible, and **any repeat-reliability figure
    measured on already-annotated rows should be treated as optimistic** -- including, possibly, the
    `D32` 7% (check whether those rows carried stored verdicts when served). To repeat blind, clear
    the row's stored value for that Category first.
  - **The split (user, 2026-09-20).** `Normal` becomes "no definition" vs `Toned` (a hint of ab
    definition), because the two pair with different skin textures -- a faintly defined belly next to
    a texture with paunch shading looks wrong. The user's examples: `Z-O-E 77` Toned; `Z-O-E 09-11`,
    `AwSmallBreast`, `RowdyD1313's (BustyXSlim) Pumpkin Pie`, `kaosOneV3BA` no definition.
  - **Why it is not a rule yet.** `MuscleAbs` cannot separate them (Toned 27.5 vs no-definition 21 and
    30). `Z-O-E 09-11` carries the same lean-belly slider pattern as `Z-O-E 77`, stronger
    (`RibsMore_v2` 56.5 vs 52.5, `TummyTuck` 67 vs 30, `Belly` -31 vs -5); the only torso differences
    favouring `Z-O-E 77` are `MuscleAbs` 27.5 vs 21, `BackArch` -35 and `MuscleBack_v2` -50. A belly
    surface-relief measure (Laplacian of the deformed front-belly patch, scratch only) did not separate
    them either -- it scores fat folds as highly as ab grooves.
  - **Applied (additive only):** `Toned` inserted after `Normal` in the `BellyMorph` catalog. `Normal`
    is not renamed, the category default stays `Normal`, no rule was added -- nothing is auto-assigned
    `Toned`. The 34 stored BellyMorph verdicts that are now ambiguous (every `Normal`, plus `Z-O-E 77`'s
    two) were cleared so round 5 re-judges them blind; they are listed in
    `belly_d38_cleared_annotations.json`. 7 annotation entries held nothing else and were dropped.
    Remaining: Muscular 17, Very Muscular 7. Other categories' verdicts untouched; no drift.
  - **Round 5 design (one blind pass, Target Category = BellyMorph):** the 34 cleared rows + 14 fresh
    -- 7 lean bodies laddered `MuscleAbs` 0-60, 3 soft bodies at ~20/40/60, 2 lean with
    `MuscleMoreAbs_v2` >= 15, 2 lean with the `Z-O-E 77` back pattern. Notes contentless; nothing
    pre-ticked. **Then decide:** a `MuscleAbs` band -> slider rule; a slider combination -> AND/OR
    rule; repeats flipping -> `Toned` stays manual. Rename `Normal` -> `No Definition` (and move the
    default) only once the round is in.
  - **Round 5 is in (2026-09-21) and `Toned` is NOT slider-predictable as judged.** _(verdicts:
    `obody_arm_tools/belly_verdicts_d38_export.json`)_ 10 Toned / 38 Normal among the 48 rows. Three
    Toned rows have **zero on every abs slider** (`CustomPreset04`, `Farodadestin's (Perky) Office
    Crush`, `0_Igawa Asagi's Ass`), while `NinjaMommy` (`MuscleAbs` 50 + `MuscleMoreAbs_v2` 50) and
    `Last wish 3BA 1.2` (`MuscleMoreAbs_v2` 20) came back Normal. `MuscleAbs` is not in the top 15
    sliders by AUC; the top one is `HipForward` (0.86) -- a hip slider, and a chance-level best when
    ~150 sliders are tested against 10 positives. No measurement separates it either (best
    `BellyVolume` 0.29, i.e. Toned bodies are slightly smaller-bellied). `Z-O-E 09-11`, stated
    no-definition in chat, was judged Toned; all four Z-O-E-author bodies came back Toned.
  - **User's hypothesis: the textures are driving it.** At this subtlety the diffuse, normal and gloss
    maps carry fine ab-like detail of their own, and it catches the light differently on different
    belly contours -- which a mesh-only rule can never see. **Next: re-judge the same 48 rows
    untextured.** The viewer already has per-mesh texture toggles on its right-click menu
    (`UC_CharacterViewer.xaml.cs` `GlControl_MouseRightButtonUp`: Diffuse / Normal Map / Skin/SSS /
    Specular / Tint...; Diffuse off = flat 0.8 grey in `basic.frag`). If those survive slice changes
    in the queue, no code is needed; if not, a persistent global 'untextured' toggle is the handoff.
    Either way, clear the 48 rows' BellyMorph values first so the pass is blind.
  - **Tested 2026-09-21: the toggles hold across preset changes but reset on every weight change** --
    each weight slot has its own preview NPC, so `LoadNpcAsync` rebuilds the meshes and their per-mesh
    flags. Handoff for a 'Lock texture visibility' menu item written:
    `obody_arm_tools/HANDOFF_texture_visibility_lock.md`. Round 5 re-run waits on it.

- **`D39`** -- 2026-09-21: **with normal maps off, `Toned` becomes a `MuscleAbs` band. The textured
  verdicts were reading the normal map, not the mesh.** _(verdicts:
  `obody_arm_tools/belly_verdicts_d38b_nonormal_export.json`; nothing applied)_
  - **How judged:** the same 48 round-5 rows, normal maps disabled (diffuse on), with the new texture
    lock (`HANDOFF_texture_visibility_lock.md`, implemented by another session) holding it across
    weight changes. Caveat: the round-5 verdicts were still stored, so rows came up pre-ticked -- not
    blind. Anchoring would push toward agreement, yet 14 of 48 changed, so it did not dominate.
  - **The flips are one-directional in the way the texture hypothesis predicts.** All three Toned rows
    with zero on every abs slider (`CustomPreset04`, `Farodadestin's Office Crush`, `0_Igawa`) went
    to Normal, as did two low-abs Z-O-E bodies (21, 23). Nine rows at `MuscleAbs` 30-59 went the
    other way, Normal -> Toned (`NinjaMommy`, `CP Strong2`, `Fit and Realistic Extreme`, `Decent of
    Kai`, both Tinraa, ousnius 0175 / 0940, `AwSmallBreast`). So the normal map was adding ab-like
    detail to soft bodies and hiding real mesh definition on firm ones.
  - **Now: `MuscleAbs` AUC 0.94** for Toned vs Normal (textured pass: not in the top 15; next-best
    slider now 0.73). `MuscleMoreAbs_v2` adds nothing (sum 0.92). No measurement is useful (best 0.73,
    arm/shoulder = confound). Best cut: **Toned = `MuscleAbs` in [40, 70)** -> 5 errors of 48
    (89.6%, at the 7% noise ceiling); 50 also gives 5, 30 gives 8, 20 gives 12. The errors: three
    Toned below 40 (0702 26.4, `Z-O-E 77` 27.5, `AwSmallBreast` 30), ousnius 0589 at 45 Normal, and
    `DibellianArtsPresetNude` at 150 Normal (fat hiding abs -- a Muscular-rule miss, known from `D38`).
    25-40 is the mixed zone. The upper edge meets Muscular cleanly (`REACHBody` 67 Toned, ousnius
    0311 70 Muscular).
  - **Population if applied:** [40, 70) = 3775 rows (14.8%); [50, 70) would be 10.2%, [30, 70) 20.6%.
    Muscular + Very Muscular stay 17.4%.
  - **Proposed, awaiting the user:** add slider rule `Toned` = `MuscleAbs >= 40 AND < 70` (whole
    numbers -- `SliderClassificationRule.Value` is `int`); rename `Normal` -> `No Definition` and
    move the category default; optionally `Very Muscular >= 195` from `D38`. Store these verdicts as
    the BellyMorph annotations of record. **Judging convention from here on: BellyMorph is judged
    with normal maps off**, since the rule can only see the mesh.
  - **APPLIED 2026-09-21 (user approved all three)** via `obody_arm_tools/apply_d39_toned_rule.py`,
    backup `OBodySettings - Copy (48) - before D39 Toned rule and No Definition rename.json`.
    `BellyMorph` is now **No Definition / Toned / Muscular / Very Muscular**, slider-only on
    `MuscleAbs`: [0, 40) / [40, 70) / [70, 195) / >= 195, default `No Definition`. `Normal` was
    renamed in all four places it occurred (catalog, profile default, classifier default, 34 stored
    annotations); no rule referenced it. Thresholds are ints; the file is identical outside those
    fields; no drift.
  - **Populations:** No Definition 17277 (67.8%) / Toned 3775 (14.8%) / Muscular 4349 (17.1%) / Very
    Muscular 99 (0.4%). **Accuracy on the 72 judged rows: 65/72 = 90.3%**, at the noise ceiling.
  - **Side effect to know about:** `Realism:UnrealisticChest` g3 reads `[BellyMorph:Muscular]` (the
    `D24` branch parked in the Realism section). Moving Very Muscular to 195 moves 17 corpus rows
    (MuscleAbs 170-194) from Very Muscular into Muscular, so g3 can now fire on them. `Toned` rows
    are not Muscular, so the new band does not feed g3. Settle with the rest of Realism.
  - **`D39b` (2026-09-21): `No Definition` renamed `Low Definition`** (user): a flat mesh can still
    pick up apparent definition from a skin texture's normal map, so "No" overstated what the value
    guarantees -- it describes the mesh. Same four places; value + category descriptions now say the
    value is about the mesh and that BellyMorph is judged with normal maps off. Backup `OBodySettings -
    Copy (49) - before D39b Low Definition rename.json` (`apply_d39b_low_definition.py`); no drift.
  - **Bug found (pre-existing since `D33`): Match Presets could never show a slider-only value.** It
    filters on `ScanResults`, which is the measurement classifier's output, and by design seeds
    (slider matches, stored Manual/Library annotations) are never emitted there -- they only gate
    DescriptorRefs and suppress the Category default. So every BellyMorph row showed either the
    default (MuscleAbs < 40) or no BellyMorph value at all; `Muscular` had been unfindable since the
    split. **Fixed in `VM_BodyTypeProfileEditor.cs`** (built, not yet run in-app): the rebuild keeps
    each key's seeds in `VM_BodyTypeProfile.SeedDescriptors`, and the Match Presets list filters and
    displays classifier output + seeds (`GetMatchesWithSeeds`). `ScanResults` itself is unchanged, so
    its other consumers (tallies, rules tab, stale checks) still read the pure classifier output.
- **`D40`** -- 2026-09-21: **BellyShape and BellyMorph approved; a Pregnant -> Very Thick fix is
  proposed and a check list staged.** _(nothing applied to rules; worklist
  `obody_arm_tools/trunk_d40_worklist.txt`, 18 cases, seed 4417, + key)_
  - **Approved (user, 2026-09-21):** `BellyShape` (hash `f989a4aeb5` frozen in machine state) and
    `BellyMorph` (slider-only, grid only).
  - **The user flagged 11 slices as Very Thick, not Pregnant:** `CML CBBE Chunky` @50/@100, `Nami Extra
    Curvy` @100, `CustomPresetchubby` @75, `VenusMamaUltra (BHUNP)` @75/@100, `AA_LexiLove91` (all five).
    All are heavy all over (wide hips) rather than a bump on an ordinary frame; 10 fire Pregnant g0, one
    (`CustomPresetchubby` @75) g2.
  - **Ruled out:** a manual override (the slot merge protects only the identical (Category, Value), so a
    hand-set Very Thick would sit beside the rule's Pregnant; profile annotations never reach the
    patcher) and `sternum_to_belly` (most Pregnant rows protrude as little; a catching cut would strip
    `AwPregnant` and `Moon Bride Pregnant`).
  - **Proposed minimal change:** Pregnant g0 `hip_width < 33.85` -> **`< 30.55`** (midpoint between
    `AwPregnant` @100 at 30.43, which must stay, and `Nami Extra Curvy` at 30.66; 30.50-30.65 all behave
    identically), and g2 gains `hip_width < 33.85`. **Blast radius:** Pregnant 165 -> 139; the 11 flagged
    go 6 Very Thick + 5 Thick (LexiLove, `Arch_to_BellyFlab` 15.59 vs the 15.70 Very Thick cut); 15 other
    rows move (14 -> Very Thick, `CustomPresetchubby` @50 -> Thick): `CML CBBE Chunky` @75, `CP Chungus`,
    `JS Fat`, `Momma Body - Housewife` (x5), `Samaelxx46 (Fat) Very Fat`, `Urielman-Petite2BBW`, `Yui
    Preset customised`; Build -> Chubby on 26 rows; Realism / Chest / Shape unchanged. Lowering Very Thick
    to 15.55 to catch LexiLove would also move 71 Thick rows -- not recommended.
  - **Check list:** the 15 would-move rows (Momma Body served at w0 and w100 only; its waist widens
    with weight) + 6 unmarked contrast rows that keep Pregnant, nearest the new cut (`AwPregnant` @75/@100,
    `Moon Bride Pregnant`, ousnius 0206 / 0229 / 0948). One pass, Target Category = TrunkThickness.
  - **Check results (2026-09-22):** hip-only proposal 15/18. The user explained the misses: `Yui Preset
    customised` @75/@100 are Pregnant (a taut, significant bump -- unsupported fat would droop), `JS Fat`
    @75 is borderline and fine as Very Thick (author's intent), `CustomPresetchubby` @50 is Thick (a
    careless annotation). They confirmed arm thickness was their main context cue. Verdicts:
    `obody_arm_tools/trunk_verdicts_d40_export.json` (corrections applied in the analysis, not the file).
  - **Droop could not be measured:** `Arch_to_BellyFlab / Back_to_Belly` is degenerate -- the
    `BellyFront` (Y 73-84, midline) and `BellyFlab_F` (Y 70.6-78.1, full width) windows overlap, so on a
    large belly both pick the same vertex (18/29 rows read exactly 1.000). Offline midline-profile
    measures (apex height vs navel; forward depth 4 / 6 below the navel vs at it) scored 7-9 errors:
    only the heaviest bodies (Samaelxx46, CP Chungus, JS Fat, Momma Body) actually hang; the flagged
    ones are round and forward like the Pregnant ones. A real flab measure would need non-overlapping
    above/below-navel key vertices.
  - **APPLIED 2026-09-22 (variant C)** via `apply_d40_pregnant_frame.py`, backup `OBodySettings - Copy
    (50) - before D40 Pregnant hip-or-arms.json`. Pregnant: g0 hip <33.85 -> <30.55; g2 + hip <33.85;
    new g3 = g0's belly terms + original hip <33.85 + `Arm_Pinch` <7.74; new g4 = g2's belly terms +
    `Arm_Pinch` <7.74. Judged 29/29; nothing newly Pregnant (a first draft of g3 without the 33.85 ceiling
    admitted CP pear / Dino_3BA_044_Chubby_2 -- caught in simulation). Pregnant 165 -> 141, Very Thick
    603 -> 621, Thick 7260 -> 7266, Build:Chubby 1387 -> 1411; Realism / Chest / Shape unchanged. The arm
    cut is 0.02 from Yui@100 (7.72) and JS Fat@50 (7.76) -- those two rows are all it decides.
  - **TrunkThickness APPROVED (user, 2026-09-22)**, hash `099b38ef05` frozen. Exact-value accuracy on
    all 60 exported verdicts: 55/60 (91.7%).

- **`D41`** -- 2026-09-23: **BicepBulge split into Normal / Pronounced / Very Pronounced; applied.**
  - **Trigger:** the user flagged six presets that read bulged but were Normal (`Juno - Armor`(100),
    `CP Orc`(100), `Hjartulv's (Thick) The Female 3`, `Dino_3BA_026_Chubby`, `MyRaggy`, `Nami Muscle
    Mommy`). All have `MuscleArms` 100 and sit between the Arms gate (0.4264) and 0.442. Root cause: 0.442
    was never fitted on a bulge verdict -- it is the D13 Build:Powerful-vs-Athletic bar, carried over by D14.
  - **Why `Arm_Bicep_Ratio` and not `Arm_Bulge_Ratio` (re-checked on the full cache):** per 100 slider
    units, in SDs of the metric, Bicep responds +2.96 to MuscleArms against a worst confounder of 0.31
    (9.6x); Bulge_Ratio +2.37 against VanillaSSELo +2.15 / VanillaSSEHi +1.81 / Arms +1.47 (1.1x).
    FrontBack is 0.8x (VanillaSSEHi +2.76).
  - **Worklist:** `obody_arm_tools/make_d41_worklist.py` (seed 4141) -> `bicep_d41_worklist.txt`: 9
    blind random (3 per bicep band), 30 probes (Bicep x Bulge_Ratio grid, per-metric disagreement rows,
    clean MuscleArms ladder), the 6 flags. Verdicts `bicep_d41_verdicts.json` (15 P / 30 N); fit
    `fit_d41.py`. Single-cut errors (LOO): Bicep 3 (5), Bulge_Ratio 4 (4), FrontBack 5 (7), everything
    else 11-13 (14-24). Bulge_Ratio's four misses are its known confounders (BigTorso +/-, thin arms).
    Pairs only ever fixed one row (SubToBreed_Clothed) -- not adopted.
  - **Cut 0.437** from the window (0.4358 Mature N, 0.4389 Hjartulv P); 0.4353 also scores 3 but its
    window is 0.0005. Misses: `Juno - Armor`(100) P at 0.4287 (a non-blind flag), `CustomPreset01`(75)
    P at 0.4355 vs `Mature`(50) N at 0.4358 (the fuzzy edge), `SubToBreed_Clothed`(100) N at 0.4423
    (ChubbyArms -67 + MuscleMoreArms_v2 100: thin arm, big cap). Eye line ~ MuscleArms 90 on a plain arm.
  - **Why three values:** re-cutting the single Pronounced to 0.437 would move D13-judged Athletic
    rows (`Dibella's Hips` 0.4398; at 0.4353 also `Beautiful Angel`) to Build:Powerful. So the descriptor
    and the Build gate get separate values (the D12 / D14 two-cuts-on-one-measurement pattern):
    Pronounced = `0.437 <= Arm_Bicep_Ratio < 0.442`, new **Very Pronounced** = `>= 0.442` (exactly the old
    Pronounced set), and `Build:Powerful(F)` branch 4 now reads `[BicepBulge:Very Pronounced]`.
  - **Applied** via `obody_arm_tools/apply_d41.py` (dry run, then write; backup
    `OBodySettings.json.pre-d41.bak`). Asserted: Build unchanged on all 25500 cache rows, every other
    category unchanged, Very Pronounced == old Pronounced row-for-row, no co-occurrence, Build still pure
    DescriptorRef, D41 misses == the three above. BicepBulge rows: Normal 23875 / Pronounced 878 / Very
    Pronounced 747. Hashes: BicepBulge `4d741b847e` -> `9341bd6986` (2 rules); Build `a674dd5888` ->
    `293e181f51` (ref value renamed only; behaviour identical).
  - **Not changed, note:** the 45 D41 annotations are binary -- 7 rows the user labelled `Pronounced`
    now classify `Very Pronounced`, so they show as disagreements in the annotation grid. Also, 7
    presets carry `Manual` BicepBulge = Normal seeds in `BodyShapeDescriptorsByWeight` (`(SDZ21) My
    Lovely Body - Sexy Beauty (Nude)`, `(SDZ21) The Exothicc Beauty`, `- (Busty) Acid's Preset 2`,
    `ClaudeTest_01/02/03/06`), which override the rule for those presets.
  - **Soft spot:** the 0.4355-0.439 band holds only ~5 judged rows and two of them disagree; a short
    ladder there would firm up 0.437. _(applied)_
  - **BicepBulge APPROVED (user, 2026-09-23)**, hash `9341bd6986` frozen.

- **`D42`** -- 2026-09-26: **`Cup` deprecated in favour of `BustSize`; applied.**
  - **Why (user's call):** actual cup size can't be measured in a technically correct way. `Cup` banded
    `chest_projection` (one forward distance, no band size) into bra-cup letters; `BustSize` measures
    volume (`L_Breast_Vol`). This supersedes the Cup half of `D15` (Cup in the distribution-facing list).
  - **Removed** via `obody_arm_tools/apply_d42_remove_cup.py` (dry run, then write; backup
    `OBodySettings.json.pre-d42.bak`): the 10 Cup rules in each profile (20); CBBE `Chest:Petite/Medium/
    Busty`, which read only Cup (3); CBBE `Build:Slight`'s `NOT [Cup:D]` / `NOT [Cup:DD]` guards (it is now
    identical to 3BA's); the `TemplateDescriptors` Cup entry; and the empty Cup slider-classifier
    placeholder in all 12 `BodySlideClassificationRules` body types (the annotator would otherwise stash and
    re-save them forever). No presets carried manual Cup seeds or annotations; no code or distribution
    config referenced Cup.
  - **Asserted:** the spliced text equals the independently computed expected document; no Cup
    category/ref remains; on all 25500 CBBE 3BA cache rows every non-Cup category is unchanged. Rules: 3BA
    72 -> 62, CBBE 50 -> 37.
  - **CBBE side effects (deferred profile, expected):** CBBE assigns no `Chest` until the transfer, and CBBE
    Build moves on 895 of the 3BA cache rows used as a proxy (Chest:Busty no longer blocks Slight, plus the
    dropped Cup guards). CBBE Build hash `a2911a9dfb` -> `2313c31147`. The transfer replaces both.

- **`D43`** -- 2026-09-26: **Rules moved out of `OBodySettings.json` into shareable Body Type Rules files.**
  - **What moved:** each body type's Label by Sliders rules + Label by Measurements profile now live
    together in `<SynthEBD root>\Body Type Rules\<name>.json` (`BodyTypeRuleSet`: `Version`, `Name`,
    `BodyTypeName`, `SliderRules`, `MeasurementProfile`, `DescriptorDefinitions`). They travel together
    because measurement rules read slider-rule output (DescriptorRef) and the two share category
    defaults. Several files may target one body type; OBody > Misc > Body Type Rules picks the active one
    per body type, and that one choice drives both menus.
  - **What stayed in `OBodySettings.json`:** the worklist annotations (now a flat
    `PresetAnnotations` list, each tagged with `BodyTypeName`), `AnnotatorPrefsByBodyType`, the selection
    (`SelectedRuleFileByBodyType`), `DeclinedDescriptorImports`, `LastSelectedMeasurementBodyType`.
  - **Split done by script** (backup `OBodySettings.json.pre-rulefiles.bak`): `CBBE 3BA.json` (62
    measurement rules, 3 slider value-rules, 16 slider defaults, 69 descriptor definitions) and
    `CBBE.json` (37 rules, 40 definitions); 115 annotations moved. The 10 empty slider-classifier
    skeletons (BHUNP, HIMBO, UBE, Unknown, 3BA, 3BBB, BHUNP 3BBB, ...) had no rules or defaults and were
    dropped. Verified with the app's own loader: profiles, annotations, prefs and 3BA slider rules
    identical to the backup; all other settings keys untouched; app re-save stable. Rule hashes unchanged
    (`obody_scan.py`: no drift).
  - **Behaviour changes shipped with it:** worklist verdicts are applied to presets only via the new
    **Apply Annotations to Presets** button in the annotation queue (Manual, replacing that slot's
    category; asks before replacing a different manual value). Manual labels now win per (weight slot,
    category) in Label by Sliders and in the measurement merge (previously: one manual label anywhere on a
    preset blocked its category in every slot, and the measurement merge only blocked identical values).
  - **Tooling:** `obody_scan.py` and `obody_arm_tools/engine.py` read the active rule files now. **Still
    pointed at the old layout:** `jsonsplice.py` (anchors on `"BodyTypeProfiles": [`), the `make_d*_worklist`
    scripts, `KV_AUTHORING.md` / `README.md` -- retarget to `Body Type Rules\CBBE 3BA.json`
    (`"MeasurementProfile"`) before the next offline splice.
  - **TODO 1 (shipping defaults, `D4`):** once the preset descriptors are reviewed, copy the files into
    `SynthEBD\InternalData\Default Body Shape Rules\<name>.json` and have first launch copy them into
    `Body Type Rules\<name>.json`. Use `Version` to detect a newer shipped default: if the user's copy is
    unchanged since install, update it silently; if edited, ask **overwrite / keep mine / install the new
    one as a separate file**. Not built yet.
  - **TODO 2:** keep overruled suggestions (instead of deleting them when a manual label exists) and show
    them in red; compile the manual + suggestion master dictionary non-persistently before patching.
  - **Note:** the measurement classifier does not run at patch time -- `RunPatcher` applies only the
    annotation library and the slider rules (`AutoApplyMissingAnnotations`). Classifier labels reach
    distribution only via the live BodySlides preview merge, and are stripped on save. Relevant to TODO 2. _(open)_

- **`D44`** -- 2026-09-27: **Waist re-measured on a zeroed-space region; waist thresholds refit; applied.**
  - **Why:** `L/R_WaistSide` were unpaired Waist-pinch picks in a thin front slab (Z -7.13..-0.45). The
    Z faces clipped the waist side on ~52% of slices (waist read slightly narrow), and on extreme cups the
    slab caught only a cleavage sliver (TopHeavy4 w75/w100 waist_width ~1.1; 11 slices < 10 overall).
  - **Now:** both are **Paired Waist-pinch** (`PinchPairMaxX` / `PinchPairMinX`) over region **`Back`**,
    Defined On `(Zeroed Sliders) @ W50`: X -11..11, Y 71.8..98.9, Z 0.2..11.2. Region membership is by
    vertex identity, so breast tissue sagging into the waist range on big-cup presets is never a candidate.
    Face choices were simulated over all 25,500 slices: X +-11 (wider catches the inner arms -> Bad box);
    bottom 71.8 (74-77 lets a partial lower-back band win on heavy presets); front 0.2 (at -3 the region
    takes in the rear of the zeroed bust). Region-mode pinch picks matched the offline port 99.5%.
  - **Rescan:** 23,810 slices; the 338 hidden-and-disabled presets (x5 weights) are not rescanned and have
    no waist values. New vs old waist_width: median ratio 1.0043, half of slices within ~0.6%;
    **mis-measured before** = >10% off the median ratio = 199 slices / 89 presets (TopHeavy4 now 16.7-17.6).
  - **Thresholds refit** (`obody_arm_tools/apply_d44_waist_region_thresholds.py`; each placed mid-gap at
    the value minimising side-flips among the 23,611 correctly-measured slices): waist_to_hip
    0.50->0.5017, 0.52->0.5223, 0.59->0.5928, 0.592->0.5945 (and the disabled Neutral group's
    0.55->0.5525, 0.62->0.6225); waist_to_shoulder 0.5128->0.5136; waist_width 21.0->21.14. **Kept:**
    `waist_width <= 19.5` and `belly_width_to_proj > 0.75` in TrunkThickness:Pregnant -- refitting them
    moved no Pregnant call, so the approved TrunkThickness hash is untouched.
  - **Result vs the old labels:** 7.3% of correctly-measured slices change a label (Waist 1127, Build 572,
    Realism 500, Shape 329); all are borderline -- 75% had old values within 1% of a threshold, 95% within
    2%, max 5.6%. 137 of the 199 mis-measured slices change (Waist, Shape Apple->Pear, UnrealisticWaist).
    TrunkThickness: no change. `obody_scan.py`: only **Shape** drifts (provisional `5b89a36bcd` ->
    `f9454a5942`); baseline not updated pending review.
  - **Review:** `obody_arm_tools/waist_d44_worklist.txt` (38 cases) and `shape_d44_worklist.txt` (24),
    each a shuffled mix of fixed / borderline-moved / unchanged-control slices
    (`make_d44_worklist.py`, seed 4444; keys in `*_d44_worklist_key.json`). _(Shape half superseded 2026-10-08 by
    `shape_worklist.txt`; never judged)_
  - **2026-09-28:** the Waist half is superseded by `D45` (Waist no longer reads `waist_to_hip`), so
    `waist_d44_worklist.txt` is obsolete. The refit `waist_to_hip` thresholds still drive Shape
    Hourglass/Rectangle (`0.5945`) and Realism UnrealisticWaist (`0.5017`).

- **`D45`** -- 2026-09-28: **Waist reworked onto `waist_to_butt_width`; APPROVED (hash `2fb18fceee`).**
  - **Why:** `waist_to_hip` bins were driven mostly by hip width, not by the waist's in-curve: hip width
    varies far more than chest/armpit width (hip p1..p99 23.8..39.4 vs armpit p5..p95 16.5..20.1, and
    the two barely correlate, r = 0.10), so wide-hipped presets read Pinched regardless of the waist
    (median hip/armpit 1.78 on the Pinched slices an armpit-based ratio would call Neutral). Thick thighs
    rising into the hips (common on CBBE) inflate hip width further. An `armpit_to_waist` swap was tried
    and rejected: it agrees with the old bins on only ~45% of slices at matching bin sizes, and matching
    the old bins would reintroduce the hip bias anyway.
  - **Now:** Pinched = `waist_to_butt_width < 0.54`; Flat = `waist_to_butt_width >= 0.64`; Neutral =
    category default. `waist_to_butt_width` = X width at the waist (`L/R_WaistSide`, region `Back`) over
    X width at the butt (`L/R_WidthAtButt`, BoundingBox `BulgePair`, Y 68.23..73.98). Flat keeps its old
    `waist_to_hip` group, disabled. Judged in-app by Spread (the user found single-preset worklists
    unreliable for waist form -- it needs comparison against neighbours). Filled on all 23,810 scanned
    slices by the derivation pass (no body rebuild).
  - **Re-check:** Build (reads `Waist:Pinched` in `Build:Curvy` group 1). _(Build is still 🔧 Revise.)_

- **`D46`** -- 2026-09-28: **`armpit_width` pinned to a fixed vertex pair. APPLIED 2026-09-29** (backup
  `CBBE 3BA.json.pre-d46.bak`; no rule hash moved -- Shape's scanner drift is the pre-existing `D44` one).
  - **Symptom:** `L/R_Armpit_Rear` (BoundingBox, `BoneTransitionPairMaxX/MinX`, X +-19.78, Y 100.72..102.0,
    Z 7.09..12.15) gave `armpit_width` < 12 on 46 slices / 17 presets (median 18.5).
  - **What the search actually finds:** not the torso/arm seam. It finds the **Spine2 -> Clavicle** skin-weight
    seam on the upper back (zeroed w50: x +-9.27 at Y 101). The real torso/arm seam (Spine2 -> UpperarmTwist)
    is lower, at Y 94..98. Skin weights never change with sliders, so on well-sampled rows the search returns
    a fixed pair: **v3949 / v7728 on 85% of rows**, v7724 / v7729 (the same seam about 0.7 units narrower,
    slightly higher) on 14.5%, and one other pair on 0.5%.
  - **Three failure modes (live box):** (A) sparse band: the deformed body leaves only a few back vertices in
    the 1.3-unit band, so the walk jumps from a Spine2 vertex at x 1.7 straight to Clavicle at x 12
    (Ku Box Hippo w100 3.4, My preset w100 3.6); (C) the band rides up to where Clavicle reaches x +-4.6
    (**Z-O-E 27 push reads 9.4 at every weight but is really 18.6**); (B) folded meshes, no fix possible
    (ousnius 0315 `ShoulderWidth`=1147, ousnius 0256 `VanillaSSEHi`=-668).
  - **Not a defect:** Golden Rounded, Pyramid True, Lexi, Lineage and 1 Ultimate stay ~9.5 even on a fixed
    pair. They set 3BA `ShoulderWidth` to 300..450 (it moves these vertices inward ~0.8 per 100). Their
    `shoulder_width` is 19..24 and the torso's full X extent at that height is 15.7..17 (zeroed 20.5), so
    they really are narrow up top and Pear is correct for them. The value is exaggerated (-50% vs -20% for
    the torso outline) but it points the right way.
  - **Method:** Python port of `FindBoneTransitionX` / `FindPairedBoneTransitionX` plus skin weights read
    as `NifMeshBuilder` does (NiSkinData per-bone lists, first-empty-slot fill). It reproduces the cached
    `armpit_width` on **25,457 / 25,500 rows (99.83%)**; the rest are duplicate-name rows (`D10`). All
    10 candidates were simulated on every cached row (`armpit_sim.py`, 46 s): the live box, 3 taller or
    lower BoundingBoxes, 4 zeroed-space Regions, and 2 fixed pairs.
  - **Result (`armpit_analyze.py`):** the fixed pair **FX_A** (v7728 / v3949) has 7 presets < 12 (the
    5 real-narrow presets + 2 folded), is exactly linear in weight on every preset (0 non-linear presets,
    vs 13 for the live box), and moves **390** Shape rows (1.5%). A Region over the live box gives the same
    numbers but keeps the search. Taller boxes move 750..1518 rows and add non-linear presets. The
    anatomical seam (Y 95..98.5, `BB_LOW` / `RG_LOW`) is cleanest (3..4 presets < 12) but moves **~4,280**
    rows (17%), which would need a Shape refit: kept as the option if Shape is ever re-based.
  - **Movers:** 384 of the 390 are rows where the live box picked the narrower pair (their width rises
    about 0.69, `armpit_to_hip` +0.023); only 2 are collapse fixes. Flows: Pear -> Hourglass 301,
    -> Rectangle 55, -> Ambiguous 34 (those 34 are hidden-and-disabled presets with no waist values, `D44`).
    `armpit_refit.py` sweeps the 0.618 cut: **0.618 is already the mover-minimising value** (any other cut
    moves more pair-A rows than it saves), so no rule edit.
  - **Edit (`apply_d46_armpit_fixed.py`, dry-run validated):** rewrite both rows in place as Coordinate
    key vertices (template `L_ArmMid_F`): `L_Armpit_Rear` = v7728 (9.274404, 101.082985, 8.135844),
    `R_Armpit_Rear` = v3949 (-9.274402, 101.082985, 8.135849). Coords are exact to about 5e-7 and the
    next vertex is 0.49 away. Names are kept, so all four measurements keep resolving. Old rows for the record:
    BoundingBox X +-19.783985, Y 100.71647..101.99831, Z 7.090286..12.154551, criteria
    `BoneTransitionPairMaxX` (L) / `BoneTransitionPairMinX` (R).
  - **Review:** `obody_arm_tools/shape_d46_worklist.txt` (24 cases: 14 moved / 4 collapse-fixed / 6
    near-cut controls; seed 4646; `make_d46_worklist.py`). Shape is provisional, so a moved row means
    re-investigating it (`check_shape_provisional.py`). _(rescan done -- cache verified 2026-10-08; superseded by
    `shape_worklist.txt`, see Current focus)_
- **`D48`** -- 2026-10-08: **BustSize `Flat` moved onto projection; APPLIED, then SUPERSEDED by `D48b`** (same day) (`apply_d48_bustsize_flat.py`,
  backups `CBBE 3BA.json.pre-d48.bak` + `OBodySettings.json.pre-d48.bak`; BustSize hash `2be5185a7a` -> `93c4715a79`).
  - **Trigger:** on Spread the user found Erydia's (Skinny) Hel (all weights; vol 201-237, s2c 1.17-1.26) and
    Kettle-Maiden Stormcloaks Nexus @75 (vol 300.0, s2c 1.82) called Petite but really Flat. Flat itself had
    no false positives.
  - **Worklist:** `obody_arm_tools/bustsize_flat_worklist.txt` (18 cases, seed 1717,
    `make_bustsize_flat_worklist.py`): 9 low-projection Petite (s2c 1.0-1.5), 3 mid (1.5-2.0), 2 high
    (>= 2.0), 4 Flat edge controls. Projection alone separates the judged rows: Flat s2c <= 1.29, Petite
    s2c >= 1.418. No Flat row in the corpus has s2c above 1.47, so the old `vol < 160` branch was really a
    stand-in for projection.
  - **Was:** `vol < 160` OR (`s2c < 1.5` AND `vol < 170`) OR (`s2c < 1.0` AND `vol < 300`).
    **Now:** `vol < 160` (fallback for the 2 rows with no s2c) OR (`s2c < 1.35` AND `vol < 300`).
    vol = `L_Breast_Vol`, s2c = `sternum_to_chest`. The `vol < 300` cap stays: 10 rows with s2c < 1.35 at
    vol 300-450 are untested.
  - **Fit:** 17/19 judged presets. Accepted misses, pending the user's 3D look: **Kushin Flat but Cakey @75**
    (judged Petite, s2c 1.054, vol 177; Dino_3BA_003_Slim @50 at 1.046 / 194 is Flat) and **Kettle-Maiden
    Stormcloaks Nexus @75** (judged Flat, s2c 1.82; Argonian Female @0 1.73 / 255 and ousnius 0472 @0
    1.845 / 270 are Petite). Borderline per the user: **KML Body @0** (Petite, s2c 1.450, vol 222). It is
    not the nearest Petite row to the cut (four sit at 1.418-1.445), so flipping it would cost four others.
  - **Populations:** Flat 526 -> 1030 (+513 / -9; the 9 are s2c 1.35-1.47 rows at vol 160-170 that the old
    second branch caught), Petite 4642 -> 4138. Small and above are unchanged.
  - **Annotations:** the 13 Petite verdicts were banked as `Small` by mistake (user meant Petite) and were
    relabelled `Petite` in `PresetAnnotations`. Hel and Nexus @75 are verbal verdicts only, not banked.
  - **Dependents:** none moved -- Chest and Build are identical on all cache rows (Chest maps Flat and Petite alike). _(resolved by `D48b`)_
- **`D48b`** -- 2026-10-08: **BustSize `Flat` narrowed to `sternum_to_chest < 1.0` AND `L_Breast_Vol < 300`; APPLIED**
  (`apply_d48b_bustsize_flat.py`, backups `*.pre-d48b.bak`; the `*.pre-d48.bak` files are the pre-D48 state;
  BustSize hash `93c4715a79` -> `fad252271d`).
  - **Why:** the user re-judged `D48`'s set in the 3D viewer and found `D48` leaned too far towards Flat. In
    3D only ousnius 0518 @25 (s2c 0.861) and TxAssassin AA-DD V2 @0 (0.965) are Flat. Dino_3BA_003_Slim @50
    (1.046), Kushin @75 (1.054), Wrongii's Petite @50 (1.217), Leblanc008 @25 (1.291), Hel @0 / @100
    (1.17 / 1.26) and Kettle-Maiden Stormcloaks Nexus @75 (1.82) are all Petite. Hel and Nexus had been
    called Flat from the 2D panel, which hides projection.
  - **Rule:** single group `s2c < 1.0 AND vol < 300`, so `D48`'s Kushin/Dino conflict and both 2D
    outliers are gone. The `vol < 160` branch is dropped: Leblanc (vol 156) is Petite, and the only rows
    without s2c are the folded ousnius 0256 mesh. **21/21 judged rows correct.**
  - **Populations:** Flat 495 (pre-`D48` 526, `D48` 1030), Petite 4673. Chest and Build are identical to
    pre-`D48` on all cache rows. Untested: 9 rows with vol 42-159 and s2c >= 1.0 that are now Petite
    (Leblanc at 156 is the only evidence there).
  - **Annotations:** Dino @50 and Leblanc @25 relabelled Flat -> Petite; Wrongii @50 was already Petite
    (re-labelled in-app). Hel and Nexus @75 are verbal verdicts, not banked.
  - **Lesson:** judge projection-driven categories in 3D. The 2D panel produced all four "misses" in `D48`.
- **`D49`** -- 2026-10-08: **Chest key vertices moved onto the breast regions; APPLIED (needs the in-app rescan).**
  - **Why:** on presets with a "shoulders back, chest out" posture (Dino_3BA_001_Slim @100), the wide chest box's
    `L_ChestFront` (MinZRightOfX) landed on the upper belly at the lower ribs, inflating `chest_sag_ratio`.
  - **Regions:** the user drew `L_Breast_Conservative` (same box as `L_Breast`, 1308 remove + 11 add edits;
    ~1100 of the removals are the nipple/areola cluster around zeroed (7.1, 96.4) -- **excluding the nipple is intentional** (user, 2026-10-08), so the chest measurements no longer respond to nipple sliders). `R_Breast_Conservative` is its
    X-mirror (`breast_region_mirror.py`): the 3BA mesh is up to 0.025 asymmetric, past the app's 1e-3 edit
    matcher, so each mirrored edit is snapped 1:1 to a real vertex (assignment), plus 3 box-edge fixes; R's 2336
    members are exactly the mirror image of L's. `L_Breast` (volume) is untouched.
  - **Key vertices** (`apply_d49_breast_regions.py`): `L_ChestFront` / `R_ChestFront` -> Region MinZ on L / R;
    `L_ChestSide` -> Region MaxX on L, `R_ChestSide` -> Region MinX on R. The pair criteria were dropped:
    they only pair rows on the same region, so on two regions they silently become unpaired bulges.
    Backups: `CBBE 3BA.json.pre-d49.bak` (before everything), `CBBE 3BA.json.pre-d49-kv.bak` +
    `OBodySettings.json.pre-d49-kv.bak` (after adding the region, before the key-vertex change); measurement
    cache snapshot `CBBE 3BA.measurement_cache.json.pre-chestfront.bak`.
  - **Forecast** (`chest_region_sim.py` -> `chest_region_predict.py`; port reproduces the cache on 25,316 / 25,496
    rows): today's front pick is a nipple vertex on ~68% of rows, so the change is broad, not just the posture
    outliers -- median `chest_projection` / `sternum_to_chest` -0.13, `chest_sag_vertical` +0.81,
    `chest_sag_ratio` +0.14. Moves: **BustHeight 3374 rows** (Lifted -> Typical 2964, Lifted -> Sagging 227);
    **Realism 991** (UnrealisticChestLift drops); **Build 436** (Exaggerated drops, via Realism);
    **BustSize 78** (Petite -> Flat); Chest and Shape 0. Movers listed in `obody_arm_tools/chest_region_movers.json`.
  - **Approved cell hit without drift (`D20` blind spot):** BustSize's hash is unchanged but its input moves:
    banked verdicts Kushin @75 (s2c 1.054 -> 0.932) and Wrongii @50 (1.217 -> 0.855) become wrong at the 1.0 cut.
    BustHeight verdicts: Dino @100 (ratio 3.33 -> 2.14) and Stellar Blade Curvy @100 become right (6 -> 4 wrong).
  - **Rescan verified:** the in-app cache matches the forecast on 99.5% of values. _(resolved by `D50`)_
- **`D50`** -- 2026-10-08: **BustSize Flat and BustHeight Sagging refit on the `D49` values; APPLIED**
  (`apply_d50_chest_refit.py`, backups `*.pre-d50.bak`). Judged in 3D: `bustsize_d49_worklist.txt` (10) +
  `bustheight_d49_worklist.txt` (18), seed 4949, `make_d49_worklists.py`; scored with all earlier verdicts.
  - **BustSize (reopened):** Flat `s2c < 1.0` -> **`< 0.80`** (AND vol < 300). 29/31; misses ousnius 0518 @25
    (Flat at 0.861) and Strong Redone 1.5 @25 (Petite at 0.780). Every single chest measurement was tried as the
    cut: none does better than 2 errors, so the boundary is genuinely noisy below ~0.87. Flat 525 -> 354 rows.
    Hash `fad252271d` -> `15bb88b441`; needs re-approval.
  - **BustHeight:** Sagging group 1 (proj >= 12) ratio `>= 1.15` -> **`>= 1.20`**; group 4 (proj >= 5) ratio
    `>= 2.5` -> **`>= 2.65`**. Lifted is unchanged: on the new values its 1.30 cut still splits Lifted <= 1.270 /
    Typical >= 1.310, so the 2964 Lifted -> Typical movers from `D49` were correct. 36/40 (was 30/40); misses
    Devonix FinVahzahSlen @25 (Sagging, ratio 1.150, proj 14.01 -- twin of SyndiChips @100, Typical from 2D),
    Flying-Dutchman-09 @100 and VanityElf Slim to Thiccc @75 (Typical at the proj 12.0 edge; fixing them needs a
    `proj >= 12.1` hairline -- rejected as overfit), ousnius 0049 @75 (Lifted at 1.319 vs Janos Typical at 1.310).
    Sagging 589 -> 356 rows. Hash `e30b6b72d9` -> `0b2170926e`.
  - **Annotations:** the 8 verdicts given outside the queue were banked (5 BustHeight from Spread, 3 BustSize
    for Hel @0/@100 and Nexus @75).
- **`D51`** -- 2026-10-08: **BustSize Small|Medium and Medium|Large edges lowered (EXPLORATORY, applied for an
  in-app Spread review)** (`apply_d51_bustsize_bands.py`, backups `*.pre-d51.bak`; BustSize hash `15bb88b441` ->
  `a574fefdc7`, so the scan shows drift on the approved cell until the user keeps or reverts it).
  - **Trigger:** the 3D Chest worklist (`chest_worklist.txt`, 18 cases, seed 5151, `make_chest_worklist.py`) was
    judged consistently larger than the BustSize-derived Chest mapping: Medium from `L_Breast_Vol` 370 (rule: 450),
    Busty from 671 (rule: 750; Medium judged up to 530, no samples between). Both boundaries fell inside BustSize
    bands, so a pure Chest remap could not express them; the user chose to move the BustSize bands (option 3).
  - **Edit:** Small `vol < 450` -> **`< 360`**; Large `750..1150` -> **`600..1150`** (Medium's disabled
    documentation group follows). Flat/Petite (300), Very Large (1150), Enormous (1600) and the Chest mapping are
    unchanged. Small 7778 -> 3290, Medium 7812 -> 9654, Large 3135 -> 5781. Chest 17/18 (miss: my ragged new 2 @100,
    Petite at 378). Knock-ons: Chest Petite -> Medium 4488, Medium -> Busty 2646; Build gains Curvy on ~2390 rows.
  - **Kept:** the user reviewed Spread in-app and preferred the new cuts; BustSize re-approved at `a574fefdc7`
    and Chest approved at `92221f8130` (2026-10-08).
---

## Session log

Append newest at the top. Keep entries short: date, what changed, what got approved.

- **2026-10-08** -- **`BustSize` RE-APPROVED** (hash `a574fefdc7`, `D51` bands kept after the in-app Spread review) and **`Chest` APPROVED** (hash `92221f8130`; pure BustSize mapping, 17/18 judged in 3D). Tier 2 left: Realism; then Build.

- **2026-10-08** -- **`D51` applied (exploratory)**: BustSize Small `< 360` (was 450), Large from `600` (was 750), to match the 3D Chest verdicts. BustSize shows drift vs its approval (`15bb88b441` -> `a574fefdc7`) pending the user's Spread review; revert = `CBBE 3BA.json.pre-d51.bak`.

- **2026-10-08** -- **`BustSize` RE-APPROVED** (hash `15bb88b441` frozen; Flat `s2c < 0.80`, `D50`) and **`BustHeight` APPROVED** (hash `0b2170926e` frozen; Sagging ratio `>= 1.20` at proj >= 12 / `>= 2.65` at proj 5-6, Lifted unchanged, `D50`). Both on the `D49` breast-region key vertices. Next tier-2 candidates: Chest (reads BustSize), Realism (reads the same chest measurements).

- **2026-10-08** -- **`D50` applied** (`apply_d50_chest_refit.py`): after the `D49` rescan, BustSize Flat `s2c < 0.80` (29/31; **BustSize reopened**, hash `15bb88b441`) and BustHeight Sagging ratio `>= 1.20` at proj >= 12 / `>= 2.65` at proj 5-6 (36/40, hash `0b2170926e`). 8 off-queue verdicts banked.

- **2026-10-08** -- **`D49` applied** (`apply_d49_breast_regions.py`): `R_Breast_Conservative` added (mirror of the user's L region); `L/R_ChestFront` and `L/R_ChestSide` now resolve on the two breast regions. Needs the in-app rescan. Forecast: BustHeight 3374 / Realism 991 / Build 436 / BustSize 78 rows move; BustSize stays hash-approved but 2 of its verdicts go wrong -- refit after the rescan.

- **2026-10-08** -- **`BustSize` APPROVED** (hash `fad252271d` frozen): Flat = `sternum_to_chest < 1.0` AND `L_Breast_Vol < 300` (`D48b`); Petite / Small / Medium / Large / Very Large / Enormous on `L_Breast_Vol` (300 / 450 / 750 / 1150 / 1600). Unblocks Chest (tier 2).

- **2026-10-08** -- **`D48b` applied** (`apply_d48b_bustsize_flat.py`, backups `*.pre-d48b.bak`), superseding `D48`: after the user's 3D re-judging, BustSize Flat = `sternum_to_chest < 1.0` AND `L_Breast_Vol < 300`; Flat 495 (was 1030 under `D48`, 526 before it); 21/21 judged rows. Chest/Build unchanged. BustSize hash now `fad252271d`. Status unchanged (draft).

- **2026-10-08** -- **`D48` applied** (`apply_d48_bustsize_flat.py`, backups `*.pre-d48.bak`): BustSize Flat = `sternum_to_chest < 1.35` AND `L_Breast_Vol < 300` (+ `vol < 160` fallback); Flat 526 -> 1030. 13 worklist verdicts relabelled Small -> Petite. BustSize hash now `93c4715a79`. No rescan needed (existing measurements). Status unchanged (draft) pending the user's 3D look at Kushin @75 / KML @0 / Nexus @75.

- **2026-10-08** -- **`Butt` APPROVED as-is** (hash `916739853e` frozen): Flat `< 2.05` / Medium (default) / Large `>= 3.09` / Very Large `>= 4.07` AND `GluteFold_Projection >= 4.10` on `fold_to_butt` (`D47`). No rule changes.

- **2026-10-04** -- **`D47` applied** (`apply_d47_butt_fold.py` + `apply_d47b_butt_default.py`, backup `CBBE 3BA.json.pre-d47.bak`): Butt rules moved from `butt_projection` to `fold_to_butt` (new Coordinate KV `L_GluteUnder`) with a `GluteFold_Projection` gate on Very Large; stale profile default `Butt: Normal` corrected to `Medium`. Butt hash now `916739853e`. Needs the in-app rescan. Status unchanged (draft).

- **2026-09-29** -- **`D46` applied** (`apply_d46_armpit_fixed.py`, backup `CBBE 3BA.json.pre-d46.bak`). Needs the in-app rescan, then `shape_d46_worklist.txt`.

- **2026-09-29** -- **`Thighs` APPROVED as-is** (hash `f03fecf044` frozen): Skinny `< 11.65` / Normal (default) / Thick `>= 14.5` on `thigh_thickness`. No rule changes.

- **2026-09-28** -- **`D46` staged (not applied):** `armpit_width` collapse root-caused (the thin band loses the Spine2/Clavicle seam); fix = pin `L/R_Armpit_Rear` to Coordinate v7728 / v3949, rules unchanged, ~390 predicted Shape movers (1.5%). Apply with `apply_d46_armpit_fixed.py`, rescan, judge `shape_d46_worklist.txt`.

- **2026-09-28** -- **`Waist` APPROVED** (hash `2fb18fceee` frozen): Pinched `< 0.54` / Flat `>= 0.64` on `waist_to_butt_width` (`D45`). **Next session: fix `armpit_width`** (collapses on 17 presets; feeds Shape via `armpit_to_hip`) -- see Current focus.

- **2026-09-27** -- **`D44`: waist now measured on region `Back`** (Paired Waist-pinch, zeroed-space box); waist_to_hip / waist_to_shoulder / waist_width thresholds refit to keep correctly-measured slices in place (7.3% borderline movers). TrunkThickness untouched; Shape (provisional) drifts -- review `waist_d44_worklist.txt` + `shape_d44_worklist.txt`.

- **2026-09-26** -- **`D43`: rules moved into `Body Type Rules\CBBE 3BA.json` / `CBBE.json`**; annotations stay in `OBodySettings.json`. No rule content changed (no drift). Apply Annotations to Presets button added; manual labels now win per weight slot.
- **2026-09-26** -- **`D42`: `Cup` removed** (deprecated in favour of `BustSize`): 20 Cup rules, CBBE's 3 Cup-based Chest rules, CBBE Build:Slight's Cup guards, the catalog entry and 12 classifier placeholders. 3BA non-Cup classifications unchanged on all cache rows.
- **2026-09-26** -- **`ShoulderWidth` APPROVED** (hash `7954e4e2c1` frozen).
- **2026-09-26** -- **`ShoulderMorph` APPROVED** (hash `921369a744` frozen).
- **2026-09-23** -- **`BicepBulge` APPROVED** (hash `9341bd6986` frozen).
- **2026-09-23** -- **`D41`**: BicepBulge -> Normal / Pronounced (0.437-0.442) / Very Pronounced (>= 0.442) on `Arm_Bicep_Ratio`, fitted on a 45-row blind worklist; Build reads Very Pronounced (behaviour identical). `Dino_3BA_032_Muscles` added to Build's presets-to-watch.
- **2026-09-20** -- **`BellyVolume` w25 defect root-caused and fixed in code (`D36`)**: the scan's
  region prep read live viewer geometry and caught a replayed preset; it now uses
  `GetZeroedShapePositions`. Cache repair staged (`strip_w25_bellyvolume.py`), needs `--apply` + one
  in-app scan. `BellyMorph` round 4 staged (`D37`). No rules changed.

- **2026-09-18** -- **annotation queue gains `List` mode.** A fourth sampling mode serves a
  hand-authored worklist (plain text or JSON, with a per-case note shown while judging) in the order
  written, ignoring the other sampling options; lists load from a file or the clipboard, and a
  verdict export is itself a valid worklist. Space now commits, Tab skips. Intended for `D29`
  step 2's spot check. No rules or annotations changed.
- **2026-09-18** -- **`D28`: annotation queue implemented.** The Label-then-Suggest tab now
  has a queue with `Spread` / `Uncertainty` / `Random` sampling, an exactly-counted random-draw
  fraction (default 0.25) with per-slice provenance, alias-family de-duplication with verdict
  propagation, keyboard auto-advance (digits toggle, Enter commits and loads the next body), a
  background prewarm of the next body, and JSON verdict export carrying policy + seed. Rules-only
  categories became labellable. No classification behavior changed and nothing rescans. Rules
  untouched; all hashes unchanged. Not yet exercised on a real labelling run.
- **2026-09-18** -- **`D25`/`D26`/`D27`.** New key vertex `BellyFlab_F` (user-authored) +
  3 depth measurements added and cache rescanned; `Belly:Flat` renamed **`Belly:Thin`** (pure rename,
  populations identical); `Belly:Chubby` and `Belly:Pregnant` rewritten on `Spine_to_BellyFlab`
  (Chubby 435 -> 801, Pregnant 127 -> 168). Judged 74/84 -- **but a random spot check of the changed
  rows scored 8/14**, so the live Belly rules are provisional. Diagnosed: butt contamination in
  `Spine_to_BellyFlab` (+0.487 vs `butt_projection`), a mis-chosen `sternum_to_belly` companion term,
  and Pregnant over-reach. Leading fix (not applied): swap the companion to `butt_projection <= 7.41`
  -> 3 errors on 57 hard rows. Verdict corpus grew to **84** slices, saved to
  `obody_arm_tools/belly_verdicts.json` and back-filled into `PresetAnnotations` (84 entries).
  Belly `6c86c3730a` -> `8ce915f100`; Realism `79bd14de40` -> `6c1c09c19d` (rename only).
  Belly and Realism both stay Draft.
- **2026-09-14** -- **`D24` UnrealisticChest reads Muscular.** Filling the hole `D23`
  exposed: `Realism:UnrealisticChest` gated only on `[Belly:Normal]` / `[Belly:Flat]`, neither of
  which a slider-seeded Muscular row can carry, so those bodies fell through to the unconditional
  `> 19`. Added `chest_projection > 16.0 AND [Belly:Muscular]` as a fourth group. Purely additive:
  138 rows gain the value when seeded, 0 rows change otherwise, nothing is lost anywhere. Restores
  the 11 rows D23 cost plus ~127 more that never had it. Realism `cdb43053d5` -> `79bd14de40`.
  Backup `OBodySettings - Copy (40)`. Realism stays Draft, to be re-judged separately.
- **2026-09-14 (earlier)** -- **`D23` Muscular excludes Flat; Belly double-assignment policy settled.**
  Audit after `D22`: Belly carried 9 `Fat+Pregnant` rows and a cross-layer `Muscular+Flat` overlap.
  The user ruled Belly a **tag set** -- Fat+Pregnant stays dual and the `Very Fat` w100 triple is
  correct -- but Muscular must exclude Flat, so `Belly:Flat` gained `![Belly:Muscular]` (the guard
  `Chubby` already had). 0 rows move unseeded; 11520 drop Flat when the slider layer seeds Muscular.
  Belly `b69abe8512` -> `6c86c3730a`. Backup `OBodySettings - Copy (39)`. Logged `D24` (Realism's
  UnrealisticChest branches assume Belly is Flat or Normal, which a seeded category never is).
- **2026-09-13 (earlier)** -- **`D22` Belly:Pregnant depth floor.** The user reported 11 rows as wrongly
  Pregnant (the `Z-O-E` family + `CBBE Gen` w100); all reached it through group 1, which had no floor on
  belly protrusion. Appended `belly_proj_to_hip >= 0.41` AND `belly_projection_2 >= 5.4` to that group
  only. Pregnant 138 -> 127: exactly those 11 leave, 0 of the 127 keepers displaced, Realism / Shape
  unchanged, one accepted knock-on (`CBBE Gen` w100 Build Medium -> Powerful). Belly `65bb844ae5` ->
  `b69abe8512`. Backup `OBodySettings - Copy (38)`. Belly stays ✏️ Draft.
- **2026-09-13 (earlier)** -- **Shape -> 🟢 PROVISIONAL.** New status between Draft and Approved: the user
  evaluated the post-`D21` assignments and is satisfied, but Shape's many upstream dependencies argue
  against a terminal sign-off. Holds unless a preset's Shape moves while other rules are revised. Frozen
  the verdicts of all 25500 rows to `obody_arm_tools/shape_verdicts_2026-09-13.json` and added
  `check_shape_provisional.py` to diff against them; `obody_scan.py` now drift-checks `provisional`
  cells like `approved` ones. Shape hash `5b89a36bcd`.
- **2026-09-13 (later)** -- **`D21` Shape rework applied** to the live 3BA profile: hip-retuned cuts
  (0.592 / 0.618 / 1.115) made consistent across all five rules, `NOT [Shape:Inverted Triangle]` routing,
  hip-free IT gate (`waist_to_shoulder <= 0.5128` OR `ShoulderWidth:Wide`), `Ambiguous` default. Every
  cache row now gets exactly one Shape (the half-applied hand edit had left 6101 rows empty and 2173
  double). IT 2971 -> 2007. Shape `892a87f521` -> `5b89a36bcd`; Shape is tier 2 now (reads ShoulderWidth).
  Waist / Belly retunes computed but left for the user. Backup: `OBodySettings - Copy (37) - before Shape
  rules edit.json`.
- **2026-09-13** -- **Hips APPROVED.** The user reconfigured the hip measurement in-app
  (`hip_to_torso` ratio -> absolute `hip_width`; hip-side KV boxes now X +-23.49, Y 64.86-76.43) and cut
  Narrow < 25.5 / Wide > 30.3. Verified the cache's `hip_width` fingerprint matches the new definition, so
  the populations are on the re-boxed vertices: Narrow 882 / Normal 16291 / Wide 8327 of 25500.
  Hips `2ec438467f` -> `57873f5afc`. Logged `N2` (CBBE's hip boxes differ; the absolute cuts need
  re-verifying on CBBE) and `D20` (the scanner's hash ignores measurement / KV edits). **All edits were
  in the 3BA profile; CBBE was not touched** -- it is a backup copy awaiting deprecation by the transfer.
  Its Belly / Build / BustHeight hashes in the machine state were stale from an older paste and were
  simply refreshed; no action.
- **2026-09-07** -- **`D18` + `D19`, and Arms APPROVED.** The D17 fixtures did their job: the
  user judged `ClaudeTest_02` Thick vs the real `Dino_054_09` Powerful, and confirmed by superimposing
  them that the cap difference is below visual resolution -- the eye was reading the **bicep**. So the
  cap branch gained a bicep floor (`>= 0.4183`) instead of a hair's-breadth cap bar, and the D16 volume
  discontinuity was retired (400 -> 380). Then `D19` pruned 3 disabled groups + `arm_thickness_to_torso`
  (0 of 25500 rows changed). **Arms -> ✅ Approved** at the user's call: reviewed across the whole set,
  one miss found and fixed (D16), boundary re-tested with purpose-built fixtures, cell pruned.
  Spun out: the default-value margin score (`DEFAULT_MARGIN_SCORE_PLAN.md`, commit `c0e75c82`).
- **2026-08-30 (D17)** -- **`D17`**: stood up the `Claude Test Presets` MO2 mod for synthetic edge-case
  fixtures; batch 1 (6 presets) targets the band D16 left behind. Awaiting a Match Presets scan.
- **2026-08-30 (D16)** -- **`D16`**: user's Arms review across the whole set found **one** miss,
  `Dino_3BA_054_09_SlimMuscles_Weight_3` w100 (`Delt_Cap_Ratio` 0.4599 vs the 0.46 gate -- missed by 0.0001,
  while its own w50/w75 read Powerful). Fixed with a volume-aware cap branch rather than a flat threshold
  drop (9x wider safety window). **Exactly 1 row of 25470 changes.** Arms `b44f21b3ba` -> `36a8b54e85`.
- **2026-08-30 (D15)** -- **`D15`**: rules-only descriptor categories implemented as a real feature
  (`BodyShapeDescriptorShell.IsRulesOnly`), not a catalog omission -- omitting a category orphans its rules in
  the editor, which is why `Cup` was invisible for so long (`D1`, now resolved by adding Cup to the catalog).
  `ShoulderMorph` / `ShoulderWidth` / `BicepBulge` flagged. Build + 430 tests green. Documented in README and
  a new `OBody.DescriptorRulesOnly` tooltip.
- **2026-08-30 (D14)** -- **`D14`**: `Build` converted back to pure DescriptorRef form. New leaf
  `BicepBulge` [Normal | Pronounced] at `Arm_Bicep_Ratio >= 0.442`; `ShoulderWidth:Wide` reused as-is at
  31.5 (inside the judged rows' feasible window, so no re-cut and no descriptor/gate split needed).
  11/11 judged rows hold; 10 rows demote in the 31.2-31.5 sliver. Rules 68 -> 69; Build `47e750caae` ->
  `3c920599f1`. **Standing rule recorded: keep `Build` leaf-only; warn before ever breaking it.**
- **2026-08-30 (later)** -- **`D12` phase 2 applied.** The rescan had cleared the coverage gate; all four
  anchors reproduced the offline predictions to 0.00000. `ShoulderMorph` / `ShoulderWidth` leaf categories
  added, `Arms` regrouped so a pronounced deltoid cap lowers the volume floor 380 -> 330, `Build:Athletic`
  gained the `[ShoulderMorph:Muscular]` branch. Rules 65 -> 68; Arms `913dcd9c2f` -> `b44f21b3ba`, Build
  `87a1b3ac79` -> `47e750caae`. 83 rows change Arms, 0 gain a second value, movers exactly as expected.
  All 11 D13-judged rows re-verified under the combined profile. **Next: the user's in-app pass over both.**
- **2026-08-30** -- `D13` **applied**: shoulder-aware `Build:Powerful`. User judged 8 presets from a
  purpose-built (shoulder width x bicep bulge) grid; combined with the 2026-08-29 anchors the verdicts
  are **unfittable by any flat bicep bar** (Powerful at 0.4281 below Athletic at 0.4398), which is the
  first hard proof the shoulder axis is load-bearing. Branch 3 split into a wide-shoulder branch
  (`shoulder_width >= 31.2`) and a bulge branch (`Arm_Bicep_Ratio >= 0.442`); `Build:Athletic` lost its
  `![Thighs:Thick]` guard. 11/11 judged rows, Powerful 1428 -> 1075, all 353 demotions land on Athletic.
  Build hash `0349b4d0c5` -> `87a1b3ac79`. Also noted: the rescan has populated `Delt_Cap_Ratio` on all
  25470 rows, so **`D12` phase 2 is no longer coverage-blocked**.
- **2026-08-25** -- New review data: five `Dino_*_SlimMuscles_Weight_3` rows judged Powerful/Athletic on both
  Arms and Build. Root-caused to the unmeasured deltoid axis (`MuscleMoreArms_v2` = 100 on all of them);
  proved the bicep threshold cannot be retuned to fix it. Designed `Delt_Cap_Ratio` (AUC 0.999, 3.4x
  confounder margin) plus two new leaf categories `ShoulderMorph` / `ShoulderWidth` (`D12`). **Phase 1
  applied** -- KVs `L_DeltCap` v10559 + `L_DeltIndent` v10605 and the measurement are in the settings, rules
  untouched. Phase 2 (rules) staged and gated on the rescan.
- **2026-08-24 (latest)** -- Parser fix (F1) built and cache regenerated. Verified live: cache now matches
  float parsing. Judged set unchanged (31/31, anchors and midpoint identical); Arms/Build hashes unchanged.
  Populations moved only on the 1000 formerly mis-parsed presets: Athletic 2305 -> 2668, Medium 19348 ->
  18969, Powerful 535 -> 545, Thick 565 -> 568, Small 2717 -> 2720; Build rows carrying Athletic 1684 ->
  2018, Powerful 1404 -> 1428. Residual offline-vs-cache noise down to 5 rows (duplicate preset names).
- **2026-08-24 (later)** -- User rescanned; cache carries `Arm_Bicep_Ratio` on all 25470 rows. `apply_d9_rules.py`'s
  blanket tolerance gate failed (0.18925) -- diagnosed as the offline pipeline's out-of-range-slider blind spot,
  not a D9 fault (150 labels, 0 unexplained; hits the older measurements harder). Gate rewritten to assert
  coverage + anchors + zero judged-preset misses, then the swap was applied at the user's go-ahead. Arms hash
  71f7769e5a -> 913dcd9c2f; Build unchanged. In-app re-check 31/31 judged rows incl. Wrongii. New action item `D10`
  (replicate BodySlide's out-of-range slider handling). Arms/Build stay 🔧 Revise.
- **2026-08-24** -- Zeroed-base discovery (viewer zeroed space = BS Trainer `Zeroed Sliders - 3BA` NIF; offline
  pipeline now exact). `D9` KVs + `Arm_Bicep_Ratio` measurement applied; separates all 39 judged presets at
  threshold 0.4264. Rule swap staged in `apply_d9_rules.py`, pending the in-app rescan. `KV_AUTHORING.md` +
  memory written so future KV authoring is mechanical.
- **2026-08-23 (review)** -- User checked Arms:Powerful bottom-by-alpha: 3 Thick calls. Existing-metric ceiling
  is 7/8 (`FrontBack >= 0.975`); cleaner ratio candidates found offline. Nothing applied yet.
- **2026-08-23 (later)** -- `D8` applied at the user's request, extended with granular Arms values
  (Large -> Athletic / Powerful / Thick) and catalog entries. Arms hash -> 71f7769e5a (5 rules), Build ->
  0349b4d0c5 (8 rules). 65 rules total, no cycles. Review pending.
- **2026-08-23** -- User asked to split Powerful into Powerful vs Athletic (exemplars Dommy Mommy Thicc 100 /
  Valkyrie Shape 75). Proposed mass-based split (`D8`); nothing applied.
- **2026-08-22 (latest)** -- User rescanned (cache now carries `Arm_FrontBack_Ratio` for all 25470 rows).
  Threshold tuned 0.975 -> 0.970 from in-app anchors; Arms hash -> 9058fcd74f. Refreshed-cache check: Arms 22/23,
  Build 16/17; Large 2325 / Powerful 1992. User confirmed Bulked Up 2 (75) was already Powerful.
- **2026-08-22 (later)** -- `D7` applied to the settings JSON at the user's request: +`Arm_FrontBack_Ratio`,
  Arms:Large and Powerful(F) rewritten. Arms hash 04e320d681 -> fca6458bf1, Build e5c7dcc8fb -> b3ef60405b
  (counts unchanged, no cycles). Rescan + threshold tuning still pending.
- **2026-08-22** -- User reviewed Arms:Large on 23 presets. Slider-calibrated offline analysis ->
  proposed `Arm_FrontBack_Ratio`, re-scoped Arms:Large and Powerful(F) (`D7`). Arms, Build -> 🔧 Revise.
- **2026-07-14** -- Reframed to 3BA-first: CBBE marked Deferred (derive-by-transfer), added Transfer
  plan + nuance `N1`, switched glyphs to Unicode. No approvals yet.
- **2026-07-14** -- Tracker + `obody_scan.py` created and seeded from a live scan.

---

## Machine state

The scanner reads the JSON below to detect drift. When a 3BA cell is approved, its `status` becomes
`approved` and its `hash` is frozen to the value at approval; the scanner then flags any later change.
CBBE cells are `deferred` (parked until the transfer). Statuses: `notstarted` / `draft` / `review` /
`revise` / `approved` / `provisional` / `deferred`. Run `python obody_scan.py` and paste its "Suggested machine-state
block" here (it preserves approved baselines). Do not hand-edit hashes except when consciously
(re-)approving.

<!-- OBODY-SCAN-STATE:BEGIN -->
```json
{
  "CBBE": {
    "Arms": {
      "count": 3,
      "hash": "91401c3eb4",
      "status": "deferred"
    },
    "Build": {
      "count": 7,
      "hash": "2313c31147",
      "status": "deferred"
    },
    "BustHeight": {
      "count": 3,
      "hash": "cb4ba7e6c8",
      "status": "deferred"
    },
    "Butt": {
      "count": 4,
      "hash": "86db31ba8a",
      "status": "deferred"
    },
    "Hips": {
      "count": 3,
      "hash": "4482f3bf10",
      "status": "deferred"
    },
    "Realism": {
      "count": 6,
      "hash": "532020c394",
      "status": "deferred"
    },
    "Shape": {
      "count": 5,
      "hash": "892a87f521",
      "status": "deferred"
    },
    "Thighs": {
      "count": 3,
      "hash": "9785552e6f",
      "status": "deferred"
    },
    "Waist": {
      "count": 3,
      "hash": "477c824385",
      "status": "deferred"
    }
  },
  "CBBE 3BA": {
    "Arms": {
      "count": 5,
      "hash": "1a2a524462",
      "status": "approved"
    },
    "BellyShape": {
      "count": 2,
      "hash": "f989a4aeb5",
      "status": "approved"
    },
    "BicepBulge": {
      "count": 2,
      "hash": "9341bd6986",
      "status": "approved"
    },
    "Build": {
      "count": 8,
      "hash": "293e181f51",
      "status": "revise"
    },
    "BustHeight": {
      "count": 3,
      "hash": "0b2170926e",
      "status": "approved"
    },
    "BustSize": {
      "count": 7,
      "hash": "a574fefdc7",
      "status": "approved"
    },
    "Butt": {
      "count": 4,
      "hash": "916739853e",
      "status": "approved"
    },
    "Chest": {
      "count": 3,
      "hash": "92221f8130",
      "status": "approved"
    },
    "Hips": {
      "count": 3,
      "hash": "57873f5afc",
      "status": "approved"
    },
    "Realism": {
      "count": 6,
      "hash": "e2d7d58c70",
      "status": "draft"
    },
    "Shape": {
      "count": 5,
      "hash": "5b89a36bcd",
      "status": "provisional"
    },
    "ShoulderMorph": {
      "count": 1,
      "hash": "921369a744",
      "status": "approved"
    },
    "ShoulderWidth": {
      "count": 2,
      "hash": "7954e4e2c1",
      "status": "approved"
    },
    "Thighs": {
      "count": 3,
      "hash": "f03fecf044",
      "status": "approved"
    },
    "TrunkThickness": {
      "count": 5,
      "hash": "099b38ef05",
      "status": "approved"
    },
    "Waist": {
      "count": 3,
      "hash": "2fb18fceee",
      "status": "approved"
    }
  }
}
```
<!-- OBODY-SCAN-STATE:END -->
