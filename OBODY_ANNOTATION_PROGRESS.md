# OBody Body Type Profile -- Annotation Rule Progress

Cross-session tracker for hand-authoring the **default Body Type Profile annotation rules** that
ship with SynthEBD -- the measurement/region rules inside `BodyTypeProfiles[].Rules[]` that
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

- **Scope:** the profile `Rules[]` engine only. The separate `BodySlideClassificationRules`
  (raw-slider) layer is **not** tracked here (see `D3`) -- but note it holds the 3BA
  `Belly=Muscular` rule that has no CBBE equivalent (nuance `N1`).
- **Active profile:** **CBBE 3BA** -- every measurement, key-vertex and rule edit happens here.
  **CBBE is deferred** (⏳): the profile currently in the settings is a **backup copy of the
  pre-3BA-work rules, kept untouched and slated for deprecation** -- the transfer overwrites it
  wholesale (rules, measurements, key vertices). Its column below is a snapshot of that backup,
  not an independently maintained profile. Don't spend effort on CBBE cells now.
- **Source file:** `S:\Dev\MO2\mods\SynthEBD Portable - Patcher Dev\SynthEBD\Settings\OBodySettings.json`
- **Refresh (run at the start of every session):** `python obody_scan.py` from the repo root.
  It re-reads the settings and prints live rule counts + content hashes + the dependency map, and
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
  **Step 2 (the rules) is proposed, not applied:** run `obody_arm_tools/apply_d29_belly_rules.py`
  after the user's review. `D26` is subsumed by `D29`. Chubby / Fat / Pregnant are deliberately
  untouched, so `D25`'s open Chubby item survives into the new scheme unchanged.
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
| BicepBulge  | ✏️ 1 | ⏳ — | leaf; new (`D14`) -- `Arm_Bicep_Ratio >= 0.442`; feeds Build |
| Belly       | ✏️ 5 | ⏳ 5 | 3BA also feeds Realism; re-scoped to Thin/Flat/Average/Chubby/Fat/Pregnant by `D29` (step 1 applied, rules proposed) |
| BustHeight  | ✏️ 3 | ⏳ 3 | leaf |
| BustSize    | ✏️ 7 | ⏳ — | 3BA-only; CBBE gains it via transfer (`D2`) |
| Butt        | ✏️ 4 | ⏳ 4 | leaf |
| Chest       | ✏️ 3 | ⏳ 3 | 3BA reads `BustSize` (CBBE currently reads `Cup`; transfer switches it) |
| Cup         | ✏️ 10 | ⏳ 10 | leaf; added to `TemplateDescriptors` 2026-08-30 (`D1` resolved) |
| Hips        | ✅ 3 | ⏳ 3 | **APPROVED 2026-09-13.** leaf; now `hip_width` (absolute X span of the re-boxed hip-side KVs) -- Narrow < 25.5 / Wide > 30.3; see `N2`, `D20` |
| Realism     | ✏️ 6 | ⏳ 6 | 3BA also reads `Belly` |
| Shape       | 🟢 5 | ⏳ 5 | **PROVISIONAL 2026-09-13** (evaluated + accepted; re-investigate if any assignment moves -- `check_shape_provisional.py`). Reworked (`D21`): hip-retuned cuts, `NOT [Shape:Inverted Triangle]` routing, hip-free Inverted Triangle gate; now reads `ShoulderWidth` (tier 2); default `Ambiguous` |
| ShoulderMorph | ✏️ 1 | ⏳ — | leaf; new (`D12`) -- `Delt_Cap_Ratio >= 0.435`; feeds Build |
| ShoulderWidth | ✏️ 2 | ⏳ — | leaf; new (`D12`) -- apparent width, deltoid-contaminated |
| Thighs      | ✏️ 3 | ⏳ 3 | leaf |
| Waist       | ✏️ 3 | ⏳ 3 | leaf |
| Build       | 🔧 8 | ⏳ 7 | aggregator -- approve **last**; Powerful/Athletic split (`D8`); rules untouched by `D9` but populations moved with Arms |
| **Total**   | **69** | **55** | |

---

## Suggested approval order & re-review triggers (CBBE 3BA)

Derived from the `DescriptorRef` cross-references (a rule in category A that reads category B's
result). Lock **leaves first** so aggregators sit on stable ground; **`Build` last**.

- **tier 1 (leaves):** Arms, Belly, BustHeight, BustSize, Butt, Cup, Hips, Thighs, Waist (+ BicepBulge, ShoulderMorph, ShoulderWidth)
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

**What the transfer resolves automatically:** CBBE gains `BustSize` (`D2`), and CBBE's `Chest`
switches from reading `Cup` to reading `BustSize` (`D5`) -- both just fall out of adopting 3BA's rules.

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

### Belly
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
| **3BA** | ✏️ | 3 | Lifted, Typical, Sagging | `chest_projection`, `chest_sag_ratio` | -- |
| CBBE (defer) | ⏳ | 3 | Lifted, Typical, Sagging | `chest_projection`, `chest_sag_ratio` | -- |

**Notes / opinions:** _(to fill)_

### BustSize  (3BA-only; CBBE gets it via transfer)
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✏️ | 7 | Flat, Petite, Small, Medium, Large, Very Large, Enormous | `L_Breast_Vol`, `sternum_to_chest` | `!BustSize:{Flat,Petite}` |
| CBBE (defer) | ⏳ | -- | -- | -- | -- |

**Notes / opinions:** _(to fill)_ -- newest 3BA work (volume-driven). Feeds `Chest`.

### Butt
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✏️ | 4 | Flat, Normal, Round, Large | `butt_projection` | -- |
| CBBE (defer) | ⏳ | 4 | Flat, Normal, Round, Large | `butt_projection` | -- |

**Notes / opinions:** _(to fill)_

### Chest
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✏️ | 3 | Petite, Medium, Busty | (none -- pure DescriptorRef) | `BustSize:{Flat..Enormous}` |
| CBBE (defer) | ⏳ | 3 | Petite, Medium, Busty | (none -- pure DescriptorRef) | `Cup:{Flat..H}` |

**Notes / opinions:** _(to fill)_ -- 3BA derives Chest from `BustSize`; the transfer replaces CBBE's
`Cup`-based Chest with this (`D5`). Re-check Chest whenever you retune the BustSize buckets.

### Cup
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✏️ | 10 | Flat, A, B, C, D, DD, E, F, G, H | `chest_projection` | -- |
| CBBE (defer) | ⏳ | 10 | Flat, A, B, C, D, DD, E, F, G, H | `chest_projection` | -- |

**Notes / opinions:** _(to fill)_ -- **`D1`:** `Cup` isn't defined in `TemplateDescriptors` (works, but
the catalog doesn't list it). Currently byte-identical CBBE/3BA.

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
| **3BA** | ✏️ | 6 | Unrealistic, UnrealisticChest, UnrealisticChestLift, UnrealisticButt, UnrealisticThighs, UnrealisticWaist | `chest_projection`, `chest_sag_ratio`, `butt_projection`, `thigh_thickness_to_hip`, `waist_to_hip` | self + `Belly:{Thin,Average,Muscular}` (+ `Flat` once `D29` step 2 lands) |
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
| **3BA** | ✏️ | 3 | Skinny, Normal, Thick | `thigh_thickness` | -- |
| CBBE (defer) | ⏳ | 3 | Skinny, Normal, Thick | `thigh_thickness` | -- |

**Notes / opinions:** _(to fill)_

### Waist
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | ✏️ | 3 | Pinched, Neutral, Flat | `waist_to_hip` | -- |
| CBBE (defer) | ⏳ | 3 | Pinched, Neutral, Flat | `waist_to_hip` | -- |

**Notes / opinions:** _(to fill)_

### Build  (aggregator -- approve last)
| | Status | Rules | Values | Key measurements | Reads |
|--|--|--|--|--|--|
| **3BA** | 🔧 | 8 | Slight, Medium, Curvy, Chubby, Exaggerated, Powerful, Athletic | (none -- pure DescriptorRef) | Arms, Belly, Chest, Hips, Realism, Shape, Thighs, Waist |
| CBBE (defer) | ⏳ | 7 | (same 6 values) | (none -- pure DescriptorRef) | + also reads `Cup` |

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

---

## Session log

Append newest at the top. Keep entries short: date, what changed, what got approved.

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
    "Belly": {
      "count": 5,
      "hash": "e298d58022",
      "status": "deferred"
    },
    "Build": {
      "count": 7,
      "hash": "fff45621d3",
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
    "Chest": {
      "count": 3,
      "hash": "44ed689e88",
      "status": "deferred"
    },
    "Cup": {
      "count": 10,
      "hash": "9cf9a06a44",
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
    "Belly": {
      "count": 5,
      "hash": "81ef53de5f",
      "status": "draft"
    },
    "BicepBulge": {
      "count": 1,
      "hash": "4d741b847e",
      "status": "draft"
    },
    "Build": {
      "count": 8,
      "hash": "3c920599f1",
      "status": "revise"
    },
    "BustHeight": {
      "count": 3,
      "hash": "e30b6b72d9",
      "status": "draft"
    },
    "BustSize": {
      "count": 7,
      "hash": "2be5185a7a",
      "status": "draft"
    },
    "Butt": {
      "count": 4,
      "hash": "d182972532",
      "status": "draft"
    },
    "Chest": {
      "count": 3,
      "hash": "92221f8130",
      "status": "draft"
    },
    "Cup": {
      "count": 10,
      "hash": "9cf9a06a44",
      "status": "draft"
    },
    "Hips": {
      "count": 3,
      "hash": "57873f5afc",
      "status": "approved"
    },
    "Realism": {
      "count": 6,
      "hash": "d0fbbb2e91",
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
      "status": "draft"
    },
    "ShoulderWidth": {
      "count": 2,
      "hash": "7954e4e2c1",
      "status": "draft"
    },
    "Thighs": {
      "count": 3,
      "hash": "f03fecf044",
      "status": "draft"
    },
    "Waist": {
      "count": 3,
      "hash": "abbff6143d",
      "status": "draft"
    }
  }
}
```
<!-- OBODY-SCAN-STATE:END -->
