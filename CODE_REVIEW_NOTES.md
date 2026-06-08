# SynthEBD Code Review Notes

A running survey produced alongside a docstring pass over the SynthEBD main app
(branch `CodeReview`). The goal is **observation, not modification** — nothing in
here has been changed yet. Each entry flags a function/area that may be worth a
second look, so the codebase can later be modernized or corrected in a deliberate,
reviewable way.

## How to read this

Entries are grouped by subsystem, in the order they were reviewed (leaf-level
helpers first, building up to their callers). Each entry is tagged:

- 🔧 **(a) Modernize** — works, but could be expressed more cleanly/idiomatically
  (e.g. LINQ instead of manual loops, modern BCL APIs, less duplication).
- 🐞 **(b) Possible bug** — suspected logical error, edge case, or correctness risk.
  *Flag only — not yet verified or fixed.*
- 💭 **(c) Opinion** — a design/readability/architecture observation; subjective.

Severity within a tag is noted inline where it matters. File/line references are
clickable.

---

## ✅ Resolved (remediation pass)

Progress tracker for the behavior-fix pass that follows this catalogue (branch
`CodeReview`). Items are grouped by the commit that closed them.

### Bucket 1 · E1–E4 — cosmetic cleanup (no behavior change)

- **E1 — stray unused `using`s removed (7):** `PreRunValidation` & `FirstLaunch`
  (`static …AxHost`), `BodyShapeDescriptor` (`Synthesis.Bethesda.Execution.DotNet`),
  `VM_BodyGenConfig` (`System.Printing`), `FlattenedSubgroup` & `VanillaBodyPathSetter`
  (`DirectoryServices.ActiveDirectory`), `EBDScripts` (`Intrinsics.X86`).
- **E2 — dead statement:** `VM_SpecificNPCAssignment` `break; ;` → `break;`.
  *(The `Logger:230` / `PatcherSettingsSourceProvider` `;;` flagged in the notes were
  already clean — no `;;` remains anywhere in the repo.)*
- **E3 — dead writes removed:** `EnvironmentStateProvider` `OutputMod = null;` before
  `new`; `RecordGenerator` double self-assignment ×2; `MiscValidation.VerifyBodyGenAnnotations`
  unused `missingBodyGenMessage`/`messages` locals; `VM_SettingsModManager`
  always-true `if (this != null)` wrapper; `UC_Settings_General` unused `_isDragging`.
- **E4 — user-facing typos:** `EnvironmentStateProvider` "patha"→"path";
  `MiscValidation.VerifyBlankAttributes` unclosed `[` → `[…]`; `DefaultAttributeGroups`
  "Mildy"→"Mildly"; `Patcher` "seleections"→"selections" ×2; `RecordPathParser`
  "as an XXX" placeholder → "as an array/list (IReadOnlyList<dynamic>)".
- *Verification:* Release build 0 errors; `SynthEBD.Tests` 124 passed / 1 skipped /
  0 failed. Purely cosmetic — no new automated test applicable.
- ⚠️ **Carve-out:** the "Mildy"→"Mildly" rename was reverted out of this batch — it is
  not a cosmetic change (see **M1**). The other E4 typos stand.

### Bucket 1 · E5–E12

**Behavior-neutral (cosmetic / dead code / renames):**
- **E5 — malformed save-dialog filter labels (9 sites; notes said 4):** `"… files (.ext|*.ext"`
  → `"… files (*.ext)|*.ext"` in VM_BodySlideExchange (×2), VM_OBodyTrainerExporter,
  VM_LogDisplay, SettingsIO_AssetPack/BodyGen/Height, VM_BlockListUI,
  VM_SpecificNPCAssignmentsUI. (Dialogs already filtered correctly; only the label was wrong.)
- **E6 — dead never-assigned RelayCommands removed:** `VM_HeadPart.Clone`/`ToggleHide`
  (+ the abandoned commented-out Clone block), `VM_7ZipInterface.StartExtraction`,
  `VM_BodyGenGroupsMenu.RemoveTemplateGroup`. None were XAML-bound.
- **E10 — `MainModule` duplicate `VM_SpecificNPCAssignment` registration removed** (kept one).
- **E11 — `Patcher` `Uthgerd` debug stub removed.**
- **E12 — method-name typos renamed (+ call sites):** `DumpViewModeltoModel`→`DumpViewModelToModel`
  (VM_BodyShapeDescriptor + 2 callers), `VerifyOBodyTemplateJsonExits`→`…Exists`
  (MiscValidation + PreRunValidation).

**Behavior-changing (obviously-correct; not unit-testable — WPF VMs, manual-verify):**
- **E7 — `VM_AssetPresenter.UpdatePreviewImages` `&`→`&&`:** the bitwise `&` evaluated
  `!SourceChain.Contains(...)` even when `SourceChain` was null → NRE risk; now short-circuits.
- **E8 — `VM_AssetPack` dead FormKey null-checks → `.IsNull`:** dropped always-false
  `DefaultTemplateFK == null` at the gender check (`.IsNull` already covered it); changed
  always-true `DefaultTemplateFK != null` → `!DefaultTemplateFK.IsNull` in
  `CandidateTargetPathExists` (downstream `TryResolve` already guarded null, so benign).
- **E9 — `VM_CustomEnvironment`:** removed the dead hand-declared `PropertyChanged` that
  shadowed the Fody-woven base `VM` INPC; removed the redundant second `builder.Build()`
  (it rebuilt the env *without* the mod-listing transform and discarded the result).
- *Verification:* Release build 0 errors; `SynthEBD.Tests` 124 passed / 1 skipped / 0 failed.

### Bucket 2 · logical bugs

- **B1 — `BoolByProbability.Decide` off-by-one bias (fixed).** Replaced
  `new Random().Next(100) <= trueProbability` (`Next(100)` is 0–99, so for probability T it returned
  true for T+1 of 100 buckets → +1% per bucket: 0% was true ~1% of the time, 99% was always true; the
  integer draw also truncated fractional probabilities) with
  `Random.Shared.NextDouble() * 100.0 < trueProbability` — 0% never, 100% always, exact T% for
  integer/fractional T, thread-safe, no per-call allocation. Gates head-part randomization %
  ([HeadPartSelector.cs:307](SynthEBD/Patcher/Head%20Part%20Patching/HeadPartSelector.cs#L307)) and
  Mix-In inclusion probability ([AssetSelector.cs:1380](SynthEBD/Patcher/Asset%20Patching/AssetSelector.cs#L1380)).
  *Test:* new `BoolByProbabilityTests` (7 cases) — 0% never true, 99% not always true (old high-end
  off-by-one), 100% always true, 50/25/75/33.5% rates within tolerance over 200k trials.
  Suite 131 passed / 1 skipped / 0 failed.
- **B2 — `FlattenedAssetPack.GetSubgroupPositionString` off-by-one guard (fixed; was latent).** The guard
  `Source.Subgroups.Count >= index` admitted `index == Count`, then threw `ArgumentOutOfRangeException`
  indexing the list (valid range 0..Count-1; it also didn't guard negatives). Extracted a pure static
  `FormatSubgroupPosition(IReadOnlyList<AssetPack.Subgroup>, index, includeFormatting)` with correct bounds
  (`index >= 0 && index < Count`); the instance method delegates. The sole caller
  ([AssetSelector.cs:992](SynthEBD/Patcher/Asset%20Patching/AssetSelector.cs#L992), a log line) passes
  flattened-position indices that stay in range, so the throw was unreached — defensive correctness.
  *Test:* new `FlattenedAssetPackTests` (7 cases) — valid indices format "ID: Name" / " (ID: Name)";
  `index == Count`, out-of-range, negative, and null/empty list all return "" without throwing.
  Suite 138 passed / 1 skipped / 0 failed.
- **B3 — `BlockListHandler` per-head-part-type flags now OR-aggregate (fixed).** The context-chain merge
  OR-aggregated every top-level flag but the per-head-part-type loop had an `else { output[t] = false; }`
  that let a later plugin in the chain clear a per-type block set by an earlier one — so blocking e.g.
  "Eyes" for an NPC could silently stop working when another block-listed plugin in its override chain
  blocked Head Parts but not Eyes (failing *open*). Extracted a pure static
  `MergeBlockedPlugins(IEnumerable<BlockedPlugin?>)` (the resolver maps the chain → entries → calls it) and
  dropped the `else` so per-type flags OR-aggregate like the rest. Made `BlockListHandler` public for
  testing (matching the codebase's existing public test targets; no `InternalsVisibleTo` is configured).
  *Test:* new `BlockListHandlerTests` (5 cases) — later plugin doesn't clear an earlier per-type block,
  per-type OR across plugins, top-level OR, single-plugin pass-through, empty/null → nothing blocked.
  Suite 143 passed / 1 skipped / 0 failed.
- **B4 — `CombinationLog.FormatAssetPackStats` category list fixed (logging-only).** The four-category
  enumeration listed `MixInFemale` twice and `MixInMale` never, so the combination/assignment log's
  "Assignment Statistics" section omitted every male Mix-In pack and double-listed female Mix-In packs
  (NPC assignments unaffected — this only formats counts). Changed the last `.And(assetPacks.MixInFemale)`
  → `.And(assetPacks.MixInMale)`. No unit test by agreement: a real regression test needs heavy
  `FlattenedAssetPack`/`PatcherState` construction, and a concat-extract test would be theater (the bug is
  in the call-site arguments). Manual-verify via the combination log. Suite 143 / 1 skipped / 0 failed (no regression).
- **B5 — `VerboseLoggingNPCSelector` disallowed-attributes group source fixed (logging-only).** The
  verbose-logging NPC selector resolved its *allowed*-attributes group labels against
  `GeneralSettings.AttributeGroups` but its *disallowed* check used `OBodySettings.AttributeGroups`
  (copy-pasted from `OBodySelector`). Verbose logging is a General-settings feature, so both must use the
  General set; changed line 77 → `GeneralSettings.AttributeGroups`. Per the local-vs-General resolution
  architecture (now documented in CLAUDE.md), this usually resolved correctly anyway via the load-merge +
  the default-on `OverwritePluginAttGroups` toggle, but diverged when the toggle was off and OBody locally
  redefined a referenced group label. Fix-only (a real test is integration-level; the bug is a call-site
  argument). Suite 143 / 1 skipped / 0 failed (no regression).
- **B6a — `OBodySelector.FilterPresetsByPreferredDescriptors` descriptor priority now applied (fixed).**
  `priorities.OrderBy(x => x.Priority)` discarded its result, so the lexicographic narrowing of candidate
  BodySlide presets ran in arbitrary collection order and ignored asset-imposed descriptor priorities (on a
  conflict a weaker preference could override a stronger one). Now
  `priorities = priorities.OrderByDescending(x => x.Priority).ToList()` so the highest-priority descriptor
  narrows first.
- **B6b — `BodyGenSelector.ChooseMorphs` ForceIf priority now applied + a single combination (fixed).** The
  loop walked every ForceIf tier and selected from the full `availableCombinations` set each pass, so the
  ForceIf-matched tier was never preferred *and* an NPC with mixed ForceIf matches got morphs stacked from
  multiple combinations (one per tier). Now it selects ONE combination from the top tier
  (`prioritizedCombinations.First()`) and drops the outer loop. (When nothing ForceIf-matches, the single
  tier-0 group is the full set, so unaffected NPCs behave as before.)
- *Verification (B6):* Release build 0 errors; suite 143 / 1 skipped / 0 failed (no regression). Fix-only +
  manual-verify (deep in the selectors; the harness is assets-only) — confirm via the patcher report, which
  logs the imposed descriptor priorities and the chosen morphs.
- **B7 — `AssetSelector.CombinationAllowedBySpecificNPCAssignment` now enforces forced subgroup IDs (fixed).**
  The Primary and MixIn cases looped over the forced ids but the body
  (`!selectedCombination.ContainedSubgroups.Select(x => x.Id).Any()`) ignored each `id` and only checked the
  combination was non-empty — so a pre-determined (consistency / linked-group) combination was accepted even
  when it lacked a forced subgroup, and a user's explicit "force this NPC to wear X" lost to a stale
  consistency/linked combo. Extracted a pure `CombinationContainsForcedSubgroups(combinationIds, forcedIds)`
  (forced ids are a subset that must all be present; null/empty = no constraint), used in Primary + MixIn
  (Replacer's exact per-index match was already correct). *Test:* new `AssetSelectorTests` (4 cases) — all
  forced ids present → true, a missing forced id → false (the regression), no forced ids → true, forced id
  vs empty combination → false. Suite 147 / 1 skipped / 0 failed.
- **B8 — `AssetAndBodyShapeSelector.ClearStatusFlags` removed (redundant no-op, not a behavior bug).** The
  method was a complete no-op (by-value param + `flags = ~X` overwrites), but tracing its only two callers
  (OBodySelector / BodyGenSelector) showed each sets `statusFlags = new BodyShapeSelectorStatusFlag()` (= 0)
  on the line *before* the call and then ORs flags in afterward — so even a working version would only set
  0→0. Removed the method and both call sites (behavior-preserving dead-code removal). No test applies;
  suite 147 / 1 skipped / 0 failed (no regression).
- **B9 — `HeadPartSelector` duplicate condition + dead FormKey null compares (fixed).**
  - *B9a:* the `else if` at :199 repeated the preceding `if`'s condition, so the warning "Specific NPC
    Assignment … calls for {FormKey} but this head part does not currently exist in the load order" never
    fired. Changed the `else if` to `ResolvedHeadPart == null` (diagnostic-only; the branch only logs).
  - *B9b:* `ResolveConflictWithAssetAssignment` compared `FormKey` (a struct) to `null` (always false/true),
    so the "one source assigned, the other didn't" early-outs were dead — when one source assigned a real
    head part and the other an explicit `FormKey.Null`, the conflict-winner switch could return the null and
    drop the real assignment. Extracted a pure static `ResolveHeadPartConflict(assetFK, menuFK, winner)` using
    `.IsNull` (a real assignment beats a null one regardless of the winner); the instance method passes
    `SourceConflictWinners[type]`. *Test:* new `HeadPartSelectorTests` (6 cases) — asset-real/menu-null →
    asset and menu-real/asset-null → menu (both regardless of winner; the regression), plus both-real
    honoring each winner. Suite 153 / 1 skipped / 0 failed.
- **B10 — `VanillaBodyPathSetter` `&&`/`||` precedence — verified NOT a bug (false positive).** The condition
  gates a *diagnostic warning only* (no behavior) that fires when an NPC has no appearance-override mods yet is
  getting the vanilla/race body. "No override mods" == base master (+ SynthEBD's own output override, which is
  added only in non-SkyPatcher mode via `GetOrAddAsOverride`; SkyPatcher uses `ApplySkin`/ini and adds none),
  so no-override == Count 2 (non-SkyPatcher) or Count 1 (SkyPatcher). The current
  `(!SkyPatcher && Count==2) || Count==1` is correct in all six (mode × count) cases and warns in both modes;
  the notes' suggested bracket `!SkyPatcher && (Count==2 || Count==1)` would have *suppressed* the warning for
  every no-override NPC in SkyPatcher mode. Left the logic unchanged and documented it in-code so it isn't
  re-flagged. No behavior change.
- **B11 — `RecordPathParser` numeric array-index bounds guard ×2 (fixed).** In `GetArrayObjectAtIndex` and
  `GetArrayObjectCollectionAtIndex`, the guard `iIndex < 0 || iIndex < collectionObj.Count()` gates the
  `ElementAt(iIndex)` fetch arm (vs the graceful "no element at this index" else arm). A **negative** index
  made `iIndex < 0` true → fell into the fetch arm → `ElementAt(negative)` threw an unhandled
  `ArgumentOutOfRangeException` (a record path like `Armature[-1]` aborts the run/UI op instead of logging and
  returning false). Changed both to `iIndex >= 0 && iIndex < collectionObj.Count()`. *Test:* added
  `NumericArrayIndex_OutOfRange_FailsGracefully` to `SetViaFormKeyReplacementTests` — `[-1]` and `[5]`
  (out-of-range) return false without throwing, `[0]` still resolves. Suite 154 / 1 skipped / 0 failed.
- **B12 — `RecordPathParser.RemovePairedParens` made depth-aware (hardening; the notes' active example was a
  false positive).** The helper strips redundant enclosing parens from one array-condition term before the
  comparator is split out. It naively stripped first/last chars whenever `StartsWith('(') && EndsWith(')')`,
  ignoring whether they were a matched pair. *Verify finding:* the notes' `(a) && (b)` never reaches it —
  `GetConditionsFromString` pre-splits on every `&`/`|` char (`input.Split({'|','&'})`), so terms arrive as
  `(a) ` / ` (b)`. So the corruption was latent (only an unusual single term like `(x).Foo(y)` could trigger
  it). `TrimParens` (the depth-aware sibling) does NOT replace it — it removes a *stray unmatched* paren, not
  a redundant *enclosing* pair — so the helper was hardened rather than removed: it now strips the outer pair
  only when the leading `(` closes at the final char (quote-aware, matching `TrimParens`). Fix-only (a real
  test means a heavy public-API condition path; the method is private inside a private nested class).
  Suite 154 / 1 skipped / 0 failed.
- **B13 — `MiscFunctions.StringHashSetsEqualCaseInvariant` deleted; caller inlined a sound comparison.** The
  helper mixed a case-sensitive count check with case-insensitive (LINQ, O(n²)) membership, so case-variant
  duplicate entries could compare equal to a differently-populated set. The sole caller — `SelectRecordType`
  ([AssetReplacerSelector.cs:119](SynthEBD/Patcher/Asset%20Patching/AssetReplacerSelector.cs#L119)), matching a
  replacer's target file-path set against the hardcoded `ReplacersByPaths` specifiers — legitimately wants
  case-insensitive matching (Windows paths). Per request, removed the helper and inlined
  `new HashSet<string>(targetPaths, StringComparer.OrdinalIgnoreCase).SetEquals(specifier.Paths)` (sound, O(n),
  same result for clean path sets). No unit test (the inlined BCL `SetEquals` is trusted; the custom helper is
  gone). Suite 154 / 1 skipped / 0 failed (no regression).
- **B14 — `PatchableRaceResolver` dual collections collapsed to one (fixed; was a real drift bug).** The
  resolver held the patchable races as two hand-synced collections — `PatchableRaces`
  (`HashSet<IFormLinkGetter<IRaceGetter>>`) and `PatchableRaceFormKeys` (`HashSet<FormKey>`).
  `ResolvePatchableRaces` reset `PatchableRaces = new()` each call but only ever **added** to
  `PatchableRaceFormKeys`, so on any 2nd+ call (it runs at [SaveLoader.cs:97](SynthEBD/SaveLoader.cs#L97) at
  load *and* at the top of every [Patcher.cs:279](SynthEBD/Patcher/Patcher.cs#L279) `RunPatcher`) stale
  FormKeys accumulated while the form-link set rebuilt fresh — the two drifted. Symptom: de-select a race in
  General Settings, then run; `PatchableRaceFormKeys` (a stale superset) still gates asset/body/height/headpart
  assignment ([Patcher.cs:1032…1213](SynthEBD/Patcher/Patcher.cs#L1032)) so the de-selected race keeps getting
  patched, while `VanillaBodyPathSetter` (using the fresh form-link set) treats it as not-patchable — the two
  halves of the pipeline disagree on the same NPC. **Per the user's call, collapsed to a single
  `HashSet<FormKey>`** (the form-link representation was load-bearing nowhere): `ResolvePatchableRaces` now
  assigns it wholesale (`CompilePatchableRaces(...).Select(r => r.FormKey).ToHashSet()`) so drift is impossible
  by construction; `CompilePatchableRaces` modernized (seed the set from the explicit list, `UnionWith` the
  groupings, drop the redundant `Contains`-before-`Add` guards). Consumers updated: `VanillaBodyPathSetter`
  (line 63 → `.FormKey`; `InitializeDefaultMeshPaths` iterates FormKeys, compares `armaGetter.Race.FormKey`),
  and the two `RecordPathParser` `PatchableRaces.Contains(...)` evaluator sites (convert the comparison object
  to `.FormKey` instead of `.ToLinkGetter<IRaceGetter>()`, feed `PatchableRaceFormKeys`). `CompilePatchableRaces`
  still returns `HashSet<IRaceGetter>` for `ApplyRacialSpell` (needs the records). *Test:* new
  `RecordPathParserEvalTests` (3 cases) — made `EvalBoolExpression` public and pinned the exact expression the
  collapsed site builds (`"_1.Contains(_0)"` over `[FormKey, HashSet<FormKey>]`): member present → true, absent
  → false, negated → false. This proves DynamicExpresso still resolves `HashSet<FormKey>.Contains(FormKey)` and
  that FormKey equality holds through the interpreter after the type change. Suite 157 / 1 skipped / 0 failed.
- **B15 — `_7ZipInterface` dead corrupt-archive check (live) + unconditional `BeginOutputReadLine` (latent) (both fixed).**
  Two defects in the 7-Zip wrapper. **(1, live):** `GetArchiveContents` declared a `StringBuilder
  standardOutputCapture` but its `OutputDataReceived` handler appended each stdout line to `outputLines`
  instead, so `outputStr = standardOutputCapture.ToString()` was always `""` and the
  `Contains("Can't open as archive")` corrupt-archive check was unreachable — listing a truncated/corrupt
  archive in the Config Drafter ([VM_ConfigDrafter.cs:511](SynthEBD/GUI_Aux/ViewModels/VM_ConfigDrafter.cs#L511))
  silently returned an empty content list instead of raising the "File Extraction Error" dialog. Fixed by
  reading the lines actually captured (`outputLines.Any(x => x.Contains("Can't open as archive"))`) and
  dropping the never-populated `standardOutputCapture` from that method. **(2, latent):** `ExtractArchive`
  set `RedirectStandardOutput` only when `mirrorUIstr != null` but called `process.BeginOutputReadLine()`
  unconditionally, so a null callback would throw `InvalidOperationException: StandardOut has not been
  redirected` → caught → misleading "extraction failed" dialog (and the handler's `mirrorUIstr(e.Data)` would
  NRE). No current caller passes null — both routes go through `VM_7ZipInterface` (non-null `AddToScreen`)
  and the no-callback overload passes `(_) => {}` — so this is defensive hardening: the handler subscription
  and `BeginOutputReadLine()` are now gated on `mirrorUIstr != null`, mirroring the existing redirection guard.
  No unit test: the class shells out to the bundled `7z.exe` and parses its stdout, so a real test needs the
  binary plus a corrupt-archive fixture (integration-level) and a mocked `Process` would be theater. Manual-verify.
  Suite 157 / 1 skipped / 0 failed (no regression).
- **B16 — `MiscValidation` ini-parse error gated its line locator on the wrong out-variable (fixed; log-only).**
  In `VerifyRaceMenuIniForBodyGen` / `VerifyRaceMenuIniForBodySlide`, the "Could not parse X" branches for
  `bEnableBodyGen` and `iScaleMode` appended the "( in line …)" locator only `if (morphLine.Any())` while
  printing `genLine` / `scaleLine` — the guard was copy-pasted from the morph block. Diagnostic-only (`valid`
  is set false regardless), but the locator's presence was coupled to an unrelated setting: a malformed
  `bEnableBodyGen=` whose hint we *know* was suppressed when `bEnableBodyMorph` was absent, and a blank
  "( in line: )" was emitted when `bEnableBodyGen` was absent but `bEnableBodyMorph` present. Surfaces in
  pre-run validation messages ([PreRunValidation.cs:92-96](SynthEBD/General_Aux/PreRunValidation.cs#L92)) for a
  user troubleshooting why BodyGen/BodySlide won't run. Rather than swap the three guards, extracted a pure
  `public static string AppendIniLineReference(string message, string line)` that takes the gated value as one
  parameter so the guard and appended text cannot diverge, and routed all five sites (the two correct morph
  branches included) through it — structurally killing the mismatch bug class. Per request, the locator now
  reads "( in line: <text>)" (added a colon so a blank/odd line stands out). *Test:* new `MiscValidationTests`
  (2 cases) — known line appends the colon-form locator; empty line returns the message unchanged. (The full
  methods aren't unit-testable as-is: they read a real ini via `_raceMenuHandler` and log; the I/O isn't where
  the bug lived.) Suite 159 / 1 skipped / 0 failed.
- **B17 — `BSAHandler.ReferencedPathExists` (candidate-mods overload) never set `modName` (fixed; log-only).**
  The overload initialized `modName = ""` and never reassigned it, so callers got an empty mod name even on a
  hit — unlike the single-path overload, which sets it. The only consumer is
  [AssetPackValidator.cs:252-262](SynthEBD/Classes_Core/Models/AssetPackValidator.cs#L252): when an asset path
  isn't on disk and isn't in a path-prefixed mod's BSA but *is* covered by a subgroup's `AssociatedBsaModKeys`
  BSA that lacks the file, the validation error appended `"… or any BSA archives corresponding to " +
  specifiedModName` — a trailing blank instead of the mod name. Diagnostic-only (the boolean result and patch
  behavior are unaffected; the other caller discards the out-param). Fixed by accumulating the candidate mods
  whose BSA was opened and assigning `modName` = the matched mod on a hit, else the comma-joined list of
  candidates that had a BSA (matching what `specifiedArchiveExists` reports). No unit test: the overload does
  real BSA I/O (`TryOpenCorrespondingArchiveReaders` → `Archive.GetApplicableArchivePaths` against a live Data
  folder + `ReadersOrDeferredHaveFile`) with no pure seam to extract, and a mocked-BSA test would assert the
  mock; an integration test is disproportionate for a diagnostic-string fix. Manual-verify. Suite 159 / 1
  skipped / 0 failed (no regression).
- **B18 — `SettingsIO_BodyGen` female attribute-group merge iterated `Male` (fixed; real distribution bug).**
  The load-time General→local attribute-group merge ran as two loops (BodyGen configs are gender-split), but the
  "female" loop read `foreach (var femaleConfig in loadedPacks.Male)` — iterating `Male` again. So male configs
  were merged twice (idempotent via the `Contains(label)` guard) and **female** configs never got General
  attribute groups seeded into their local set. Per the local-vs-General resolution design (CLAUDE.md), with
  `OverwritePluginAttGroups` **off**, a female BodyGen morph rule referencing a General-only attribute-group
  label (e.g. "Vampires" defined in General but not in the config file) resolves against the config's local
  `AttributeGroups` via `GetAttributeGroupByLabel` → not found → the rule mis-gates and the female NPC gets the
  wrong/default morph. (Masked when the default-on toggle is on, which returns the General entry directly.)
  Extracted the merge body into a pure `public static AddMissingAttributeGroups(ICollection<AttributeGroup>
  target, IEnumerable<AttributeGroup> generalGroups)` and ran it over `loadedPacks.Male.Concat(loadedPacks.Female)`
  in one loop, so there is no second collection to mis-name. *Test:* new `SettingsIO_BodyGenTests` (3 cases) —
  a missing label is added as an independent copy (new group + new `Attributes` set, so local edits can't mutate
  General); an existing label is not duplicated and the local instance is preserved (local-wins); an empty
  General set leaves the target unchanged. (`LoadBodyGenConfigs` itself isn't unit-testable — reads JSON from
  disk + needs `PatcherState`/logger — but the I/O isn't where the bug lived.) Note: the same 3-line merge
  appears in `SettingsIO_AssetPack`/`_OBody` (each a single, correct loop) — a future R-item could dedup all
  three onto this helper. Suite 162 / 1 skipped / 0 failed.
- **B19 — `SettingsIO_Misc.LoadUpdateLog` fallback checked the consistency backup, not the update-log backup (fixed).**
  The update-log fallback branch's guard tested `File.Exists(_paths.GetFallBackPath(_paths.ConsistencyPath))`
  while its body loaded `_paths.GetFallBackPath(_paths.UpdateLogPath)` — it checked one file and loaded another
  (copy-paste from `LoadConsistency`). So the update-log fallback fired based on whether the *consistency* backup
  existed: when a user upgrading had an update-log backup but no consistency backup, the existing update-log
  fallback was never loaded → `LoadUpdateLog` returned an empty `UpdateLog` → `UpdateHandler` believed no
  migrations had run and could re-apply already-applied version migrations (the M1 double-migration hazard);
  conversely, when only the consistency backup existed, it attempted to load a missing update-log fallback and
  logged a spurious "Could not load Update Log." Extracted the primary/fallback selection into a pure
  `public static string? SelectExistingPath(string primary, string fallback, Func<string,bool> exists)` that
  ties the existence check and the returned path to the same arguments, and routed **both** `LoadUpdateLog` and
  `LoadConsistency` through it (the consistency loader was already correct; this protects it too and dedups the
  near-identical dance). Also fixed an adjacent cosmetic copy-paste: `SaveUpdateLog`'s failure status message
  named `_paths.ConsistencyPath` instead of `UpdateLogPath`. *Test:* new `SettingsIO_MiscTests` (4 cases, injected
  `exists` predicate — no disk) — primary exists → primary; primary missing + fallback exists → fallback; neither
  → null; both exist → primary. The `Load*` methods stay I/O-bound (real `_paths`/JSONhandler), but the
  path-selection where the bug lived is now pure and covered. Suite 166 / 1 skipped / 0 failed.
- **B20 — default race aliases duplicated CotR Imperial and omitted CotR Imperial Vampire (fixed; + migration).**
  Two default-data lists — `Settings_General.RaceAliases` (fresh-install defaults) and `UpdateHandler`'s
  `cotrRaceAliases` (the 1.0.4.8 CotR backfill set) — listed `DefaultRaceAliases.RaceAliasCotR_Imperial` twice
  and never `RaceAliasCotR_ImperialVampire` (distinct: source `05A17A:COR_AllRace.esp` → `ImperialRaceVampire`,
  vs Imperial's `05A179` → `ImperialRace`). Every other CotR race pairs a base + `_Vampire` alias; only the
  Imperial pair was broken. Effect (real distribution): NPCs of the CotR Imperial Vampire race were never aliased
  to vanilla `ImperialRaceVampire`, so the patcher skipped them (no appearance randomization) while every other
  CotR variant was handled. The 1.0.4.8 backfill couldn't add it either — `05A17A` is in its `cotrRaceStrs` but
  the lookup into `cotrRaceAliases` returned null (the set lacked Imperial Vampire). Fixed both lists (the second
  Imperial → Imperial Vampire, which also restores alphabetical order). **Migration:** added a version-gated
  `UpdateV1070RaceAliases` (dispatched `if (appliedVersion < "1.0.7.0")`) that silently repairs existing settings
  files — for users who have the CotR Imperial alias (so CotR support a user removed isn't reintroduced), it drops
  redundant duplicate Imperial entries and adds the missing Imperial Vampire alias to `_generalVM.raceAliases`
  (mirroring the 1.0.4.8 VM-based pattern). Idempotent. **Version not bumped** (currently 1.0.6.9) per the
  release convention — the bump to 1.0.7.0 is the release commit, at which point the migration fires for all
  pre-1.0.7.0 users. *Test:* new `DefaultRaceAliasesTests` (3 cases) — reflect every `RaceAliasCotR_*` field and
  assert all appear in `new Settings_General().RaceAliases` (catches the omission + future ones), the default
  source `Race` FormKeys are unique (catches the duplicate), and Imperial Vampire is present specifically. The
  migration itself is manual-verify (needs the heavy `VM_Settings_General`); the root-cause static data is the
  tested part. Suite 169 / 1 skipped / 0 failed.
- **B21 — TexMesh Batch Actions "Add as Allowed/Disallowed Attribute" ignored the checkbox selection (fixed).**
  Both apply commands iterated `foreach (var assetPack in AssetPacks)` and wrote the cloned attribute rule into
  *every* asset pack's `Allowed`/`DisallowedAttributes`, never reading `assetPack.IsSelected`. Confirmed against
  the view ([Window_TexMeshBatchActions.xaml](SynthEBD/Settings/Settings_TexMesh/TexMeshBatchActions/Window_TexMeshBatchActions.xaml)):
  the left-panel checkboxes bind `IsChecked={Binding IsSelected}` and the Select/Deselect-All buttons toggle the
  same flag — so the entire selection UI (per-pack checkboxes + Select/Deselect All) was inert; the only reader of
  `IsSelected` was the post-apply cleanup that unchecks everything. Symptom: a user checking 4 of 9 packs and
  clicking "Add as Disallowed" silently wrote the rule into all 9 configs, corrupting the 5 they never selected.
  Fixed by scoping both loops to `AssetPacks.Where(x => x.IsSelected)`; applying with nothing checked is now a
  no-op instead of writing to all. Fix-only + manual-verify: the apply logic is inline `RelayCommand` lambdas in
  the constructor bound to heavy collaborators (`_attributeCreator`, `VM_AssetPack.DistributionRules`,
  `VM_NPCAttribute.CloneInto`); there's no pure seam, and a VM/wrapper-mock test would exercise framework plumbing
  rather than the one-line selection bug. Verified via the Batch Actions window. Suite 169 / 1 skipped / 0 failed
  (no regression). (The sibling `VM_SelectableSubgroupShell` INPC/IsSelected concern is tracked separately as B31.)
- **B22 — `NPCAttribute*.CloneAsNew` dropped `Not`, shared collections by reference, Misc dropped mood/aggression/gender (fixed).**
  All 11 typed `CloneAsNew` factories failed to copy the `Not` negation (part of every type's `Equals`/`GetHashCode`
  and the `ITypedNPCAttribute` contract); 9 of them assigned the `FormKeys`/`ModKeys`/`SelectedLabels` collection
  *by reference* (`output.FormKeys = input.FormKeys`), so the "clone" aliased the source set; and
  `NPCAttributeMisc.CloneAsNew` copied `EvalMood`/`EvalAggression` but not the `Mood`/`Aggression` values, nor
  `EvalGender`/`NPCGender`. These clones run in the **patcher** path — `AttributeWeightModifier.CloneAsNew` →
  `NPCAttribute.CloneAsNew`, invoked during subgroup flattening of probability-weight modifiers
  ([FlattenedSubgroup.cs:42](SynthEBD/Patcher/Internal%20Data%20Structures/FlattenedSubgroup.cs#L42),
  [BodyGenSelector.cs:710](SynthEBD/Patcher/BodyGen%20Patching/BodyGenSelector.cs#L710),
  [AssetPack.cs:94](SynthEBD/Classes_Core/Models/AssetPack.cs#L94)). Effect: a weight modifier with a negated
  attribute (e.g. "weight ×0.1 if NOT Nord") flattened to the **opposite** population ("if Nord"); a Misc-based
  modifier ("if Mood = Angry" / "if Female") lost its value (reset to `Neutral` / gender check dropped); and the
  flattened clone's collection aliased the source subgroup's. Fixed all 11: copy `Not`, deep-copy every
  collection (`new HashSet<…>(input.…)`), and in Misc copy `Mood`/`Aggression`/`EvalGender`/`NPCGender` (plus
  `ReferenceNPCFK`/`SelectedFormKeyType` in Custom for UI-state fidelity); refreshed the now-stale per-method doc
  comments. *Test:* new `NPCAttributeCloneTests` (7 cases) — `Not` preserved for every type via the dispatcher;
  FormKey/ModKey/label-set independence (mutate clone, original unchanged); Custom Record `ValueFKs`
  independence; Misc copies mood/aggression/gender (non-default members chosen reflectively); and the
  `CloneAsNew(NPCAttribute)` dispatcher preserves a negated sub-attribute. Suite 176 / 1 skipped / 0 failed.
  (The broader generic-base dedup for the `NPCAttribute*` family remains a separate 🔧 R-item.)
- **B23 — `NPCAttribute` equality order-dependent + family `Equals`/`GetHashCode` inconsistency (fixed; + comparer removal).**
  `NPCAttribute.Equals` compared the `SubAttributes` `HashSet` **positionally** (`ToArray()` then index-by-index),
  so two equal attributes whose sub-attributes enumerated in different orders compared unequal — making
  attribute dedup/containment in `AllowedAttributes`/`DisallowedAttributes` sets unreliable. Deeper: each typed
  `Equals` ignored fields its `GetHashCode` *included*, so the two disagreed. Tracing every ignored field through
  `AttributeMatcher` split them: **`ForceMode`/`Weighting`** are distribution modifiers (applied after a match —
  not match criteria), so ignoring them in `Equals` was defensible; but **`Comparator`** (Custom — `==` vs `!=`
  match opposite NPCs), **`ModActionType`** (Mod — CreatedBy vs PatchedBy), and **`EvalGender`/`NPCGender`** (Misc)
  *are* match criteria, so `Equals` wrongly reporting them equal was a latent bug (masked only because the
  over-inclusive hash kept genuinely-different attributes in different `HashSet` buckets). Per the user's call,
  did the **all-fields audit**: each typed `Equals` now compares exactly what its hash includes (the safe
  direction — bringing `Equals` *up* to the hash, not stripping the hash down to the buggy `Equals`, which would
  have activated the bug and risked dedup data loss). Added `object.Equals(object)` overrides to all 11 typed
  classes (so the family has consistent value equality and `HashSet<ITypedNPCAttribute>` dedups by value), and
  reimplemented `NPCAttribute.Equals` via `SubAttributes.SetEquals`. Folded in the **deletion of
  `FormKeyHashSetComparer`/`ModKeyHashSetComparer`** (user goal): `Equals`→`SetEquals`, the duplicated
  `ComparableSetHashCode`→one generic `NPCAttribute.OrderIndependentHash<T>` (value-identical), and the 12
  `Contains` callers→BCL `HashSet.Contains`. *Test:* new `NPCAttributeEqualityTests` (7 cases) — the
  `{Race[Nord,Orc],Keyword[Vampire]}` example equal across outer+inner reordering with equal hash; sub-attribute
  inequality; Comparator/ModActionType/gender now distinguished; `HashSet` value-dedup via `object.Equals`;
  different-type inequality. Split across commits: B23 audit, then comparer removal. Suite 184 / 1 skipped / 0 failed.
- **B24 — `NPCAttributeCustom.GetHashCode` NRE on null `Comparator` (fixed).** `Comparator` has no initializer
  (defaults null, unlike the other `= ""` string fields), but `GetHashCode` called `Comparator.GetHashCode()`
  unguarded — so hashing a freshly-constructed Custom attribute (new in the UI before a comparator is picked, or
  deserialized from older JSON) threw an NRE when added to a `HashSet`/`Dictionary`. Changed to
  `(Comparator?.GetHashCode() ?? 0)`. *Test:* new `NPCAttributeCustomTests` — `GetHashCode` on a default Custom
  attribute does not throw. Suite 184 / 1 skipped / 0 failed.
- **B25 — `NPCAssignment.SubgroupIDs` defaulted null → NRE importing zEBD assignments with forced subgroups (fixed).**
  `SubgroupIDs` defaulted to `null` (unlike the sibling `AssetReplacerAssignment`/`MixInAssignment` lists, which
  default `new()`), but `ToSynthEBDNPCAssignments` does `new NPCAssignment()` then `s.SubgroupIDs.Add(zFS.id)` for
  every forced subgroup — so importing any legacy zEBD specific-NPC assignment that forced subgroups threw a
  `NullReferenceException` and aborted the import. The same null default was a latent NRE at
  [AssetSelector.cs:927](SynthEBD/Patcher/Asset%20Patching/AssetSelector.cs#L927), which dereferences
  `SubgroupIDs.Contains(...)` unguarded — confirming the intended invariant is a non-null list (no code uses
  `null` as a sentinel). Changed the default to `new()`, fixing both the converter and the latent deref. Also
  deep-copied `BodyGenMorphNames` in the converter (was a by-reference assignment of the transient zEBD DTO's
  list). *Test:* new `NPCAssignmentTests` — `new NPCAssignment().SubgroupIDs` is non-null and `.Add(...)` doesn't
  throw (the full converter needs a live `Converters`/env; the POCO invariant is where the bug lived). Suite 185 /
  1 skipped / 0 failed.
- **B26 — `VM_Subgroup` `DisplayForceIfOption` loop: verified NOT a bug; deleted as dead code from all 9 sites.**
  The flagged loop `foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }` ran *before*
  `DisallowedAttributes` was populated, so it iterated an empty collection — but it has **no user-facing symptom**.
  Both ways a disallowed attribute is created already set `DisplayForceIfOption = false` from the same `false`
  argument: load-from-model via `CopyInFromModels(..., false, null)` → `GetViewModelFromModel`
  ([VM_NPCAttribute.cs:224](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L224)), and create-new via the
  "Add Disallowed Attribute" command → `CreateNewFromUI(..., false, …)`
  ([:183](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L183), and the shell at [:177](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L177)).
  So the loop was redundant in every path — the misplaced ones (`VM_Subgroup`, `VM_DetailedReportNPCSelector`)
  as no-ops, and the 7 correctly-placed ones (`VM_BodyShapeDescriptorRules` ×2, `VM_ConfigDistributionRules`,
  `VM_BodyGenTemplateMenu`, `VM_BodySlideSetting`, `VM_HeadPart`, `VM_HeadPartCategoryRules`) as re-setting what
  `CopyInFromModels(false)` already set. Per the user's call, deleted all 9 redundant loops across 8 files. No
  behavior change (verified: `DisplayForceIfOption` ends up `false` for disallowed attributes regardless, in both
  load and create paths). No test (dead-code deletion). Suite 185 / 1 skipped / 0 failed.
- **B27 — `VM_BodySlidePlaceHolder.RenameByIndex` `TrimEnd(charSet)` under-stripped zero-padded suffixes (fixed; catalogue example corrected).**
  The catalogue's `"Body22"→"Body"` example was inaccurate — it doesn't reproduce, because `GetTrailingInt`
  collects the *maximal* trailing digit run, so `TrimEnd` always stops at the non-digit base (`"Body22"` →
  `"Body23"` correctly). The actual defect: `Label.TrimEnd(selectedCloneIndex.ToString().ToArray())` builds the
  char set from the *parsed int*, which has dropped leading zeros, so a zero-padded suffix is under-stripped —
  `"Body007"` → set `{'7'}` → `TrimEnd` leaves `"Body00"` → renames to `"Body008"` instead of `"Body8"` (UI repro:
  clone a BodySlide preset whose name ends in a zero-padded number). Fixed by extracting a pure
  `public static string ReplaceTrailingNumber(string label, int newIndex)` that strips the exact trailing digit
  run by length (matching `GetTrailingInt`'s `char.IsNumber` definition) and appends the new index, also unifying
  the prior if/else (no trailing digits → append). *Test:* new `BodySlidePlaceHolderTests` (5 cases) —
  `"Body007"+8→"Body8"` (the fix), `"CBBE Outfit 007"+2→"CBBE Outfit 2"`, `"Body22"+23→"Body23"` (normal suffix
  regression-guard), `"Body"+2→"Body2"` (append), `"Body2"+3→"Body3"`. Suite 190 / 1 skipped / 0 failed.

---

## 🆕 Added during remediation (not in the original catalogue)

### M1 — `MatureFace` label rename + backward-compat migration — 🐞 compat (BLOCKER for v1.0.7.0)

The "Mildy"→"Mildly" typo fix renames the default `MatureFace` AttributeGroup **Label**.
AttributeGroups are referenced **by Label string** — `NPCAttributeGroup.SelectedLabels`
([NPCAttribute.cs:1130](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L1130)), "resolved by
label at match time" — so renaming the default silently desyncs every reference. Surfaces:
- `GeneralSettings.AttributeGroups[].Label`
- each `AssetPack.AttributeGroups[].Label` ([AssetPack.cs:31](SynthEBD/Classes_Core/Models/AssetPack.cs#L31))
- every `NPCAttributeGroup.SelectedLabels` entry referencing the old label, anywhere
  NPCAttributes appear: config + subgroup `Allowed/Disallowed/ForceIfAttributes` (recursively),
  plus OBody / HeadPart rules.

**Matched pair — must ship together, gated on `PatcherState.Version` (currently "1.0.6.9"):**
1. Re-apply the rename in `DefaultAttributeGroups.MatureFace`.
2. Add `UpdateV1070AttributeGroupRename()` to `UpdateHandler` (cumulative
   `if (appliedVersion < "1.0.7.0")`), modeled on `UpdateV1032AttributeGroups`, rewriting
   old→new across settings + all loaded configs. Bump `PatcherState.Version` to "1.0.7.0".
3. Config-install trigger: when importing a config (`ConfigInstaller` / AssetPack load-merge),
   rewrite incoming old→new references. Implement via a reusable `{oldLabel → newLabel}` alias
   map so future renames are one-liners.
4. Demo settings `SynthEBD.Tests/TestData/DemoSettings/Settings/GeneralSettings.json:1024`
   still carries the old label — keep it old to serve as the migration **test fixture**.
5. Test: run a settings tree + a config that reference the old label through the migration;
   assert the group Label and all `SelectedLabels` references are rewritten and still resolve.

*Status:* rename was reverted out of `d098cac0` so the branch isn't half-migrated; the rename
will land **with** this migration.

### B47 — `ProbabilityWeightModifier` under-applies — 🐞 (found via integration tests)

A `ProbabilityWeightModifier` with `Factor=20` raises the matching subgroup's selection share to
only ~0.81 instead of ~0.95 (20/21); plain `ProbabilityWeighting` ratios are exact (3:1→0.75).
The factor appears to compose at the wrong granularity during combination generation rather than
on the final per-leaf weight. *Repro:* `ProbabilityWeightingTests.cs:84-90` currently asserts only
`> 0.70` with a comment rationalizing 0.79 — the fix tightens it to ~0.95.

### B48 — Whole-config grouping rules are a silent no-op — 🐞 (Important; found via integration tests)

`AssetPack.DistributionRules` resolves `Allowed/DisallowedRaceGroupings` **labels** against the
pack's own `RaceGroupings` (usually empty) instead of `GeneralSettings.RaceGroupings` that
subgroups use — so a config-level `AllowedRaceGroupings={"Nord"}` resolves to nothing and the
whole config distributes to all races. Explicit race FormKeys still work. Same family as the
General_Aux/Patcher "wrong-settings-source" bugs. *Repro:* `ConfigRulesAndInheritanceTests.cs:43-48`
sidesteps it with explicit `AllowedRaces`; the fix lets the grouping path be tested directly.

---

## General_Aux (utility / helper layer)

*The lowest-level helpers — extension methods, comparers, logging, path parsing,
external-tool wrappers, validation. Called by much of the app; calls little of it.*

### Cross-cutting (whole subsystem)

- 🔧/💭 **Namespace style is inconsistent.** Some files use file-scoped namespaces
  (`namespace SynthEBD;`), others the older block form (`namespace SynthEBD { ... }`),
  sometimes within the same folder. A one-time normalization to file-scoped would cut a
  level of indentation across the project.
- 🔧/💭 **Static-only "handler" classes are declared as plain (instance) classes.**
  `NameHandler`, `EditorIDHandler`, `FormKeyHashSetComparer`, `ModKeyHashSetComparer`
  expose only `static` members but are not `static class`, so nothing stops an accidental
  `new`. Marking them `static` documents intent and is free.
- 🔧 **Boilerplate unused `using`s.** Many small files carry the default
  `System / System.Text / System.Threading.Tasks / System.Linq` block even when nothing
  uses them. Harmless, but `dotnet format` / a global cleanup would remove the noise.

### `ExtensionMethods.GetDefaultValue<T>` — 💭 opinion + 🔧 modernize

[ExtensionMethods.cs:12](SynthEBD/General_Aux/ExtensionMethods.cs#L12) · The name implies it
returns a property's *default* value, but it returns the property's *current* value on the
passed instance (it's "default" only if the caller hands in a default-constructed object).
Also returns `dynamic` (a heavier `object` would usually do) and reflects on every call with no
caching. Worth renaming (e.g. `GetPropertyValue`) and reconsidering the `dynamic` return.

### ✅ `FormKeyHashSetComparer` / `ModKeyHashSetComparer` (whole file) — 🔧🐞 RESOLVED (deleted; callers use BCL) — see Resolved §B23

Both classes were deleted entirely. `Equals(a,b)` → `a.SetEquals(b)` (in the typed `NPCAttribute` `Equals`);
`Contains(coll,key)` → `coll.Contains(key)` (12 callers in AttributeMatcher/BodyGenSelector/Patcher); and
`ComparableSetHashCode` → the single generic `NPCAttribute.OrderIndependentHash<T>` (value-identical XOR-fold).
Covered the original three modernize/footgun items (`Equals`, `Contains`, `ComparableSetHashCode`) in one removal.

### `ExceptionLogger.GetExceptionStack` — 🔧 modernize (minor)

[ExceptionLogger.cs:16](SynthEBD/General_Aux/ExceptionLogger.cs#L16) · `e as TooManyMastersException
!= null` followed by a second `as` cast can collapse to `if (e is TooManyMastersException tmm)`.
Cosmetic; the recursion and layer logic look correct.

### `DictionarySplitter.SplitDictionary` — 🔧 modernize (minor)

[DictionarySplitter.cs:11](SynthEBD/General_Aux/DictionarySplitter.cs#L11) · Correct, but the manual
counters could be expressed with LINQ `.Chunk(maxKeyCount)` (.NET 6+) over the entries, then
`ToDictionary`. Lower priority — current code is readable.

### `EditorIDHandler.GetEditorIDSafely<TType>` — 🔧 modernize (minor)

[EditorIDHandler.cs:18](SynthEBD/General_Aux/EditorIDHandler.cs#L18) · The inner `if/else` on
`getter.EditorID` could mirror the first overload's `?? ` form for consistency.

### `ExtendedTreeView` — 💭 opinion (minor/cosmetic)

[ExtendedTreeView.cs:20](SynthEBD/General_Aux/ExtendedTreeView.cs#L20) · Lifted from StackOverflow;
identifiers `___ICH` and the trailing-underscore `SelectedItem_` are unidiomatic. Works fine — flag
only for a future cosmetic rename.

### `Logger` (whole class) — 💭 opinion (architecture)

[Logger.cs:22](SynthEBD/General_Aux/Logger.cs#L22) · This is a god-object: it's a VM for the
status/log UI, *and* the elapsed-time timer, *and* the startup-timing log, *and* the per-NPC XML
report builder, *and* a couple dozen `static` string formatters (`GetSubgroupIDString`,
`GetRaceLogString`, `GetBodyShapeDescriptorString`, `GetFormLogString`, …). The static formatters
have no dependency on logger state and could live in a `LogFormatting` static helper; the NPC XML
report system could be its own `NpcReportBuilder`; the status fields could be a small status VM.
Splitting would shrink this 800-line file and clarify responsibilities. Low urgency, high churn.

### `Logger.TimedNotifyStatusUpdate` (sync) — 🐞 possible bug (UI freeze)

[Logger.cs:487](SynthEBD/General_Aux/Logger.cs#L487) · Uses `Task.Factory.StartNew(() => Task.Delay(n).Wait())`
then `t.Wait()`, which **blocks the calling thread for the full duration**. If called on the UI
thread it freezes the UI for `durationSec` seconds. The async siblings
(`CallTimedNotifyStatusUpdateAsync`) do this correctly; this sync version looks like a leftover and
should probably be removed or made to delegate to the async path.

### `Logger` async-without-await status dance — 🔧 modernize

[Logger.cs:440-485](SynthEBD/General_Aux/Logger.cs#L440-L485) · `UpdateStatusAsync` →
`await Task.Run(() => _UpdateStatusAsync(...))`, where `_UpdateStatusAsync` is an `async Task` that
just sets two properties and never awaits (CS1998 is silenced project-wide). Same for
`ArchiveStatusAsync`/`_ArchiveStatusAsync` and `UnarchiveStatusAsync`/`_DeArchiveStatusAsync`.
Offloading two property assignments to a thread-pool task adds no value (and pushes VM mutation off
the UI thread). These could collapse to the synchronous `UpdateStatus`/`ArchiveStatus` methods that
already exist. Also note the naming mismatch: public `Unarchive…` vs private `_DeArchive…`.

### `Logger.FormatLogStringIndents` — 🔧 modernize / 🐞 edge case

[Logger.cs:343](SynthEBD/General_Aux/Logger.cs#L343) · Hand-rolled XML pretty-printer that
`Replace("><", ">\n<")` then re-indents by counting tags. Fragile: any text node containing `><`
would be mis-split. `XDocument.ToString()` (already called in `SaveReport`) emits indented XML by
default, and `XmlWriterSettings { Indent = true }` covers the rest — worth checking whether this
method is needed at all.

### `Logger` string-building loops — 🔧 modernize (minor)

`GetIndentString` / `Indent` build tabs with `s += "\t"` in a loop → `new string('\t', count)`.
`SpreadFlattenedAssetPack` ([Logger.cs:397](SynthEBD/General_Aux/Logger.cs#L397)) and the non-generic
`GetBodyShapeDescriptorString` ([Logger.cs:618](SynthEBD/General_Aux/Logger.cs#L618)) concatenate in
loops where a `string.Join` + `Select` reads cleaner. All cosmetic.

### `Logger.LogStartupEventStart` / `LogStartupEventEnd` — 🐞 possible bug (minor)

[Logger.cs:128](SynthEBD/General_Aux/Logger.cs#L128) · `Start` always increments the indent but only
registers the stopwatch if the message key is new; `End` only decrements if the key exists. If the
same event label is started twice (or ended without a matching start), the indent counter can drift,
skewing the indentation of later startup-log lines. Keying timers by a non-unique message is the
root risk.

### `Logger.GetRaceLogString` (static) — 💭 opinion (minor)

[Logger.cs:650](SynthEBD/General_Aux/Logger.cs#L650) · The hard-coded special-case races
(Afflicted, Astrid, Snow Elf, …) are an if/else chain that would read better as a
`static Dictionary<FormKey, string>`. Also, two unrelated methods share the name `GetRaceLogString`
(this static single-race formatter vs. the instance `out`-param report builder) — overload-by-accident
that's easy to confuse.

### `Logger` small nits — 💭 opinion / 🔧 (trivial)

- [Logger.cs:230](SynthEBD/General_Aux/Logger.cs#L230): stray double semicolon `);;`.
- [Logger.cs:593](SynthEBD/General_Aux/Logger.cs#L593): `GetNPCLogReportingString` is indented to
  column 0 (the rest of the class is at 4 spaces) — cosmetic formatting slip.
- `Utf8StringWriter` ([Logger.cs:380](SynthEBD/General_Aux/Logger.cs#L380)) appears unused within the
  class — verify references before keeping.

### ✅ `_7ZipInterface.GetArchiveContents` — 🐞 RESOLVED (corrupt-archive check now reads the captured lines) — see Resolved §B15

[7ZipInterface.cs:113](SynthEBD/General_Aux/7ZipInterface.cs#L113) · A `StringBuilder standardOutputCapture`
is declared, but the `OutputDataReceived` handler appends each line to `outputLines`, never to
`standardOutputCapture`. So `outputStr = standardOutputCapture.ToString()` is always empty and the
`Contains("Can't open as archive")` failure check can never fire here — corrupt-archive failures slip
through as an empty content list. (The sibling `ExtractArchive` appends correctly.)

### ✅ `_7ZipInterface.ExtractArchive` — 🐞 RESOLVED (BeginOutputReadLine now gated on redirection; latent) — see Resolved §B15

[7ZipInterface.cs:24](SynthEBD/General_Aux/7ZipInterface.cs#L24) · `RedirectStandardOutput` is only set
when `mirrorUIstr != null`, but `process.BeginOutputReadLine()` is called unconditionally and the
`OutputDataReceived` handler calls `mirrorUIstr(e.Data)` with no null check. If a caller passes
`null` (which the signature permits), `BeginOutputReadLine` throws "StandardOut has not been
redirected", which the catch turns into a misleading "extraction failed" dialog. Either make the
parameter required or guard the null.

### `_7ZipInterface` duplication / UI coupling — 🔧 modernize + 💭 opinion

[7ZipInterface.cs](SynthEBD/General_Aux/7ZipInterface.cs) · `ExtractArchive` and `GetArchiveContents`
share ~90% identical process-launch/stdout-capture boilerplate — extractable into one helper that
takes the 7-Zip arguments. Also, this low-level helper pops `MessageWindow.DisplayNotificationOK`
dialogs directly; returning a result/error to the caller and letting the UI layer decide would
decouple it.

### ✅ `MiscFunctions.StringHashSetsEqualCaseInvariant` — 🐞 RESOLVED (deleted; caller inlined OrdinalIgnoreCase SetEquals) — see Resolved §B13

[MiscFunctions.cs:10](SynthEBD/General_Aux/MiscFunctions.cs#L10) · `b.Contains(s, StringComparer.OrdinalIgnoreCase)`
is the LINQ overload — O(n) per call, so O(n²) overall. More importantly, comparing case-sensitive
`HashSet`s case-insensitively is unsound: `a = {"X","x"}` vs `b = {"X","y"}` (both count 2) returns
`true`. Building a `HashSet<string>(StringComparer.OrdinalIgnoreCase)` and using `SetEquals` fixes
both the perf and the correctness edge.

### `MiscFunctions` smaller items — 🔧 / 💭

- `MakeAlphanumeric` ([MiscFunctions.cs:34](SynthEBD/General_Aux/MiscFunctions.cs#L34)) builds the
  result with `output += c` in a loop → `new string(input.Where(char.IsLetterOrDigit).ToArray())`.
- `MakeXMLtagCompatible` ([MiscFunctions.cs:47](SynthEBD/General_Aux/MiscFunctions.cs#L47)) only
  escapes `+`, leading digits, and spaces; other characters illegal in an XML NCName (`/`, `&`, `:`…)
  would still produce invalid tags. Fine if inputs are known-narrow, but worth documenting the
  assumption. 💭
- `StandardizeData` ([MiscFunctions.cs:96](SynthEBD/General_Aux/MiscFunctions.cs#L96)) names its
  divisor `stdDev`, but it's only the standard deviation when the input was already mean-centered —
  the name bakes in an unstated precondition. 💭

### ✅ `PatchableRaceResolver` — 🐞 RESOLVED (collapsed to a single HashSet<FormKey>; drift impossible by construction) — see Resolved §B14

[PatchableRaceResolver.cs:28](SynthEBD/General_Aux/PatchableRaceResolver.cs#L28) ·
`ResolvePatchableRaces` resets `PatchableRaces = new()` but never clears `PatchableRaceFormKeys`
before re-adding — so calling it more than once leaves stale FormKeys accumulating in one collection
while the other is rebuilt fresh, drifting the two out of sync. Also, the
`if (!set.Contains(x)) set.Add(x)` guards (used 3× in `CompilePatchableRaces`) are redundant —
`HashSet.Add` is already idempotent — and the whole accumulation reads naturally as a couple of LINQ
`Concat`/`SelectMany` unions into a `HashSet`.

### Synthesis env wrappers are near-duplicates — 💭 opinion / 🔧 modernize

[EnvironmentStateProvider.cs:207-296](SynthEBD/General_Aux/EnvironmentStateProvider.cs#L207-L296) ·
`OpenForSettingsWrapper`, `RunnabilitySettingsWrapper`, and `PatcherStateWrapper` repeat the same
~13 property delegations almost verbatim (differing mainly in `LoggerMode` and a couple of paths).
A shared abstract base (or a single wrapper over the common `IGameEnvironment`) would remove the
triplication.

### `StandaloneRunEnvironmentStateProvider` nits — 💭 / 🐞 (trivial)

- [EnvironmentStateProvider.cs:201-202](SynthEBD/General_Aux/EnvironmentStateProvider.cs#L201-L202):
  `SelectUserSpecifiedGameEnvironment` calls both `Application.Current.Shutdown()` *and*
  `Environment.Exit(1)`, and pops a dialog directly — a low-level state provider reaching out to hard
  app-exit + UI is a layering smell. 💭
- [EnvironmentStateProvider.cs:138-139](SynthEBD/General_Aux/EnvironmentStateProvider.cs#L138-L139):
  `OutputMod = null;` immediately before `OutputMod = new SkyrimMod(...)` is dead. 🔧
- [EnvironmentStateProvider.cs:156](SynthEBD/General_Aux/EnvironmentStateProvider.cs#L156): typo in
  the exception text — "Load order file patha at". 💭
- The `IEnvironmentStateProvider` members mix `public` and bare modifiers; `public` on an interface
  member is redundant. 💭

### ✅ `BSAHandler` — 🐞 RESOLVED (candidate-mods overload now sets modName) — see Resolved §B17

[BSAHandler.cs:324](SynthEBD/General_Aux/BSAHandler.cs#L324) · The `candidateMods` overload of
`ReferencedPathExists` initializes `modName = ""` and never assigns the matched mod, so callers always
receive an empty `modName` even on a hit — unlike the single-path overload, which does set it. If any
caller relies on `modName` from this overload, it's silently wrong.

### `BSAHandler` — 🐞 verify (cross-thread mutation)

[BSAHandler.cs:606](SynthEBD/General_Aux/BSAHandler.cs#L606),
[:682](SynthEBD/General_Aux/BSAHandler.cs#L682) · `OpenReaders` is a `ConcurrentDictionary`, but its
*values* are plain `HashSet<IArchiveReader>` that `ReadersOrDeferredHaveFile` / `TryFindFileInAnyArchive`
mutate via `bsaReaders.Add(reader)`. Since the patcher resolves assets across NPCs in parallel, two
threads sharing the same mod's reader set could mutate one `HashSet` concurrently (not thread-safe).
Worth verifying whether these paths can actually run concurrently for the same `ModKey`; if so, a
concurrent set or locking is needed.

### `BSAHandler` — 💭 leftover narration comments

The file carries diff-narration comments that read as notes-to-self rather than documentation:
`// ... (Unchanged logic) ...` ([:285](SynthEBD/General_Aux/BSAHandler.cs#L285),
[:326](SynthEBD/General_Aux/BSAHandler.cs#L326)),
`// ... (Includes the leak fixes applied previously) ...` ([:462](SynthEBD/General_Aux/BSAHandler.cs#L462)),
and several `// NEW:` markers. Harmless, but worth sweeping out.

### `BSAHandler.TryGetFile` fallback — 🔧 modernize (minor)

[BSAHandler.cs:511](SynthEBD/General_Aux/BSAHandler.cs#L511) · `bsaReader.Files.Where(...).ToArray()`
then `.Any()`/`.First()` materializes the whole match set just to take the first — `FirstOrDefault`
(with a null check) is one pass and no allocation. Also `OpenReaders` is a `public` mutable field;
exposing it as a read-only view would tighten encapsulation. 💭

### `FilePathDestinationMap.FileNameToDestMap` — 🐞 possible bug (wrong destination)

[FilePathDestinationMap.cs:374](SynthEBD/General_Aux/FilePathDestinationMap.cs#L374) ·
`{ Source_HeadSpecularArgonianFemale, Dest_TorsoFemaleSpecular }` maps a **head** specular source to a
**torso** specular destination. The male counterpart
([:313](SynthEBD/General_Aux/FilePathDestinationMap.cs#L313)) correctly maps to `Dest_HeadSpecular`,
and every other female head-specular entry maps to `Dest_HeadSpecular`. This looks like a copy-paste
slip that would route the Argonian female head specular texture onto the body. Worth verifying against
the vanilla asset layout.

### `FilePathDestinationMap` helpers — 🔧 modernize (minor)

[FilePathDestinationMap.cs:417](SynthEBD/General_Aux/FilePathDestinationMap.cs#L417),
[:436](SynthEBD/General_Aux/FilePathDestinationMap.cs#L436) · `HasTNGPaths`/`HasEtcPaths` use
`paths.Where(pred).Any()`; `paths.Any(pred)` is equivalent and clearer. Also, the long `EndsWith`
disjunction in `HasTNGPaths` re-lists the same suffixes that already exist as keys in the TNG portion
of `FileNameToDestMap` — building a `HashSet` of TNG/etc suffixes once would remove the duplication
and the drift risk.

### `FilePathDestinationMap` overall — 💭 opinion

~150 `const string` magic paths plus four parallel dictionaries (`FileNameToDestMap`, `MaleTorsoPaths`,
`FemaleTorsoPaths`) that partially overlap. It works and is readable per-line, but the torso subsets
duplicate entries already in the master map, so the maps can silently drift. A single source-of-truth
table tagged by body-part/sex/variant would be more maintainable. Low priority (data, not logic).

### ✅ `RecordPathParser` array-index bounds guard — 🐞 RESOLVED (×2) — see Resolved §B11

[RecordPathParser.cs:352](SynthEBD/General_Aux/RecordPathParser.cs#L352) and
[:397](SynthEBD/General_Aux/RecordPathParser.cs#L397) · Both numeric-index branches guard with
`if (iIndex < 0 || iIndex < collectionObj.Count())` and then call `ElementAt(iIndex)` in the "valid"
arm. The intent was clearly `iIndex >= 0 && iIndex < Count`. As written, a **negative** index passes
the guard (`iIndex < 0` is true) and then `ElementAt(negative)` throws instead of failing gracefully.
A path like `[-1]` triggers it. The `||` should be `&&` with `>= 0`.

### `RecordPathParser` static caches are not thread-safe — 🐞 verify (concurrency)

[RecordPathParser.cs:46](SynthEBD/General_Aux/RecordPathParser.cs#L46) (`_lambdaCache`),
[:1115](SynthEBD/General_Aux/RecordPathParser.cs#L1115) (`PropertyCache`),
[:1166](SynthEBD/General_Aux/RecordPathParser.cs#L1166) (`GetterEmbassy`/`SetterEmbassy`) · These are
plain `Dictionary`s mutated via read-then-add. `_lambdaCache` sits on the hot path
(`EvalBoolExpression`, reached during per-NPC array-condition evaluation). If the patcher evaluates
conditions across NPCs in parallel, concurrent `Dictionary` writes can corrupt internal state or
throw. Worth confirming the concurrency model; if parallel, switch to `ConcurrentDictionary` or lock.

### ✅ `RecordPathParser.RemovePairedParens` — 🐞 RESOLVED (hardened depth-aware; notes' active example was latent) — see Resolved §B12

[RecordPathParser.cs:541](SynthEBD/General_Aux/RecordPathParser.cs#L541) · It strips outer parens
whenever the string merely *starts* with `(` and *ends* with `)`, regardless of pairing. For
`(a) && (b)` that wrongly yields `a) && (b`. The depth-aware `TrimParens` right below it is the
correct approach; `RemovePairedParens` should use the same scanning (or be removed if `TrimParens`
suffices). Verify against the conditions actually parsed.

### `RecordPathParser` smaller items — 🔧 / 💭

- [RecordPathParser.cs:345](SynthEBD/General_Aux/RecordPathParser.cs#L345): error string
  `"Could not cast " + ... + "as an XXX"` — a leftover placeholder (and missing a space before "as").
- `ChooseWhichArrayObject` and `ChooseSelectedArrayObjects`
  ([:750](SynthEBD/General_Aux/RecordPathParser.cs#L750), [:852](SynthEBD/General_Aux/RecordPathParser.cs#L852))
  are ~95% identical (single-match vs collect-all). They could share one implementation parameterized by
  "first vs all". 🔧/💭
- Many helpers use `if (x != null) return true; else return false;` →
  `return x != null;` (e.g. `GetPropertyInfo`, `GetAccessor`, `ObjectHasFormKey`). 🔧
- `GetPropertyInfo_NoCache` / `GetAccessor_NoCache` are explicitly "for performance testing only", and
  the whole `GetAccessor`/Embassy delegate-caching path is disabled (commented out in `GetSubObject`/
  `SetPropertyValue`). Consider extracting these into a clearly-marked experiments file or removing. 💭
- `variants.Count()` (LINQ) in loop bounds over an `IReadOnlyList` ([:769](SynthEBD/General_Aux/RecordPathParser.cs#L769),
  [:865](SynthEBD/General_Aux/RecordPathParser.cs#L865)) — the `.Count` property avoids re-enumeration. 🔧

### `RecordPathParser` overall — 💭 opinion

This is a genuinely impressive but high-risk subsystem: a hand-rolled record-path DSL whose conditions
are parsed by ad-hoc string surgery (paren trimming, `Invoke:` rewriting, `{LAMBDA}` placeholder
swaps) before being handed to DynamicExpresso. It works, but it's hard to test and easy to break with
unusual user input. Not something to rewrite lightly — but it would benefit most from a focused unit-test
suite around `SplitPath`, `ArrayPathCondition` parsing, and the bounds/paren bugs above.

### `PreRunValidation` — 💭 opinion (minor)

- [PreRunValidation.cs:6](SynthEBD/General_Aux/PreRunValidation.cs#L6): `using static
  System.Windows.Forms.AxHost;` is almost certainly an accidental auto-import (AxHost is an ActiveX
  host base class) — nothing here uses it. Safe to delete.
- `ValidatePatcherState` is one long sequential method built on the repeated
  `if (!Verify...()) { valid = false; }` pattern. It reads fine top-to-bottom, but the dependency
  checks could be expressed as a list of `(condition, validationFunc)` steps to cut the repetition. The
  truth-table section at the end is well-commented and worth keeping as-is. Low priority. 🔧

### ✅ `MiscValidation` ini parsers — 🐞 RESOLVED (line-ref centralized in AppendIniLineReference; guard/value can't diverge) — see Resolved §B16

[MiscValidation.cs:577](SynthEBD/General_Aux/MiscValidation.cs#L577),
[:594](SynthEBD/General_Aux/MiscValidation.cs#L594),
[:643](SynthEBD/General_Aux/MiscValidation.cs#L643) · In `VerifyRaceMenuIniForBodyGen`/`...ForBodySlide`,
the "could not parse" branches for *bEnableBodyGen* and *iScaleMode* append the offending line number
only `if (morphLine.Any())` — but they then print `genLine` / `scaleLine`. The guard was copy-pasted
from the morph block; it should check `genLine`/`scaleLine` respectively. Result: a bad BodyGen/ScaleMode
line's number is shown only when the *morph* line happened to be non-empty (and vice-versa).

### `MiscValidation.VerifyBlankAttributes` — 🐞 cosmetic (unclosed bracket)

[MiscValidation.cs:324](SynthEBD/General_Aux/MiscValidation.cs#L324) ·
`assetPack.GroupName + ": Subgroups [" + String.Join(", ", subGroupIDs)` opens a `[` that is never
closed — the user-facing message reads `Subgroups [a, b, c` with no trailing `]`.

### `MiscValidation.Verify*Installed` duplication — 🔧 modernize

Roughly a dozen methods (`VerifyEBDInstalled`, `VerifyOBodyInstalled`, `VerifyAutoBodyInstalled`,
`VerifySPIDInstalled`, `VerifySkyPatcherInstalled`, `VerifyPO3ExtenderInstalled`, …) follow the identical
"build a Data-relative path → `File.Exists` → log not-found → return bool" shape. A single helper like
`bool VerifyDataFile(string relativePath, string sourceModName, bool silent)` (looped over a small
descriptor list) would collapse most of this file. High-value, low-risk cleanup.

### `MiscValidation` smaller items — 🔧 / 💭

- `VerifyOBodyTemplateJsonExits` ([:101](SynthEBD/General_Aux/MiscValidation.cs#L101)) — method name typo,
  "Exits" → "Exists". 💭
- `VerifyBodyGenAnnotations` ([:235-238](SynthEBD/General_Aux/MiscValidation.cs#L235-L238)) declares
  `missingBodyGenMessage` and `messages` lists that are never used. Dead locals. 🔧
- `bodyGenConfigs.Male.Where(x => x.Label == name).FirstOrDefault()` (and `.Where(...).Any()` elsewhere)
  → `.FirstOrDefault(x => x.Label == name)` / `.Any(predicate)`. 🔧
- `VerifySPIDInstalled` appears to be called only from the commented-out block in `PreRunValidation`
  ([PreRunValidation.cs:129](SynthEBD/General_Aux/PreRunValidation.cs#L129)); verify whether it's still
  needed. 💭
- `CheckRaceGroupingDuplicates` ([:658](SynthEBD/General_Aux/MiscValidation.cs#L658)) does manual
  index-decrement removal and recomputes duplicate counts with repeated `Where(...).Count()`; a
  `GroupBy(x => x.Label)` pass would be clearer. 🔧

### General_Aux view models (deferred items) — 🔧 / 🐞 / 💭

*The two higher-level General_Aux VMs (`VM_7ZipInterface`, `VM_CustomEnvironment`) and their windows,
documented after their dependencies.*

- **`VM_7ZipInterface.StartExtraction` is a dead command.**
  [VM_7ZipInterface.cs:53](SynthEBD/General_Aux/ViewModels/VM_7ZipInterface.cs#L53) · The `StartExtraction`
  RelayCommand is declared get-only but never assigned, so it is always null. Either wire it to the
  extraction path or remove it. 💭
- **`VM_CustomEnvironment` redundant `PropertyChanged` event.**
  [VM_CustomEnvironment.cs:129](SynthEBD/General_Aux/ViewModels/VM_CustomEnvironment.cs#L129) · The base
  `VM` already implements `INotifyPropertyChanged` (Fody-woven), but this class hand-declares
  `public event PropertyChangedEventHandler PropertyChanged;` which is never raised. Dead/shadowing — remove. 🔧
- **`VM_CustomEnvironment.UpdateTrialEnvironment` builds the environment twice.**
  [VM_CustomEnvironment.cs:152](SynthEBD/General_Aux/ViewModels/VM_CustomEnvironment.cs#L152) · Right after
  `TrialEnvironment = builder.TransformModListings(...).Build();`, a bare `builder.Build();` runs again and
  discards the result. Building a Mutagen environment is not cheap, so this doubles the cost for nothing —
  looks like a leftover; drop the second call. 🐞/🔧
- **WinForms cursor set from a VM.**
  [VM_CustomEnvironment.cs:140](SynthEBD/General_Aux/ViewModels/VM_CustomEnvironment.cs#L140),
  [:164](SynthEBD/General_Aux/ViewModels/VM_CustomEnvironment.cs#L164) · `Cursor.Current = Cursors.WaitCursor/Default`
  (System.Windows.Forms) is driven from inside the view model — UI concern that belongs in the view. Minor. 💭

---

## Classes_Aux (models)

*Core data models — POCO settings carriers plus heavier descriptor/attribute models and the
preview/mesh/texture services. Reviewed before their view models and views.*

### `RaceGrouping.MergeRaceAndGroupingList` — 🔧 modernize + 💭

[RaceGrouping.cs:12](SynthEBD/Classes_Aux/Models/RaceGrouping.cs#L12) · Carries a stale uncertainty
comment ("this might need to work - might need to convert to string. Be sure to validate.") that reads
like a note-to-self left in shipped code. The body also builds a `HashSet<HashSet<FormKey>>` and unions
in a loop; it collapses to one expression:
`indivRaceList.Concat(raceGroupingList.Where(g => selected.Contains(g.Label)).SelectMany(g => g.Races)).ToHashSet()`.

*(The remaining small POCOs in this folder — Gender, TrimPath, AdditionalRecordTemplate, LinkedNPCGroup,
RaceAlias, HeadPartConsistency, BodySlideExchange, DetailedReportNPCSelector — are clean data carriers
with nothing to flag.)*

### `BodyShapeDescriptorRules.NPCisValid` — 💭 opinion (hidden side effect)

[BodyShapeDescriptorRules.cs:23](SynthEBD/Classes_Aux/Models/BodyShapeDescriptorRules.cs#L23) · A
method named `...isValid` that returns a bool also **mutates** `descriptor.AssociatedRules.MatchedForceIfCount`
as a side effect (read later by selection code). It works, but a predicate-named method with a write is
a readability trap — worth either renaming or returning the count explicitly. Minor: the
`AllowedAttributes` field comment says "keeping as array" but the type is a `HashSet` (stale wording).

*(The newer files in this tier — SliderCategoryCatalog, SubgroupTextureMapper, BodyGenSpecsParser,
BodyShapeDescriptorShell, NifPreviewNpcSettings — are already thoroughly documented and cleanly written;
this pass only filled a few gaps.)*

### `BodyShapeDescriptor` — 🔧 modernize (minor)

[BodyShapeDescriptor.cs:38](SynthEBD/Classes_Aux/Models/BodyShapeDescriptor.cs#L38) · The two `MapsTo`
methods (and `Equals`) use `obj is X` followed by `obj as X` — pattern matching
(`if (obj is BodyShapeDescriptor other)`) does both in one step. The two `MapsTo` implementations
(descriptor vs signature) are near-identical and could share a helper. `using
Synthesis.Bethesda.Execution.DotNet;` ([:2](SynthEBD/Classes_Aux/Models/BodyShapeDescriptor.cs#L2)) looks
like a stray import (nothing uses it), and `GetHashCode` could use `HashCode.Combine`. All cosmetic.

### ✅ `NPCAttribute*.CloneAsNew` — 🐞 RESOLVED (copies Not, deep-copies collections, Misc copies mood/aggression/gender) — see Resolved §B22

[NPCAttribute.cs:263](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L263) (and the other 10) · Most
`CloneAsNew` factories assign the collection by reference — `output.FormKeys = input.FormKeys;` — so
the "clone" shares the *same* `HashSet` as the original; mutating one mutates the other. Only
`NPCAttributeCustom` deep-copies (and only its Record branch). Separately, several clones **drop
fields**: every type omits `Not`, and `NPCAttributeMisc.CloneAsNew`
([:789](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L789)) omits `Mood`, `Aggression`, `EvalGender`,
and `NPCGender`. If these are used for UI "duplicate" actions, the duplicate silently diverges.

### ✅ `NPCAttribute.Equals(NPCAttribute)` — 🐞 RESOLVED (SetEquals + family equality audit + object.Equals overrides) — see Resolved §B23

[NPCAttribute.cs:28](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L28) · Compares `SubAttributes` by
`ToArray()` then index-by-index, but `SubAttributes` is an unordered `HashSet`. Two attributes with the
same sub-attributes enumerated in different order would compare unequal (and, paired with the XOR
`GetHashCode`, could land in a set inconsistently). Use `SetEquals`, or order both sides the same way
the hash does.

### ✅ `NPCAttributeCustom.GetHashCode` — 🐞 RESOLVED (null-safe Comparator) — see Resolved §B24

[NPCAttribute.cs:333](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L333) · Calls
`Comparator.GetHashCode()`, but `Comparator` has no default and can be null (unlike the other string
fields it's not initialized to `""`). A null `Comparator` throws inside `GetHashCode`. Use
`Comparator?.GetHashCode() ?? 0` (or `HashCode.Combine`).

### `NPCAttribute` family duplication — 🔧 modernize (headline)

The 11 `NPCAttribute*` classes are ~90% identical boilerplate. The six FormKey-based ones (Class,
FaceTexture, Keyword, Race, NPC, VoiceType) differ only in `Type`, the log label, and the getter type
passed to `FormKeyToLogString*`. A generic base (e.g. `NPCAttributeFormKeyBase<TGetter>` carrying
`FormKeys`/`Type`/`ForceMode`/`Weighting`/`Not` + shared `Equals`/`GetHashCode`/`IsBlank`/`CloneAsNew`)
would remove hundreds of lines and eliminate the per-class clone bugs above in one place. This is the
single biggest cleanup opportunity in the models folder. *(Because the members are pure interface
boilerplate, this pass documented the `ITypedNPCAttribute` contract once plus each class's summary and
`CloneAsNew`, rather than 55 redundant per-member copies — say the word if you'd prefer `<inheritdoc/>`
on every implementation.)*

### `NPCAttribute` smaller items — 🔧 / 💭

- The XOR set-hash helpers (`NPCAttribute.GetHashCode`, `NPCAttributeGroup.ComparableSetHashCode`)
  repeat the redundant `first`-flag pattern from `FormKeyHashSetComparer`; XOR is commutative so the
  `OrderBy` in `NPCAttribute.GetHashCode` ([:48](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L48)) is
  wasted. 🔧
- Adding a new attribute type requires manually updating three places that can silently drift: the
  `CloneAsNew` switch ([:101](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L101)), the file-header
  comment's `JSONhandler.AttributeConverter`, and `NPCAttributeType`. A registry/dictionary keyed by
  type would make this single-source. 💭
- `NPCAttributeMisc.GetHashCode` closing brace ([:787](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L787))
  is indented to column 0. Cosmetic. 💭

### Preview / mesh services — 💭 mostly clean

`FaceGenPreviewService`, `PreviewNpcResolver`, `NpcMeshResolver`, and `NifTextureLoader` are recent
CharacterViewer code, already thoroughly documented and cleanly written; this pass only filled
constructor / private-helper gaps. Two minor items:

- `NifTextureLoader.LoadDdsTextureViaPfim` and `LoadDdsPixels`
  ([NifTextureLoader.cs:217](SynthEBD/Classes_Aux/Models/NifTextureLoader.cs#L217),
  [:403](SynthEBD/Classes_Aux/Models/NifTextureLoader.cs#L403)) duplicate the same Pfim
  Rgba32/Rgb24/Rgb8 → BGRA decode switch; a shared `DecodeToBgra` helper would remove the copy. 🔧
- `NifTextureLoader.CreateTextureModelViaBmp`
  ([:683](SynthEBD/Classes_Aux/Models/NifTextureLoader.cs#L683)) actually encodes **PNG** (its own doc
  says so, to preserve alpha) — the "Bmp" in the name is a leftover and misleads. 💭

---

## Classes_Aux (view models & views)

*The VM_* view models behind the settings UI, plus the .xaml.cs view code-behind. This is the layer the
user flagged as their .NET learning ground, so most notes here are 🔧 modernization opportunities.*

### View-model round-trip pattern — 💭 observation

Nearly every `VM_*` follows the same shape: an Autofac `Factory` delegate, a constructor that wires
`RelayCommand`s and `WhenAnyValue` subscriptions, and `GetViewModelFromModel` / `DumpViewModelToModel`
(or `CopyInFromModel`) round-trip helpers. It's consistent and readable; the recurring 🔧 theme is hand-
rolled loops where LINQ/`SetEquals` would be terser (the user's noted pre-LINQ habit). Individual items
below; not re-flagged per file.

### `VM_RaceGrouping.CollectionMatchesRaceGrouping` — 🔧 modernize + 💭

[VM_RaceGrouping.cs:63](SynthEBD/Classes_Aux/ViewModels/VM_RaceGrouping.cs#L63) · O(n²) nested-loop set
comparison (→ `group.Races.ToHashSet().SetEquals(collection)`), and the trailing inline comment still
says "returns true if…" though the method was changed to return the matched set.

### `VM_LinkedNPCGroup.DumpViewModelsToModels` — 🐞 possible bug (fragile parse)

[VM_LinkedNPCGroup.cs:90](SynthEBD/Classes_Aux/ViewModels/VM_LinkedNPCGroup.cs#L90) · Recovers the
primary NPC's FormKey via `vm.Primary.Split('|')[2]`, assuming the display string is always the
3-field "Name | EditorID | FormKey" form. A primary whose name/EditorID itself contains `|`, or any
other display shape, would mis-parse or throw `IndexOutOfRange`. Storing the FormKey on the VM rather
than re-parsing the display string would be robust.

### `VM_AssetPresenter.UpdatePreviewImages` — 🐞 possible bug (`&` vs `&&`)

[VM_AssetPresenter.cs:208](SynthEBD/Classes_Aux/ViewModels/VM_AssetPresenter.cs#L208) ·
`sourcedImagePath.SourceChain != null & !sourcedImagePath.SourceChain.Contains(...)` uses the
non-short-circuiting bitwise `&`, so the right operand is evaluated even when `SourceChain` is null —
a null-reference risk. Should be `&&`.

### `VM_BodyShapeDescriptor.DumpViewModeltoModel` — 💭 (typo)

[VM_BodyShapeDescriptor.cs:77](SynthEBD/Classes_Aux/ViewModels/VM_BodyShapeDescriptor.cs#L77) · Method
name has a lowercase "to" (`DumpViewModeltoModel`) — inconsistent with the `DumpViewModelToModel`
convention used everywhere else. Cosmetic rename.

### `VM_FilePathReplacement` — 🔧 modernize (record-path strings triplicated)

[VM_FilePathReplacement.cs:162](SynthEBD/Classes_Aux/ViewModels/VM_FilePathReplacement.cs#L162),
[:343](SynthEBD/Classes_Aux/ViewModels/VM_FilePathReplacement.cs#L343),
[:431](SynthEBD/Classes_Aux/ViewModels/VM_FilePathReplacement.cs#L431) · The full set of
`WornArmor.Armature[...].SkinTexture.<sex>.<slot>.GivenPath` destination strings is written out three
times in this one file — once in `DestinationDetailAbstractDictionary` (path→friendly), once in
`DestinationOptions` (menu→friendly alias), once in `GetPathFromTypeString` (friendly→path) — and a
fourth time in `FilePathDestinationMap`. Any change to a destination path means editing four hand-kept
copies. A single bidirectional table (friendly-name ⇄ path) driving all four would remove the drift risk.

### `VM_NPCAttributeCustom.Evaluate` — 🐞 possible bug (missing `return` → null reference NPC)

[VM_NPCAttribute.cs:797](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L797) · When
`_environmentProvider.LinkCache.TryResolve<INpcGetter>(ReferenceNPCFormKey, out var refNPC)` fails, the
method sets `EvalResult = "Error: can't resolve reference NPC."` but does **not** `return`. It falls
through and passes the unresolved (null) `refNPC` straight into
`_attributeMatcher.EvaluateCustomAttribute(refNPC, …)`. Depending on how the matcher dereferences the NPC
this is a latent NRE (or at best a misleading second result that overwrites the error message). The
failed-resolve branch should `return` (or guard the subsequent call).

### `VM_NPCAttribute` view-model family mirrors the model boilerplate — 🔧 modernize

[VM_NPCAttribute.cs:504](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L504) (and the other 10) · The
eleven `VM_NPCAttribute*` sub-VMs are the VM-side twins of the eleven `NPCAttribute*` models and carry the
same ~90% duplication: each repeats an identical constructor (store parent VM/shell, subscribe `LinkCache`,
wire `DeleteCommand`), an identical `DebuggerString`, and near-identical static
`GetViewModelFromModel`/`DumpViewModelToModel` round-trip helpers that differ only in the FormKey field and
log label. A shared generic base (paralleling the `NPCAttributeFormKeyBase<TGetter>` suggested for the
models) would collapse most of the file. *(Following the model-family precedent, this pass documented the
`ISubAttributeViewModel` contract once plus each class summary, constructor, and round-trip helper, rather
than ~33 redundant per-member copies.)*

### `VM_NPCAttributeShell` Factory drops `displayForceIfWeight` — 💭 / 🔧 (dead factory parameter)

[VM_NPCAttribute.cs:311](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L311) · The `Factory` delegate
declares `bool? displayForceIfWeight`, and callers (`CreateNewFromUI`, `CreateNewShell`,
`GetViewModelFromModel`) all pass it — but the constructor
([:333](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L333)) has no matching parameter, so Autofac's
delegate factory silently discards the argument. `DisplayForceIfWeight` is instead derived reactively from
`ForceModeStr`, so behaviour is unaffected, but the unused delegate parameter is misleading and threads a
value through three call sites that goes nowhere. Drop it, or have the ctor consume it.

### `VM_NPCAttribute` constructor `selfFactory` unused — 🔧 (minor)

[VM_NPCAttribute.cs:47](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L47) · The injected
`VM_NPCAttribute.Factory selfFactory` is never referenced (new conditions are created through
`_creator` instead). Dead injected parameter — safe to remove.

### `VM_NPCAttributeShell.GetOrCreateSubAttribute` — 💭 (Group case bypasses the factory)

[VM_NPCAttribute.cs:471](SynthEBD/Classes_Aux/ViewModels/VM_NPCAttribute.cs#L471) · Every attribute type is
constructed through an injected `*.Factory` delegate except `NPCAttributeType.Group`, which is `new
VM_NPCAttributeGroup(...)`d inline (it takes no injected services, so it has no factory). Minor
inconsistency. Relatedly, `InitializedVMcache` and this `switch` are two manual-sync points that must both
be updated when a new attribute type is added (same single-source-of-truth smell flagged on the model's
`CloneAsNew` switch).

### `NumericOnly` TextBox handler duplicated across views — 🔧 modernize

Five view code-behinds carry an identical `NumericOnly(object, TextCompositionEventArgs)` handler that
defers to `IsNumeric.IsTextNumeric` to reject non-numeric keystrokes:
[UC_NPCAttributeFactions.xaml.cs:16](SynthEBD/Classes_Aux/Views/Sub-NPCAttributes/UC_NPCAttributeFactions.xaml.cs#L16),
[UC_BodyShapeDescriptorRules.xaml.cs:17](SynthEBD/Classes_Aux/Views/BodyShape%20SubViews/UC_BodyShapeDescriptorRules.xaml.cs#L17),
[UC_ConfigDistributionRules.xaml.cs:17](SynthEBD/Classes_Aux/Views/UC_ConfigDistributionRules.xaml.cs#L17),
[UC_DetailedReportNPCSelector.xaml.cs:30](SynthEBD/Classes_Aux/Views/UC_DetailedReportNPCSelector.xaml.cs#L30),
[Window_AssetDistributionSimulator.xaml.cs:29](SynthEBD/Classes_Aux/Views/Window_AssetDistributionSimulator.xaml.cs#L29).
A single attached behavior (e.g. a `NumericInputBehavior` bound in XAML) would remove the copy-paste and the
per-view event wiring.

### Auto-generated view summaries had stale XAML filenames — 💭 (rename history smell, now fixed)

While documenting the trivial Views, four code-behinds carried Visual Studio's auto-generated
`/// Interaction logic for <X>.xaml` summary that named a *different* file than the class — evidence the
`.cs`/`.xaml` pair was copied or renamed without updating the boilerplate:
`UC_BodyShapeDescriptorSelectionMenu` (said `…Selector.xaml`), `UC_AssetReplacementAssignment_Consistency`
(said `UC_AssetReplacerAssignment_Consistency.xaml`), `UC_RaceGroupingCheckboxList` (said
`…Checkbox.xaml`), and `UC_AttributeGroupMenu` (said `UC_AttributeGroup.xaml`). These boilerplate summaries
were replaced with meaningful ones in this pass, so the stale references are gone — flagging only as a sign
that a few of these views were cloned from siblings.

<!-- ENTRIES:Classes_Aux_VM -->

---

## Classes_Core (models)

*The core data models the patcher and the Core view models build on — settings POCOs, plus the
zEBD-format backwards-compatibility loaders. Reviewed before the Core view models. The newer
BodySlide/ML/measurement models (BodySlideGroupClassifier, MeasurementCacheStore, RegionVolumeEvaluator,
RuleSynthesizers, …) were already thoroughly documented; this pass covered the older undocumented ones.*

### ✅ `NPCAssignment` / `zEBDSpecificNPCAssignment.ToSynthEBDNPCAssignments` — 🐞 RESOLVED (SubgroupIDs defaults to a list; BodyGen morphs deep-copied) — see Resolved §B25

[NPCAssignment.cs:11](SynthEBD/Classes_Core/Models/NPCAssignment.cs#L11),
[:78](SynthEBD/Classes_Core/Models/NPCAssignment.cs#L78) · `NPCAssignment.SubgroupIDs` is initialized to
`null` (not an empty list), but `ToSynthEBDNPCAssignments` does `s.SubgroupIDs.Add(zFS.id)` for every forced
subgroup in a legacy assignment — so importing any zEBD assignment that has forced subgroups throws a
`NullReferenceException`. Initialize `SubgroupIDs` to `new()` there (or default the property to a list).
Also `s.BodyGenMorphNames = z.forcedBodyGenMorphs;` assigns the source list by reference.

### `zEBDBodyGenConfig` conversion — 🔧 / 💭 (minor)

[BodyGenConfig.cs:135](SynthEBD/Classes_Core/Models/BodyGenConfig.cs#L135) · `ToSynthEBDConfig` takes a
`filePath` parameter that the body never uses — dead parameter. And `zEBDBodyGenRacialSettingsToSynthEBD`
([:210](SynthEBD/Classes_Core/Models/BodyGenConfig.cs#L210)) guards `if (usedGroups.Contains(member) == false) usedGroups.Add(member)` —
redundant, since `HashSet.Add` is already idempotent (same pattern flagged in General_Aux). Both cosmetic.

### `ZEBDAssetPack` conversion — 💭 / 🔧 (UI coupling + dead param)

[AssetPack.cs:448](SynthEBD/Classes_Core/Models/AssetPack.cs#L448),
[:525](SynthEBD/Classes_Core/Models/AssetPack.cs#L525) · `ToSynthEBDAssetPack` pops a modal WPF dialog
(`Window_LinkZEBDAssetPackToBodyGen.ShowDialog()`) from inside a model conversion method — UI driven from the
model layer, which makes the conversion untestable headless. Also,
`ZEBDSubgroup.ToSynthEBDSubgroup` ([:276](SynthEBD/Classes_Core/Models/AssetPack.cs#L276)) takes an
`assetPackName` parameter the body never uses (only threaded through recursion), and its allowed/disallowed
race loops use the redundant `Contains`-before-`Add` pattern flagged elsewhere. Cosmetic.

### `AssetPackValidator.GetIDDuplicates` — 🔧 (minor)

[AssetPackValidator.cs:378](SynthEBD/Classes_Core/Models/AssetPackValidator.cs#L378) · Tracks seen IDs in a
`List<string>` with `searched.Contains(...)` (O(n) per lookup → O(n²) overall). A `HashSet<string>` for the
"seen" set makes it O(n). Minor; subgroup counts are small.

<!-- ENTRIES:Classes_Core_Models -->

---

## Classes_Core (view models)

*The view models behind the asset-pack / BodyGen / consistency / blocklist editors. The user's main .NET
learning ground, so most items are 🔧 modernizations; a few real bugs surfaced. (The newer OBody SubModels
were already documented and were not re-reviewed here.)*

### ✔️ `VM_Subgroup.CopyInViewModelFromModel` — 🐞 NOT A BUG (no symptom); dead loop deleted from all 9 sites — see Resolved §B26

[VM_Subgroup.cs:287](SynthEBD/Classes_Core/ViewModels/VM_Subgroup.cs#L287) ·
`foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }` runs *before*
`DisallowedAttributes` is populated (the `_attributeCreator.CopyInFromModels(..., DisallowedAttributes, ...)`
call is ~10 lines later at [:297](SynthEBD/Classes_Core/ViewModels/VM_Subgroup.cs#L297)), so the loop
iterates an empty collection and sets nothing. The sibling VMs (`VM_HeadPart`, `VM_HeadPartCategoryRules`)
run the same line *after* populating — so disallowed attributes in a subgroup never get
`DisplayForceIfOption = false` applied, unlike everywhere else.

### `VM_AssetPackDirectReplacerMenu` ctor — 🐞 possible bug (subscription not disposed) / 🔧

[VM_AssetPackDirectReplacerMenu.cs:32](SynthEBD/Classes_Core/ViewModels/VM_AssetPackDirectReplacerMenu.cs#L32) ·
The functional `WhenAnyValue(DisplayedGroup).Buffer(2,1)...Subscribe(...)` (which dumps the previous group and
loads the current one) is **not** `.DisposeWith(this)`'d — a subscription leak tied to the VM lifetime.
Meanwhile a *second*, empty-bodied `WhenAnyValue(x => x.DisplayedGroup).Subscribe(x => { })`
([:49](SynthEBD/Classes_Core/ViewModels/VM_AssetPackDirectReplacerMenu.cs#L49)) does nothing but *is*
disposed. Looks like the `.DisposeWith` landed on the wrong subscription; the empty one is dead and can go.

### `VM_BodyGenConfig` descriptor-deletion handlers — 🐞 possible bug (BodyGen vs BodySlide) + 💭

[VM_BodyGenConfig.cs:330](SynthEBD/Classes_Core/ViewModels/VM_BodyGenConfig.cs#L330),
[:356](SynthEBD/Classes_Core/ViewModels/VM_BodyGenConfig.cs#L356) · In this BodyGen-config editor,
`OnDescriptorCategoryDeletion` strips each subgroup's **BodyGen** descriptors
(`Allowed/DisallowedBodyGenDescriptors`), but `OnDescriptorValueDeletion` strips the **BodySlide**
descriptors (`Allowed/Disallowed/PrioritizedBodySlideDescriptors`) instead. The two sibling handlers
targeting different descriptor families on the same menu looks like a copy-paste from the OBody/BodySlide
equivalent — verify the value-deletion path shouldn't be operating on BodyGen descriptors. Minor extras:
the parameter is misspelled `decriptorSignature`, and `using System.Printing;`
([:6](SynthEBD/Classes_Core/ViewModels/VM_BodyGenConfig.cs#L6)) is an unused import.

### `VM_ConsistencyAssignment.CopyInViewModelFromModel` — 💭 (model mutation during load)

[VM_ConsistencyAssignment.cs:227](SynthEBD/Classes_Core/ViewModels/VM_ConsistencyAssignment.cs#L227) · A
model→VM load method **mutates its source model** — `if (!model.HeadParts.ContainsKey(headPartType)) { model.HeadParts.Add(headPartType, new()); }` —
and only refreshes the VM (`HeadParts[headPartType] = ...GetViewModelFromModel(...)`) in the `else` branch,
so a head-part type missing from the model gets a blank added to the model but the VM keeps its default
(never loaded). Reads as an inverted/asymmetric branch; at minimum, loading shouldn't write back to the model.

### `VM_HeadPart` dead members — 💭

[VM_HeadPart.cs:143-144](SynthEBD/Classes_Core/ViewModels/VM_HeadPart.cs#L143-L144) · The `Clone` and
`ToggleHide` `RelayCommand` properties are declared get-only but never assigned (always null), and a large
commented-out `Clone` block sits just above them. Dead — wire up or remove.

### View-model `.Where(pred).First()/.FirstOrDefault()` — 🔧 modernize

Several round-trip helpers use `collection.Where(x => x.Prop == v).First()` (or `.FirstOrDefault()`), which
should be `collection.First(pred)` / `FirstOrDefault(pred)`:
[VM_BlockedNPC.cs:84](SynthEBD/Classes_Core/ViewModels/VM_BlockedNPC.cs#L84),
[VM_BlockedPlugin.cs](SynthEBD/Classes_Core/ViewModels/VM_BlockedPlugin.cs),
[VM_ConsistencyAssignment.cs:146](SynthEBD/Classes_Core/ViewModels/VM_ConsistencyAssignment.cs#L146),
[VM_BodyGenConfig.cs](SynthEBD/Classes_Core/ViewModels/VM_BodyGenConfig.cs). The `.First()` variants also
throw rather than degrade if the lookup key is absent.

### `VM_HeightConfig` heights stored as strings — 🔧 / 💭

[VM_HeightConfig.cs:177-204](SynthEBD/Classes_Core/ViewModels/VM_HeightConfig.cs#L177-L204) · The
male/female base heights and ranges are held as UI `string`s and re-parsed with `float.TryParse` on every
`DumpViewModelToModel`. This is locale-dependent and defers validation to save time; binding to typed
`float` (or validating on edit) would be more robust.

### `VM_AssetPack` beast-template additional-races path — 🐞 possible bug (wrong owner collection)

[VM_AssetPack.cs:1716](SynthEBD/Classes_Core/ViewModels/VM_AssetPack.cs#L1716) · When adding a beast-race
additional-races path, the new `VM_CollectionMemberString` is constructed with
`DefaultRecordTemplateAdditionalRacesPaths` (the asset-pack-level collection) as its *owner*, yet it is added
to `currentBeastTemplate.AdditionalRacesPaths`. The non-beast sibling at
[:1686](SynthEBD/Classes_Core/ViewModels/VM_AssetPack.cs#L1686) correctly passes the matching owner, so
removing one of these beast entries in the UI would mutate the wrong collection. Copy-paste slip.

### `VM_AssetPack` FormKey `== null` checks are dead — 🔧 / 🐞 (minor)

[VM_AssetPack.cs:869](SynthEBD/Classes_Core/ViewModels/VM_AssetPack.cs#L869),
[:1145](SynthEBD/Classes_Core/ViewModels/VM_AssetPack.cs#L1145) · `DefaultTemplateFK` is a Mutagen `FormKey`
(a struct). `DefaultTemplateFK == null` (:869) is always false and `DefaultTemplateFK != null` (:1145) always
true — the real test is `.IsNull` (already OR-ed in at :869, but the :1145 guard does nothing). Use `.IsNull`.

### `VM_AssetPack` LINQ / robustness nits — 🔧 / 💭

`.Where(pred).First()` / `.FirstOrDefault()` / `.Where(pred).Any()` recur throughout the record-template and
subgroup lookups → `.First(pred)` / `.FirstOrDefault(pred)` / `.Any(pred)`. Also, `DeleteAssetFiles` wraps
its token-file reads in bare `catch { continue; }`, silently swallowing all IO exceptions — a real disk
error during cleanup would be indistinguishable from "no token file". 💭

### `VM_SpecificNPCAssignment` items — 🐞 / 🔧 / 💭

- [VM_SpecificNPCAssignment.cs:518](SynthEBD/Classes_Core/ViewModels/VM_SpecificNPCAssignment.cs#L518) ·
  `CopyInFromModel` has the same head-parts pattern flagged for `VM_ConsistencyAssignment` — it mutates the
  source `model.HeadParts` during a model→VM load and only refreshes the VM for keys already present.
- [:490](SynthEBD/Classes_Core/ViewModels/VM_SpecificNPCAssignment.cs#L490) · stray `break; ;` (dead empty statement).
- [:797](SynthEBD/Classes_Core/ViewModels/VM_SpecificNPCAssignment.cs#L797) ·
  `candidateGroups.Intersect(forcedGroups).ToArray().Length > 0` → `.Any()` (no allocation).
- `LinkAssetPackToForcedAssignment` / `LinkMixInToForcedAssignment` don't `break` after a name match, so a
  duplicate `GroupName` is processed twice (last wins); the `.Where(x => x.GroupName == ...).FirstOrDefault()`
  could be `.FirstOrDefault(pred)`.

### ✅ `VM_BodySlidePlaceHolder.RenameByIndex` — 🐞 RESOLVED (strip exact digit run; note the catalogue example was inaccurate) — see Resolved §B27

[VM_BodySlidePlaceHolder.cs:142](SynthEBD/Classes_Core/ViewModels/OBody%20SubModels/VM_BodySlidePlaceHolder.cs#L142) ·
**Correction:** the original `"Body22"→"Body"` example does *not* reproduce — `GetTrailingInt` collects the
*maximal* trailing digit run, so the char before it is always a non-digit and `TrimEnd` stops at the base
(`"Body22"` correctly becomes `"Body23"`). The real defect is the opposite direction: the char set comes from
the *parsed int* (`selectedCloneIndex.ToString()`), which has dropped leading zeros, so a zero-padded suffix is
*under*-stripped — `"Body007"` → set `{'7'}` → `TrimEnd` leaves `"Body00"` → `"Body008"`. Fixed by stripping the
exact trailing digit run by length.

### `VM_OBodyTrainerExporter` items — 🐞 / 💭

- [VM_OBodyTrainerExporter.cs:286](SynthEBD/Classes_Core/ViewModels/OBody%20SubModels/VM_OBodyTrainerExporter.cs#L286) ·
  the model save path is built with a bare `DateTime.Now.ToString()` (no format/culture), which on most
  locales produces `/` and `:` — invalid Windows path characters — and is culture-dependent. The sibling at
  [:101](SynthEBD/Classes_Core/ViewModels/OBody%20SubModels/VM_OBodyTrainerExporter.cs#L101) does it correctly
  (`"yyyy-MM-dd-HH-mm"` + `InvariantCulture`); :286 should match. 🐞
- The `TrainerExportDTO` class + its `ExportTrainingDTO` builder
  ([:352](SynthEBD/Classes_Core/ViewModels/OBody%20SubModels/VM_OBodyTrainerExporter.cs#L352)) appear unused
  (only the *learning* DTO path is invoked) — verify and remove if dead. The save-dialog filter string
  `"CSV files (.csv|*.csv"` ([:102](SynthEBD/Classes_Core/ViewModels/OBody%20SubModels/VM_OBodyTrainerExporter.cs#L102))
  has a typo'd description (missing `)`), though it still parses as a single valid filter pair. 💭

### BodyGen-config submenu nits — 🔧 / 💭

`VM_BodyGenGroupsMenu.RemoveTemplateGroup` is a get-only `RelayCommand` that appears never assigned (dead/
unwired). Across `VM_BodyGenGroupMappingMenu` / `VM_BodyGenTemplateMenu` / `VM_BodySlideAnnotator`, the usual
`.Where(pred).First()` / `.Where(pred).FirstOrDefault()` / `.Where(pred).Any()` → `.First(pred)` /
`.FirstOrDefault(pred)` / `.Any(pred)` cleanups recur. All minor.

<!-- ENTRIES:Classes_Core_VM -->

---

## Classes_Core (views)

*The .xaml.cs code-behind for the Core editors. Most are trivial InitializeComponent-only controls (a
meaningful class summary + ctor doc was added, replacing the auto-generated "Interaction logic for X.xaml").
The dozen-odd with real logic — numeric-input filters, the asset-pack subgroup-tree drag/drop, and the
assignment previewers — were documented in full.*

### Assignment previewer code-behinds leak a PropertyChanged handler — 🐞 possible bug

[UC_SpecificNPCAssignment.xaml.cs:45](SynthEBD/Classes_Core/Views/UC_SpecificNPCAssignment.xaml.cs#L45),
[UC_ConsistencyAssignment.xaml.cs:41](SynthEBD/Classes_Core/Views/UC_ConsistencyAssignment.xaml.cs#L41) ·
`OnLoaded` does `_parentVM.PropertyChanged += OnParentVMPropertyChanged;` but neither control has an
`Unloaded` handler that unsubscribes. Because these UCs are created/destroyed as the navigation
`DataTemplate` is swapped, reloading the same view re-subscribes (multiplying the handler) and keeps the old
code-behind alive via the VM — a handler leak. Capture the subscription and detach on `Unloaded`.

### `VisualTreeHelpers.NotifyDragDelta` — 💭 (misleading name / dead param; doc corrected here)

[UC_SpecificNPCAssignment.xaml.cs:118](SynthEBD/Classes_Core/Views/UC_SpecificNPCAssignment.xaml.cs#L118) ·
The method's original summary claimed it "listens for SizeChanged … when the width stabilizes," but it
actually handles the bubbling `Thumb.DragCompletedEvent` (this pass corrected the docstring). Its
`column` parameter is never used. Also, `VisualTreeHelpers` is declared as a second top-level class inside a
view's `.xaml.cs` — a general-purpose visual-tree helper that would be easier to find in its own utilities file.

### View code-behind duplication — 🔧 modernize

- `HandleSelectPreviewMouseDown`/`HandleSelectPreviewMouseUp` are copy-pasted verbatim across
  `UC_AssetPack`, `UC_AssetPackSubGroupTreePresenter`, and `UC_AssetReplacerGroup`.
- The `NumericOnly` TextBox handler recurs in ~6 more Core views (in addition to the Classes_Aux ones already
  flagged) — an attached behavior would remove every copy.
- `UC_SpecificNPCAssignment` and `UC_ConsistencyAssignment` code-behinds are near-identical previewer-column
  logic (including a duplicated `525` default-width magic number) — candidates for a shared base/behavior.

### Smaller view items — 💭

- `Window_RuleDeleteExportPicker.xaml.cs` wraps `DialogResult = confirmed;` in `try { } catch { }` —
  a blanket empty catch (guards against non-modal misuse, but hides any unexpected failure).
- `UC_BodyTypeProfileEditor.RegionsGrid_SelectionChanged` is an empty, wired-but-dead handler kept as a
  future hook (the inline comment acknowledges it).

<!-- ENTRIES:Classes_Core_Views -->

---

## Patcher

*The patching engine — per-NPC assignment of assets/body/height/head parts and record/script/NIF output.
Reviewed leaf-first. The newer files here (FaceGenPatcher, TriFileParser, the SourceResolver chain) were
already documented; this pass covers the older undocumented engine code. Started with the internal data
structures and small aux helpers.*

### ✅ `BoolByProbability.Decide` — 🐞 RESOLVED (per-call Random + off-by-one) — see Resolved §B1

[BoolByProbability.cs:21](SynthEBD/Patcher/PatcherAux/BoolByProbability.cs#L21) · Two issues in five lines:
(1) `new Random()` is constructed on **every** call — in a tight per-NPC loop the time-seeded instances
produce correlated/repeated draws; use `Random.Shared` (or a single shared instance). (2) `gen.Next(100)`
yields 0–99 but the test is `prob <= trueProbability`, so for an integer probability `T` it returns true for
`T+1` of the 100 buckets (e.g. `T=50` → 51% true) — a ~1% upward bias, and `T=0` still returns true ~1% of
the time. Use `prob < trueProbability` (with `Next(100)`), or `Next(1,101)`/a `[0,1)` double comparison.

### ✅ `FlattenedAssetPack` subgroup-index guard — 🐞 RESOLVED (off-by-one) — see Resolved §B2

[FlattenedAssetPack.cs:218](SynthEBD/Patcher/Internal%20Data%20Structures/FlattenedAssetPack.cs#L218) ·
`Source.Subgroups.Count >= index && Source.Subgroups[index] != null` — valid indices are `0..Count-1`, so the
`>= index` guard admits `index == Count` and then `Source.Subgroups[index]` throws
`ArgumentOutOfRangeException`. Should be `Count > index`.

### `NPCInfo.AllLinkedNPCGroupInfos` static cache — 🐞 verify (cross-run stale state)

[NPCInfo.cs:169](SynthEBD/Patcher/PatcherAux/NPCInfo.cs#L169) · A `private static HashSet<LinkedNPCGroupInfo>`
accumulates linked-NPC-group infos but is never cleared. In the long-lived standalone UI process this persists
across successive patcher runs, so a second run can see linked-group state from the first. Worth confirming it
is reset at the start of each run (and made instance/scoped state if not).

### Patcher leaf-file smaller items — 🔧 / 💭

- `FlattenedSubgroup.cs:6` — stray `using System.DirectoryServices.ActiveDirectory;` (accidental auto-import;
  unused, pulls in an unrelated assembly namespace). 💭
- `.Where(pred).First()/.FirstOrDefault()` → `.First(pred)/.FirstOrDefault(pred)` recurs
  (`FlattenedAssetPack`, `NPCInfo`, `LinkedNPCGroupInfo`). FormKey comparisons done via `.ToString() ==`
  (`NPCInfo`) can use value equality directly. `output.Select(x => x.Label).Contains(...)` in a loop
  (`FlattenedAssetPack`) is O(n²) — a `HashSet` of seen labels is cleaner. All 🔧 minor.

### ✅ `BlockListHandler` per-type head-part flags — 🐞 RESOLVED (not OR-aggregated) — see Resolved §B3

[BlockListHandler.cs:78](SynthEBD/Patcher/Data%20Mapping/BlockListHandler.cs#L78) · When merging the block
flags contributed by each plugin in an NPC's context chain, the top-level flags (Assets, BodyShape, Height,
…) are correctly OR-aggregated (`if (blockedPlugin.X) output.X = true;` — only ever set true). But the
per-head-part-type loop does `if (blockedPlugin.HeadPartTypes[t]) output[t] = true; else output[t] = false;`
— the `else` **overwrites** a block set by an earlier plugin, so a later plugin that blocks HeadParts but
not a given type clears that type's block. Should set true with no else (or `|=`).

### `ProbabilityWeighting` — 🔧 / 💭 (per-call Random + dead fallback)

[ProbabilityWeighting.cs:28](SynthEBD/Patcher/Shared/ProbabilityWeighting.cs#L28),
[:53](SynthEBD/Patcher/Shared/ProbabilityWeighting.cs#L53),
[:69](SynthEBD/Patcher/Shared/ProbabilityWeighting.cs#L69) · This hot-path selector news up `Random` three
separate times across the two overloads (same time-seeded-correlation issue as `BoolByProbability` — prefer
`Random.Shared`). The first overload also has a fallback block after the weighted pick whose own comment says
"function should always return by this point": it loops an `int` counter over a `double` ProbabilityWeighting
(truncating) and indexes `inputList` by `new Random().Next(weightedSet.Count)` — looks like unreachable
legacy int-only code worth removing.

### Patcher data-mapping / shared smaller items — 🔧 / 💭

- `PatcherSettingsSourceProvider` — stray double semicolon `AppendLine("Source Settings: ");;`;
  `Path.GetDirectoryName(sourcePath)` (nullable) assigned to a non-null path field; and `Initialized` is read
  from the source DTO then unconditionally overwritten to `true`, making the read dead.
- `AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed` compares FormKeys via `.ToString()` rather than
  value equality, and the `Trim*` methods mutate the caller's `allowed`/`required` value sets in place (a
  side effect the names don't advertise).
- `DictionaryMapper` — redundant `Contains`-before-`Add` on `HashSet`s (×3); `GetMorphDictionaryUnion` /
  `MergeDictionaries` keep the first value on key collision (no real value-level union), so the
  "Union"/"Merge" names mislead.
- `EBDScripts.cs` carries an unused `using System.Runtime.Intrinsics.X86;`.

### `HeightPatcher` — 🐞 / 🔧 / 💭

[HeightPatcher.cs:159](SynthEBD/Patcher/Height%20Patching/HeightPatcher.cs#L159) · `npcInfo.ConsistencyNPCAssignment.Height = assignedHeight;`
dereferences `ConsistencyNPCAssignment` under only a `bEnableConsistency` guard — if consistency is enabled
but the assignment object is null this NREs (confirm it is always created when consistency is on). 🐞
[:128](SynthEBD/Patcher/Height%20Patching/HeightPatcher.cs#L128) news up a `Random` per NPC inside the
assignment loop (clock-seeded → correlated sequences for closely-timed calls; prefer a shared instance). 🔧
`WriteAssignmentDictionaryScriptMode` ([:289](SynthEBD/Patcher/Height%20Patching/HeightPatcher.cs#L289)) is
dead — an unconditional `return; // currently handled by SkyPatcher` at the top makes the whole body (and the
`_scriptHeightAssignments` field) vestigial. Intentional but worth pruning. 💭

### `AssetAssignmentJsonDictHandler` / `EBDCoreRecords` — 🐞 / 💭

- `AssetAssignmentJsonDictHandler` — bare `catch { }` blocks swallow the exception entirely (only a generic
  message is logged; the actual error text is lost), `Dictionary.Add` is used where a duplicate original-NPC
  FormKey would throw (vs the indexer), and `MessageWindow.DisplayNotificationOK(...)` pops a UI dialog from
  engine/IO code. 🐞/💭
- `EBDCoreRecords` — a `FormKey.TryFactory` result for the player reference is ignored, so `SetTo(...)` runs
  even if parsing failed (the sibling `FaceTextureScriptWriter` checks and logs); two code paths
  (`CreateHeadPartKeyword` and the inline creation) can create the same EBD keyword EditorID. 💭

### Patcher asset-helper LINQ/struct nits — 🔧

`ArmorPatcher` filters `armorGetter.Armature.Where(x => x.FormKey != null)` — `FormKey` is a non-nullable
struct so the predicate is always true (and the `.ToArray()` is needless). `.Where(pred).First()/.Any()` →
`.First(pred)/.Any(pred)` recurs across `PathTrimmer`, `SkinPatcher`, `AssetReplacerSelector`,
`HeadPartAuxFunctions`, `BodyGenPreprocessing`; `PathTrimmer`/others are static-only helper classes not marked
`static`. All minor.

### ✅ `CombinationLog.LogStaticAssignments` references MixInFemale twice — 🐞 RESOLVED — see Resolved §B4

[CombinationLog.cs:85](SynthEBD/Patcher/PatcherAux/CombinationLog.cs#L85) ·
`assetPacks.PrimaryMale.And(assetPacks.MixInFemale).And(assetPacks.PrimaryFemale).And(assetPacks.MixInFemale)`
lists `MixInFemale` **twice** and never `MixInMale`, so male mix-in packs are omitted from the combination
log/stats and female mix-ins are double-counted. The third `.And(...)` should be `assetPacks.MixInMale`.

### ✅ `VerboseLoggingNPCSelector` allowed/disallowed AttributeGroups mismatch — 🐞 RESOLVED — see Resolved §B5

[VerboseLoggingNPCSelector.cs:77](SynthEBD/Patcher/PatcherAux/VerboseLoggingNPCSelector.cs#L77) · The
*allowed*-attributes check passes `_patcherState.GeneralSettings.AttributeGroups`
([:70](SynthEBD/Patcher/PatcherAux/VerboseLoggingNPCSelector.cs#L70)) but the *disallowed*-attributes check
passes `_patcherState.OBodySettings.AttributeGroups` — a copy-paste slip; the disallowed check should use the
same `GeneralSettings.AttributeGroups`, otherwise disallowed-attribute logging rules resolve their groups
against the wrong (OBody) group set.

### Patcher writers/parsers smaller items — 🐞 / 💭

- `EasyNPCProfileParser` — `FormKey.TryFactory(str)` returns a non-nullable `FormKey`, so the `!= null` guard
  is always true (the bool `out`-overload was likely intended; malformed input throws instead of being
  skipped); `AppearanceDictionary.Add` throws on a duplicate NPC key (unlike `NPC2ProfileParser`'s
  `ContainsKey` guard). 🐞
- `CombinationLog.LogAssignedRecords` ([:224](SynthEBD/Patcher/PatcherAux/CombinationLog.cs#L224)) does
  `Signature.Split(':')[1]` with no guard, unlike the guarded sibling at :177 — `IndexOutOfRange` if a
  signature lacks ':'. 🐞
- `OBodyWriter` script-mode `TryAdd(key, entry.Value.First())` throws when a tracker list is empty (the ini
  path guards with `if (!entry.Value.Any()) continue;`). 🐞
- `AssetReplacerHardcodedPaths` — the female right-gash `_11`/`_12` entries map to editor IDs ending
  `...LeftGashR` while the male equivalents use `...RightGashR`; possible left/right mislabel — verify against
  the RaceMenu head-part records. 🐞 verify
- Pervasive empty `catch { }` that logs a generic message but discards the exception text (`OBodyWriter`,
  `HeadPartWriter`, `NPC2ProfileParser`, `RaceMenuIniHandler`); `Task.Run` fire-and-forget writes
  (`BodyGenWriter`, `CombinationLog`); `RaceMenuIniHandler.GetIniLine` matches by bare `StartsWith` (a setting
  that is a prefix of another can match the wrong line); `OBodyWriter._loadOrderCaseSensitive` is assigned but
  never read (dead). 💭

### ✅ `AssetAndBodyShapeSelector.ClearStatusFlags` — 🐞 RESOLVED (no-op + redundant; removed) — see Resolved §B8

[AssetAndBodyShapeSelector.cs:371](SynthEBD/Patcher/Shared/AssetAndBodyShapeSelector.cs#L371) · This method
does nothing: (1) `flags` is a **by-value** parameter, so reassigning it never reaches the caller; and (2)
each line is `flags = ~BodyShapeSelectorStatusFlag.X`, which *replaces* `flags` with the complement of one
flag (i.e. sets every other bit) rather than clearing that bit — and the three lines just overwrite each
other. To actually clear bits it would need `flags &= ~X` on a `ref` parameter (or return the new value).
Any caller relying on it to reset status flags is silently getting nothing.

### Patcher core-logic items — 🐞 / 🔧 / 💭

- `UpdateHandler` [:331](SynthEBD/Patcher/PatcherAux/UpdateHandler.cs#L331) — a migration does
  `...FirstOrDefault(...).GroupedSubAttributes.First().Attribute as VM_NPCAttributeMod` with no null check on
  the `FirstOrDefault`, so an unexpected settings shape NREs mid-migration. 🐞
- `PatcherExt` [:202](SynthEBD/Patcher/PatcherAux/PatcherExt.cs#L202) — `recordsToDuplicate.Contains(dup)`
  tests the freshly-created duplicate `Npc` against the original source enumerable by reference, so it is
  ~always false and the guarded branch never fires (dead/ineffective condition). 🐞
- `UniqueNPCData` [:97](SynthEBD/Patcher/PatcherAux/UniqueNPCData.cs#L97) —
  `UniqueNameExclusions.Contains(npcName, StringComparer.CurrentCultureIgnoreCase)` uses the LINQ `Contains`
  (O(n) scan, bypassing the HashSet's O(1) lookup) and a culture-sensitive comparer inconsistent with the
  set's default-ordinal construction. Also heavy repeated triple-dictionary indexing
  (`[name][race][gender]`) that a single `TryGetValue` chain would simplify. 🔧
- `AttributeMatcher` — the per-sub-attribute `Not` handling
  `(matched && !Not) || (!matched && Not)` is just `matched ^ Not`; `ResolveAllContexts<...>` is re-run per
  sub-attribute per NPC (hot-path cost across the load order); `dynamic ==` string comparisons could be
  `string.Equals`. All 🔧/💭, not correctness bugs.

### ✅ `HeadPartSelector` duplicate condition — 🐞 RESOLVED (unreachable warning + dead FormKey null checks) — see Resolved §B9

[HeadPartSelector.cs:199](SynthEBD/Patcher/Head%20Part%20Patching/HeadPartSelector.cs#L199) · The `else if`
repeats the **exact** condition of the preceding `if` at
[:193](SynthEBD/Patcher/Head%20Part%20Patching/HeadPartSelector.cs#L193)
(`specificAssignmentSetting != null && specificAssignmentSetting.ResolvedHeadPart != null`), so the else
branch is unreachable and its warning — "…calls for {FormKey} but this head part does not currently exist in
the load order" — never fires. The intended condition was `ResolvedHeadPart == null`. Also
[:708-709](SynthEBD/Patcher/Head%20Part%20Patching/HeadPartSelector.cs#L708): `headPartAssignment == null` /
`assetAssignment == null` compare `FormKey` (a struct) to null — always false/true — so those conflict-
resolution early-outs are dead (use `.IsNull`).

### ✔️ `VanillaBodyPathSetter` `&&`/`||` precedence — VERIFIED NOT A BUG (intentional; documented in-code) — see Resolved §B10

[VanillaBodyPathSetter.cs:216](SynthEBD/Patcher/Asset%20Patching/VanillaBodyPathSetter.cs#L216) ·
`if (!bSkyPatcherModeAssets && contexts.Count == 2 || contexts.Count == 1)` parses as
`(!SkyPatcher && Count==2) || Count==1`, so the branch fires whenever `contexts.Count == 1` regardless of
SkyPatcher mode — the trailing comment ("base mod and output mod only") implies the SkyPatcher guard was meant
to cover both counts: `!SkyPatcher && (Count == 2 || Count == 1)`. Bracket it.

### ✅ `BodyGenSelector` / `OBodySelector` priority not applied — 🐞 RESOLVED — see Resolved §B6

- `BodyGenSelector.ChooseMorphs` computes a ForceIf-prioritized grouping of combinations, but the actual
  `ProbabilityWeighting.SelectByProbability(...)` is run over the **full** available-combination set rather
  than the current priority group — so the highest-ForceIf tier isn't actually preferred. Verify against the
  intended ForceIf semantics.
- [OBodySelector.cs:465](SynthEBD/Patcher/OBody%20Patching/OBodySelector.cs#L465) (and the equivalent in
  BodyGenSelector) — `priorities.OrderBy(x => x.Priority);` discards its result (LINQ `OrderBy` is
  non-mutating), and the following loop consumes `priorities` in unsorted order, so descriptor priority is
  never applied. Assign the result (`priorities = priorities.OrderBy(...).ToList()`).

### Selector log/style nits — 💭 / 🔧

- `OBodySelector` log lines [:97](SynthEBD/Patcher/OBody%20Patching/OBodySelector.cs#L97),
  [:112](SynthEBD/Patcher/OBody%20Patching/OBodySelector.cs#L112) concatenate a `List<BodySlideSetting>`
  directly (and `String.Join` over the objects), so the report prints the type name rather than the preset
  labels — `.Select(x => x.Label)` is needed. Plus log typos "desecriptor"/"Presests".
- `VanillaBodyPathSetter` — stray `using System.DirectoryServices.ActiveDirectory;`, a FormKey compared via
  `.ToString() ==` (line ~55), and `ArmatureHasVanillaPath` returns `true` ("assume vanilla / skip") on an
  unresolvable path — an inverted-return readability trap. 💭

### ✅ `AssetSelector.CombinationAllowedBySpecificNPCAssignment` forced IDs not validated — 🐞 RESOLVED — see Resolved §B7

[AssetSelector.cs:1189](SynthEBD/Patcher/Asset%20Patching/AssetSelector.cs#L1189),
[:1203](SynthEBD/Patcher/Asset%20Patching/AssetSelector.cs#L1203) · The Primary and MixIn cases loop
`foreach (var id in specificAssignment.SubgroupIDs)` but the body —
`if (!selectedCombination.ContainedSubgroups.Select(x => x.Id).Any()) return false;` — never references
`id`. `.Select(x => x.Id).Any()` is just `.Any()`, so the check only verifies the combination is *non-empty*,
not that each forced subgroup ID is actually present (the Replacer case compares per index correctly). So a
forced/specific NPC assignment's subgroup IDs aren't really enforced for Primary/MixIn packs.

### `RecordGenerator` — 🔧 / 💭

[RecordGenerator.cs:60-61](SynthEBD/Patcher/Asset%20Patching/RecordGenerator.cs#L60) · Double self-assignment
`CachedObjectsByPathAndTemplate = CachedObjectsByPathAndTemplate = new ...` (and the same on the next line) —
an accidental copy-paste; collapse to a single assignment. More broadly, `RecordGenerator` holds several
**mutable `static` dictionaries** of cross-NPC dedup state (reset in `Reinitialize()`), so it is non-reentrant
— a hazard if per-NPC processing is ever parallelized. `IncrementEditorID` keys the dedup dict on
`EditorID ?? "NoEditorID"` but appends to the possibly-null `EditorID` (so `null + "0001"` loses the
placeholder). 💭

### `HardcodedRecordGenerator` (deprecated) — 🐞 / 💭

The class header notes it is currently deprecated, and the `AssignSpecialCaseAssetReplacer` chain is dead
(caller commented out) — but two issues stand out if it's ever revived:
`AssignSkinTexture` keys its record cache on `npcInfo.NPC.HeadTexture.FormKey` (a **head** texture) for a
**skin** texture record (likely copy-paste — head and skin caches collide on the same NPC key); and a failed-
resolution `else` calls `OutputMod.Armors.Remove(newSkin)` with `newSkin` still null. Also several
`AssignArmorAddon` return values are captured into unused locals, and FormKey membership is tested via
`.Select(x => x.FormKey.ToString()).Contains(...)` (string compares). 💭

### `Patcher` orchestrator items — 🐞 / 💭

- [Patcher.cs:1473](SynthEBD/Patcher/Patcher.cs#L1473) · `FormatEntry` computes
  `(assignablePairing.Assigned * 100 / assignablePairing.Assignable).ToString("N2")` — `Assigned`/`Assignable`
  are ints, so this is **integer** division and the `"N2"` decimals are always `.00` (e.g. 1 of 3 prints
  `33.00%`, not `33.33%`). Cast to `double` before dividing. 🐞
- [Patcher.cs:1216](SynthEBD/Patcher/Patcher.cs#L1216) · dead debug stub left in the head-part region:
  `if (currentNPCInfo.Name.StartsWith("Uthgerd")) { int n = 0; }` — hardcoded NPC name + no-op body; remove. 💭
- `timer_Tick` appears to have no subscriber (dead leftover from the old timer-based status update); two
  user-facing status strings read "Made/Applied **seleections**" (typo); `AppearsHumanoidByArmature` mutates
  its lists with manual `i--`/`Remove` index fix-ups (clumsy, set-difference would be clearer). 💭

### `DeepCopyByExpressionTrees` — 💭 (third-party utility)

`ReferenceEqualityComparer.GetHashCode` calls `obj.GetHashCode()` rather than
`RuntimeHelpers.GetHashCode(obj)`; since its `Equals` is reference identity, an overridden value-based
`GetHashCode` on a key type makes the hash inconsistent with the equality (benign for the reference-tracking
dictionary here, but semantically it should use identity hashing). Adapted third-party code; low priority.

<!-- ENTRIES:Patcher -->

---

## Settings

*The settings persistence layer (JSON load/save handlers, validators, source DTOs) and the settings models /
view models / views. Reviewed leaf-first, starting with the SettingsIO handlers.*

### ✅ `SettingsIO_BodyGen` female loop iterates Male — 🐞 RESOLVED (single Male.Concat(Female) loop via AddMissingAttributeGroups) — see Resolved §B18

[SettingsIO_BodyGen.cs:205](SynthEBD/Settings/SettingsIO/SettingsIO_BodyGen.cs#L205) · After the male loop
`foreach (var maleConfig in loadedPacks.Male)` ([:194](SynthEBD/Settings/SettingsIO/SettingsIO_BodyGen.cs#L194)),
the "female" loop is `foreach (var femaleConfig in loadedPacks.Male)` — it iterates **Male** again. So the
general-settings attribute groups are never merged into the female BodyGen configs, and the male configs are
processed twice. Should be `loadedPacks.Female`.

### ✅ `SettingsIO_Misc` update-log fallback gated on the wrong file — 🐞 RESOLVED (both loaders route through SelectExistingPath) — see Resolved §B19

[SettingsIO_Misc.cs:94](SynthEBD/Settings/SettingsIO/SettingsIO_Misc.cs#L94) · The update-log fallback branch
is `else if (File.Exists(_paths.GetFallBackPath(_paths.ConsistencyPath)))` but its body loads
`_paths.UpdateLogPath` ([:96](SynthEBD/Settings/SettingsIO/SettingsIO_Misc.cs#L96)). So the update-log
fallback is taken based on whether the *consistency* fallback file exists, not the update-log one (copy-paste
from the consistency loader above). The matching error toast also names `ConsistencyPath`.

### Settings IO smaller items — 🐞 / 💭

- `IO_Aux.SelectFileSave` / `SettingsIO_AssetPack` save path — the dialog `out path` is assigned before the
  method returns the dialog's bool result, so on **cancel** the caller still receives a populated path it may
  treat as written. Callers must check the bool. 🐞
- `SettingsIO_Height` parses zEBD height strings (always `.`-decimal) with culture-sensitive
  `float.TryParse`, so comma-decimal locales silently drop the value. 💭
- Empty `catch { }` blocks that swallow the exception (`SettingsIO_BodyGen` legacy load,
  `SettingsIO_General` error-dump); UI dialogs (`MessageWindow`) invoked from the IO/validation layer
  (`OnLoadValidator`); and the `AttributeGroups.Select(x => x.Label).Contains(...)` linear merge repeated
  across the AssetPack/BodyGen/OBody loaders (a `HashSet` of labels would be O(1)). 💭/🔧

### ✅ `Settings_General` default race aliases duplicate Imperial — 🐞 RESOLVED (both lists fixed + 1.0.7.0 migration for existing settings) — see Resolved §B20

[Settings_General.cs:219-220](SynthEBD/Settings/Settings_General/Settings_General.cs#L219) · The default
`RaceAliases` list contains `DefaultRaceAliases.RaceAliasCotR_Imperial` **twice** and never
`RaceAliasCotR_ImperialVampire`. Every other Creation-of-the-Realm race in the list pairs a base alias with a
`_Vampire` variant, so line 220 was almost certainly meant to be `RaceAliasCotR_ImperialVampire` — as written,
the CotR Imperial-Vampire race is left un-aliased by default.

### Settings model smaller items — 💭

- `Settings_ModManager` initializes `TempExtractionFolder` from
  `Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)` with no null guard; `GetEntryAssembly()` can be
  null and `.Location` is empty under single-file publish (the project currently publishes
  `PublishSingleFile=false`, so it's populated — but fragile). 💭
- `DefaultAttributeGroups` — the `MatureFace` group's user-facing `Label` is "Can Get **Mildy** Older Face"
  ("Mildly" misspelled). `Settings_General.BlockedModsFromImport`'s trailing comment implies the base-master
  block is SkyPatcher-mode-only, but the default list is unconditional — confirm the consumer scopes it. 💭

### ✅ `VM_TexMeshBatchActions` batch-apply ignores the selection — 🐞 RESOLVED (both apply loops filter on IsSelected) — see Resolved §B21

[VM_TexMeshBatchActions.cs:40](SynthEBD/Settings/Settings_TexMesh/TexMeshBatchActions/VM_TexMeshBatchActions.cs#L40) ·
`ApplyAsAllowedAttribute` and `ApplyAsDisallowedAttribute` do `foreach (var assetPack in AssetPacks)` and add
the attribute to **every** pack — they never consult `assetPack.IsSelected`. But the VM also exposes
`SelectAll`/`DeselectAll` commands and a per-pack `IsSelected` checkbox (the wrapper rows), whose only purpose
is to choose which packs the batch action targets. So the selection UI is inert and the action always applies
to all packs. Should iterate `AssetPacks.Where(x => x.IsSelected)`.

### `VM_SpecificNPCAssignmentsUI.DumpViewModelToModels` null-deref — 🐞 possible bug

[VM_SpecificNPCAssignmentsUI.cs:254](SynthEBD/Settings/Settings_SpecificNPCAssignments/VM_SpecificNPCAssignmentsUI.cs#L254) ·
Dereferences `CurrentlyDisplayedAssignment.AssociatedPlaceHolder.AssociatedViewModel.DumpViewModelToModel()`
with no null guard on `AssociatedViewModel`. The selection-change path nulls `AssociatedViewModel` (to free the
heavy editor VM) while leaving `CurrentlyDisplayedAssignment` set, so a save in that window NREs.

### Settings VM smaller items — 💭

- `VM_BlockListUI` and `VM_SpecificNPCAssignmentsUI` both set a malformed save-dialog filter
  `"JSON files (.json|*.json"` (missing `)`, wrong pattern — should be `"JSON files (*.json)|*.json"`). 💭
- `VM_SettingsModManager.UpdatePatcherSettings` guards with `if (this != null)` — always true, dead check;
  `VM_Settings_Headparts._environmentProvider` is assigned but never read (dead field), and `Types` is
  populated only in `Initialize()` so the View* commands NRE if it's skipped. 💭
- `VM_SettingsOBody.CopyInViewModelFromModel` has several unused (dead) parameters, and its descriptor value-
  deletion compares `x.ToLabelSignature().ToString()` for BodySlides but `x.ToString()` for subgroup
  descriptors — verify the two stringifications agree or the subgroup cleanup may miss entries. 🔧/💭

### Settings views — 💭 (minor)

`UC_Settings_General.xaml.cs` declares `private bool _isDragging;` with no handlers or usages (also flagged by
the compiler) — dead field. A couple of Settings views also carried auto-generated `Interaction logic for
X.xaml` summaries naming a *different* XAML file (`UC_SettingsTexMesh` said `UC_BodyGenSettings.xaml`) — a
clone-from-sibling smell, now replaced with real summaries.

<!-- ENTRIES:Settings -->

---

## GUI_Aux

*WPF/UI infrastructure: value converters, markup extensions, attached behaviors, small helpers, the
record-path intellisense support, image-preview handling, path resolution, and the Config Drafter /
Config Path Remapper tooling. Reviewed leaf-first (helpers/converters first).*

### `AnnotationStateComputer.IsAnnotated` is misnamed / redundant — 💭

[AnnotationStateComputer.cs:43](SynthEBD/GUI_Aux/AnnotationStateComputer.cs#L43) · `IsAnnotated` actually
returns whether any substate is **None** (i.e. *un*-annotated) — the opposite of its name. In
`ComputeAnnotationState` the `if (!IsAnnotated(...)) state = None;` block is then both confusingly named and
redundant (the default `state` is already `None`, and the `hasManual`/`hasRulesBased` blocks below override
it). The net result is correct, but the inverted name + dead block are a readability trap.

### GUI_Aux converter/helper smaller items — 💭 / 🔧

- `VisibilityConverters` — the enum-driven converters' `ConvertBack` returns a **bool**, not the original
  enum (`BodyShapeSelectionMode`/`DrafterTextureSource`/`ExchangeMode`); harmless while one-way, wrong if ever
  two-way bound. `MaxHeightConverter.Convert` casts `(double)value` unconditionally (throws on a non-double
  source). 💭
- `RecordIntellisense.RefreshPathSuggestions` dereferences `parent.IntellisensedPath` *before* its
  `if (parent is null …) return;` guard, so the null-parent guard is dead. 💭
- `Converters` — `zEBDSignatureToFormKey`'s `default`/error branch logs `fkString` while it is still empty
  (the offending value is omitted); `RaceEDID2FormKey` case-folds with culture-sensitive `.ToLower()` per
  iteration (use `OrdinalIgnoreCase`). 🔧
- `ImagePreviewHandler.ResizeImage` swallows exceptions to `Console.WriteLine` (not the app `Logger`), so
  resize failures never reach the log. `LongPathHandler` defines `MAX_PATH = 200` (misleading name; real limit
  is 260) and splits paths on `'\'` only (mixed/forward separators break the walk). 💭

### `VM_SelectableSubgroupShell` — 🐞 / 💭

[VM_SelectableSubgroupShell.cs:20](SynthEBD/GUI_Aux/ViewModels/VM_SelectableSubgroupShell.cs#L20) · The
null-`subgroup` branch does `defaultSelectedStatus = false;` — a dead write to the *parameter* immediately
before `return`; it almost certainly meant `IsSelected = false;`. Separately, this class (unlike its sibling
VMs) does **not** inherit `VM`/implement `INotifyPropertyChanged`, so `IsSelected` may not raise change
notifications for a two-way checkbox binding (PropertyChanged.Fody only weaves INPC types).

### GUI_Aux small-VM smaller items — 💭

- `VM_LogDisplay` sets a malformed save filter `"Text files (.txt|*.txt"` (missing `)`), rebuilds the entire
  `DispString` by re-joining the whole log on every append (O(n) per event), and swallows clipboard/file
  exceptions. `VM_ConfigRemapperMissingPaths.DisplayedSubgroups` is never populated or read (dead member).
  `VM_ConfigRemapperTextureComparer` calls an `async void InitializeImage` from its constructor (unobservable
  exceptions, load races binding) and has a dead `DrawFilledRectangle`. All 💭.

### `VM_ConfigPathRemapper.GetMatchingDirCount` wrong null-fallback — 🐞 bug (minor)

[VM_ConfigPathRemapper.cs:606](SynthEBD/GUI_Aux/ViewModels/VM_ConfigPathRemapper.cs#L606) · `split2` is built
from `(Path.GetDirectoryName(path2) ?? path1)` — the null fallback uses **path1**, not path2 (copy-paste from
the line above). When path2 has no directory component, the similarity score is computed against path1's own
segments instead, skewing the path-similarity tiebreak. Should be `?? path2`. (The `new HashSet<string>(cmp) { array }`
initializer is fine — it binds to Noggog's `Add(IEnumerable)` extension and unions the segments.)

### `ConfigDrafter.CleanRedundantSubgroups` non-decremented index — 🐞 possible bug

[ConfigDrafter.cs:309](SynthEBD/GUI_Aux/ConfigDrafter.cs#L309) · The recursion removes
`currentSubgroup.Subgroups[i]` when the child collapses but does **not** decrement `i`, so the element after a
removed one is skipped — two adjacent collapsible children won't both be cleaned. Notably the same file uses
the `i--`-after-remove pattern elsewhere (e.g. the body→feet/tail and multiplet loops), so this omission looks
like an oversight. Verify.

### GUI_Aux config-tooling smaller items — 🔧 / 💭

- `VM_ConfigDrafter` — dead `unmatchedTextures` locals; the archive-extraction result (`succes`, also
  misspelled) is ignored so extraction failures are swallowed; `ComputeFileDuplicates` MD5-hashes every file
  with no try/catch (a locked file throws out of the background task). 🔧/💭
- `ConfigDrafter` — `public string SuccessString = "Success";` is a mutable public field used for value
  comparison (should be `const`); many `StartsWith`/`Contains`/`Replace` calls omit `StringComparison`
  (culture-sensitive); `new CultureInfo("en-US")` is constructed per call in `CapitalizeWordsPreserveCapitalized`. 🔧
- `VM_SubgroupLinker` — `if (!GetTopLevelIndex()) { }` has an empty body (a stray `///`), so failure to locate
  the target subgroup is silently ignored (leaves `_topLevelIndex == -1`). 💭

### GUI_Aux views — 💭 (minor)

`Window_ConfigPathRemapper.UpdateRowHeights` hardcodes the starting grid row index as `4` and then walks the
grid's `Expander` children incrementing the row — coupling the code-behind to the exact XAML row layout, so
inserting a non-expander row before row 4 (or reordering expanders) would silently misalign the resize logic.

<!-- ENTRIES:GUI_Aux -->

---

## Installer

*Downloads, installs, and packages shareable asset-pack config bundles (the ConfigInstaller engine, the
installer-wizard VMs, and the Packager tooling).*

### `ConfigInstaller.InstallConfigFile` extraction-failure falls through — 🐞 possible bug

[ConfigInstaller.cs](SynthEBD/Installer/ConfigInstaller.cs) · The `try { ExtractArchive(...) } catch (Exception ex)`
around archive extraction logs/shows the error but does **not** `return` (every other failure branch in the
method returns the empty result tuple). So after an extraction exception, execution falls through to the
`Manifest.json` lookup in the empty/partial temp folder, and the user sees "Could not find Manifest.json …"
instead of an extraction-failed message. Add a `return` in that catch.

### `ConfigInstaller` long-path mapping overwritten per pack — 🐞 verify

The `assetPathMapping` `out` value is reassigned on each asset-pack iteration of the install loop, so only the
**last** pack's path-length remapping survives — yet it is consumed downstream as if global. Multi-pack
manifests that need long-path remapping may mis-map files belonging to earlier packs. Worth verifying against a
multi-pack bundle.

### Installer smaller items — 💭 / 🔧

- `VM_PackagerOption` strips the manifest root via `path.Replace(ParentManifest.RootDirectory, "")` — a
  substring replace anywhere in the path, so a root-dir name recurring deeper would be wrongly stripped; use
  `Path.GetRelativePath`. 💭
- `VM_DestinationFolderSelector.UpdateWarningMessage` mutates `manifest.DestinationModFolder` as a side effect
  of a display-refresh method (surprising). `VM_DownloadCoordinator.PopulateDownloadInfo` recurses
  `Directory.GetDirectories` with no try/catch (an inaccessible subdir aborts the whole scan). 💭
- `ConfigInstaller` uses culture-sensitive `ToLower()` on a file extension (vs the file's usual
  `OrdinalIgnoreCase`), has a dead post-increment (`newFileNameIndex++`), and a "charactersl" status typo. 🔧

### Installer views — 💭 (minor)

`UC_DownloadCoordinator.Hyperlink_RequestNavigate` passes the raw `NavigateUri` straight into an `explorer.exe`
argument with no scheme validation — fine for the hardcoded http(s) links in use, but a data-driven URI
(local path / file scheme) would also be opened. `Window_ConfigPackager.HandleSelectPreviewMouseDown/Up`
share the same triplicated tree-select helper flagged in the Classes_Core views.

<!-- ENTRIES:Installer -->

---

## App composition / root

*The entry point, Autofac wiring, central state, persistence, and shell view models.*

### `App.xaml.cs` crash-handler / startup nits — 🐞 / 💭

[App.xaml.cs:232](SynthEBD/App.xaml.cs#L232) · `"Installation Location: " + Assembly.GetEntryAssembly()?.Location ?? "Failed to locate."`
— `+` binds tighter than `??`, so the left operand of `??` is the already-concatenated (non-null) string and
the `"Failed to locate."` fallback is dead; a null `Location` prints an empty location. Wrap the
`?.Location ?? "…"` in parens. Also: the crash handler dereferences `_settingsSourceProvider` with no null
guard (unlike the adjacent `_logger`/`_patcherState` guards), so a crash before that field resolves makes the
crash handler itself NRE and lose the report; and a `Task.Run(...).Wait()` inside an `async void` handler
blocks the UI thread. 🐞/💭

### `SaveLoader` null/guard items — 🐞

`LoadPlugins` dereferences `_patcherState.GeneralSettings.RaceGroupings` with no null check — if the
general-settings load failed, this NREs. `LoadInitialSettings` discards every loader's `out loadSuccess`
(aggregate failure is ignored), and both BodyGen save calls share the caption "Error saving BodyGen configs"
so a failure can't be attributed to male vs female. 🐞/🔧

### `MainModule` duplicate registration — 🔧

[MainModule.cs:222](SynthEBD/MainModule.cs#L222), [:274](SynthEBD/MainModule.cs#L274) ·
`builder.RegisterType<VM_SpecificNPCAssignment>().AsSelf();` is registered **twice** — redundant (the second
wins). Also `RegisterBuildCallback` configures a *static* `SynthEbdViewerHostStateRegistry` from the container,
which re-runs each time a startup path builds a fresh container (static global config across containers).

### Root composition smaller items — 💭

- `PatcherState.Version` is a mutable `public static string` (should be `const`/`static readonly`).
- `ViewModelLoader` injects several never-used factory/field dependencies (dead ctor deps), and its
  `Observable.CombineLatest` subscription fires `Reinitialize()` *during construction* (CombineLatest emits
  immediately) — a full settings+VM load as a constructor side effect.
- `VM_RunButton` takes the same `PatcherState` singleton via two params (`_patcherState` and `_state`);
  `MainWindow_ViewModel` exposes a `public readonly _paths` field specifically for crash logging (leaky).
- `FirstLaunch` carries an accidental `using static System.Windows.Forms.AxHost;` and uses
  `File.Exists`+`File.Copy(...,false)` (TOCTOU). 💭

<!-- ENTRIES:Root -->

---
