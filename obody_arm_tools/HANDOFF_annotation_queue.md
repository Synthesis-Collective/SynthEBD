# Handoff: annotation queue (sampling policy) + auto-advance

> **STATUS: implemented 2026-09-18 on branch `UIupdate`** (commits "Annotation queue: ...", phases
> 1-4). Tracker entry: `OBODY_ANNOTATION_PROGRESS.md` decision `D28`. Code:
> `SynthEBD/Classes_Core/Models/AnnotationQueuePolicy.cs` (pure ordering + tests),
> `.../OBody SubModels/VM_AnnotationQueue.cs`, `.../OBody SubViews/UC_AnnotationQueue.xaml`.
>
> Acceptance criteria 1-6 are implemented; 1, 2, 4 and 5 are argued from the code and the unit
> tests but have **not** been exercised on a real labelling run against a live corpus. Two
> deliberate departures from the text below, both agreed before implementation:
> * **Prefetch** needed a new public API. `VM_CharacterViewer.PrewarmIdentityAsync` was added to
>   `CharacterViewer.Rendering` (documented in `RENDERING_PIPELINE.md`), plus a `WeightCoherent`
>   run-coalescing option, because the viewer short-circuits its load on an unchanged NPC identity
>   and the preview NPC is chosen per weight slot.
> * **Rules-only categories** (`ShoulderWidth`, `ShoulderMorph`, `BicepBulge`) were invisible in the
>   annotation editor; `VM_BodyShapeDescriptorSelectionMenu` gained an `IncludeRulesOnly` flag so
>   they can be labelled. Distribution pickers still hide them.
>
> **Added after the handoff, on request:** a fourth sampling mode **List**, which serves exactly the
> slices named in a worklist (plain text or JSON, with an optional per-case note shown while
> judging) in the order written, ignoring every other sampling option; and the keyboard now binds
> **Space** to Commit and **Tab** to Skip alongside the original Enter / S.
>
> Section 6's out-of-scope items are untouched: `Suggest Rules` is still univariate and
> single-condition (`D27`), and no Belly rule was changed.

**Goal.** Raise the throughput of hand-labelling BodySlide presets in the OBody **Label-then-Suggest**
tab. Today the user picks rows one at a time out of a DataGrid and records verdicts in a spreadsheet
outside the app. We want: *"I'm working on the Belly descriptor — keep serving me presets, I pick
values, the next one loads automatically."*

This is a **UI/VM feature in the main WPF app** (`SynthEBD/`). It does not touch the patcher, the
measurement engine, or the rule evaluator.

---

## 1. Read these first

| What | Where |
|---|---|
| Repo conventions (MVVM, Fody, ReactiveUI, themes, progressive disclosure, tooltips) | `CLAUDE.md` |
| The annotation table (rows = preset x weight) | `SynthEBD/Classes_Core/ViewModels/OBody SubModels/VM_PresetAnnotationTable.cs` |
| The annotation editor (descriptor menu -> persisted annotation) | `SynthEBD/Classes_Core/ViewModels/OBody SubModels/VM_PresetAnnotationEditor.cs` |
| Row VM | `SynthEBD/Classes_Core/ViewModels/OBody SubModels/VM_PresetAnnotationRow.cs` |
| Persisted annotation model | `PresetAnnotation` in `SynthEBD/Settings/Settings_OBody/BodyTypeProfile.cs` |
| Tab layout / where new controls go | `SynthEBD/Classes_Core/Views/OBody SubViews/UC_BodyTypeProfileEditor.xaml` |
| Why the sampling policy matters (evidence) | `OBODY_ANNOTATION_PROGRESS.md`, decision `D25` |

**Context in one line:** a rule fitted only on boundary-sampled verdicts scored 66/70 in-sample and
**8/14** on a random draw. The sampling policy is not a nicety; it is what makes the labels
trustworthy.

---

## 2. What exists today

- `VM_PresetAnnotationTable.Rows` — one `VM_PresetAnnotationRow` per (preset, gender, weight) slice,
  built by `ScanAsync()` or rebuilt from the profile's shared measurement cache. Carries every
  measurement value as columns, with per-column visibility.
- `VM_PresetAnnotationTable.SelectedRow` — the single source of truth for "which slice am I editing".
- `VM_PresetAnnotationEditor` — watches `SelectedRow`, shows a `VM_BodyShapeDescriptorSelectionMenu`
  in annotation mode, and on every toggle writes through to the row **and** to
  `profile.PresetAnnotations` (add / update / prune-on-empty).
- `PresetAnnotation { PresetLabel, PresetGender, Weight, Descriptors : List<LabelSignature> }`,
  persisted in `OBodySettings.json` on the profile.

**Not present:** any ordering, filtering-to-unlabelled, queueing, sampling, or advance-to-next logic.

---

## 3. What to build

### 3a. `VM_AnnotationQueue` (new)

A queue that sits *beside* the table and yields the next slice to label. It must **not** reorder
`VM_PresetAnnotationTable.Rows` — that collection is also the measurement grid the user reads.
Expose the queue as its own ordered view over the same row VMs.

Inputs: the active profile, `AnnotationTable.Rows`, a target **Category** (e.g. `Belly`), and a policy.

Policies (enum + a small strategy per case, mirroring how `MeasurementSelectionAlgorithm` is
dispatched in `MeasurementDiscriminators.Score`):

1. **`Spread`** — stratified over the range of a chosen measurement. Bucket the candidate rows into
   N quantile bins of that measurement and round-robin the bins. This is the "start extreme, cover the
   whole range" mode; use it when a category has few or no annotations.
2. **`Uncertainty`** — rows closest to the current decision boundary for the target Category. Simplest
   defensible implementation: for each candidate, evaluate the profile's existing rules for that
   Category and rank by |measurement − threshold| on the rule's own terms, smallest first. If the
   Category has no rules yet, fall back to `Spread`.
3. **`Random`** — uniform random over candidates, with a **fixed seed surfaced in the UI** so a sample
   is reproducible and can be quoted in the tracker.

Plus a **`RandomFraction`** setting (default ~0.25): even in `Spread`/`Uncertainty`, serve that
fraction of slices from `Random`. This is the part that keeps the error estimate honest — do not make
it easy to set to 0 without a warning.

Required behaviours:

- **Skip already-annotated slices** for the target Category by default (toggle to include them, for
  re-judging).
- **Dedupe by measurement signature.** The corpus contains many byte-identical aliases (e.g.
  `SSBBW2` vs `S4rMs' (ThickXXX) SSBBW2`, `Curse of Eden` vs `Curse of Eden (Outfit)`). Labelling both
  wastes the user's time and double-weights that shape in training. Group by a rounded tuple of the
  profile's measurements and serve one representative; record the verdict against **all** members of
  the group, or note the alias set on the annotation.
- **Session counters** — served / labelled / skipped, and a per-value tally for the target Category.
- **Back** — return to the previous slice and edit it.

### 3b. Auto-advance

- A **Commit & Next** action that persists the current slice's descriptors and immediately loads the
  next queued slice into the viewer.
- Keyboard: digits `1..9` toggle the Nth value of the target Category, `Enter` = Commit & Next,
  `Backspace`/`Left` = Back, `S` = Skip. Follow the existing `InputBindings` pattern in
  `UC_BodyTypeProfileEditor.xaml` (see the Ctrl+S / Ctrl+L bindings already on the key-vertex and
  measurement panels).
- **Do not replace the checkbox menu with radio buttons.** Descriptor categories are *tag sets*:
  decision `D23` established that `Belly = Fat + Pregnant` is a legitimate dual assignment, and
  `Nero Preggo` legitimately carries both. Single-select would make correct labels unrepresentable.
  Digit keys toggle; `Enter` commits whatever is toggled.
- **Prefetch the next slice's mesh** while the user is deciding on the current one. Loading a preset
  into the viewer is the real throughput bottleneck, not the clicking. See `ScanAsync`'s
  per-iteration yield pattern for how the existing code keeps the UI responsive during bulk renders.

### 3c. Export (small, do it anyway)

A "Copy verdicts" / "Export verdicts" action writing the target Category's annotations as JSON, shape:

```json
{ "profile": "CBBE 3BA", "category": "Belly",
  "rows": [ { "preset": "...", "weight": 50, "value": "Chubby", "aliases": ["..."] } ] }
```

Match `obody_arm_tools/belly_verdicts.json`, which is the format the offline analysis already consumes.

---

## 4. Constraints and gotchas

- **Never write to `BodySlideSetting.BodyShapeDescriptorsByWeight`.** That is the classifier's
  Manual/Library seed path and is read by `BodySlideMeasurementEvaluator.CollectExternalDescriptors`;
  a seeded Category counts as *covered* and suppresses its default. Annotations belong in
  `profile.PresetAnnotations`, which no classifier reads (verified).
- **Do not have the queue trigger a rescan.** Rows can be rebuilt from the profile's shared measurement
  cache; rescanning 25 500 slices takes minutes.
- **UI conventions are enforced.** New controls need a `local:DocTooltip.Key` with a matching entry in
  `UiDocs` (a test enforces key parity), classification into the `[Use][Customize][Troubleshoot]`
  progressive-disclosure levels via `UiModeController`, and theme-safe styling — use SynthEBD's own
  pickers from `GUI_Aux/Controls/`, never Mutagen.Bethesda.WPF's.
- `CS4014` (unawaited Task) is an **error** in this repo. `.editorconfig` is UTF-8 + CRLF.
- Dispose ReactiveUI subscriptions via the VM's `CompositeDisposable` (`.DisposeWith(this)`).
- **Visual QA:** `SynthEBD.CLI ui-screenshot` renders assemblies from the **CLI's own output folder**,
  so rebuild `SynthEBD.CLI` before screenshotting or you will capture stale UI. Ask the user before
  launching the app — another session may be driving an in-game test.

---

## 5. Acceptance criteria

1. Selecting a Category and pressing **Commit & Next** repeatedly labels slices without touching the
   mouse, and each verdict lands in `profile.PresetAnnotations` and survives a settings save/load.
2. `Spread` visibly covers the chosen measurement's range; `Uncertainty` serves rows near the current
   rule boundary; `Random` with a fixed seed reproduces the same sequence twice.
3. `RandomFraction` > 0 genuinely interleaves random draws (assert in a unit test on the policy, which
   should be pure logic and testable without the UI — mirror `MeasurementDiscriminators`' split of
   pure scoring from VM wiring).
4. Alias groups are served once and the verdict propagates to every member.
5. Annotating does **not** change any classification: `Thin / Normal / Chubby / Pregnant / Fat`
   populations are identical before and after a labelling session.
6. No rescan is triggered by queue operations.

---

## 6. Out of scope

- Changing `Suggest Rules` (Phase 6). It can only emit single-condition OR branches and is unusable for
  Belly-shaped rules — see `D27`. Fixing that is a separate, larger job (multivariate synthesis +
  AND-group output + `DescriptorRef` guards).
- Any change to the Belly rules themselves.
