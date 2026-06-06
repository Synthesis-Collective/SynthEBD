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

### `FormKeyHashSetComparer.Equals` / `ModKeyHashSetComparer.Equals` — 🔧 modernize

[FormKeyHashSetComparer.cs:7](SynthEBD/General_Aux/FormKeyHashSetComparer.cs#L7) · Hand-rolled
O(n²) nested-loop set equality. `HashSet<T>.SetEquals` is built-in, O(n), and clearer. The two
classes are otherwise identical — a single generic `KeyHashSetComparer<T>` (or just calling
`SetEquals` at the call sites) would remove the duplication entirely.

### `FormKeyHashSetComparer.Contains` / `ModKeyHashSetComparer.Contains` — 🐞 footgun / 🔧 modernize

[FormKeyHashSetComparer.cs:27](SynthEBD/General_Aux/FormKeyHashSetComparer.cs#L27) · The body
calls `Equals(formkey, toMatch)` with two `FormKey`s, but the only `Equals` defined here takes
`(HashSet<FormKey>, HashSet<FormKey>)`. So this silently binds to `object.Equals(object, object)`,
not the intended method. It happens to be correct (FormKey is a value-equal struct), which makes it
the most correct *and* the most confusing line in the file. The whole method is equivalent to
`collection.Contains(toMatch)`. Recommend deleting it in favor of `HashSet<T>.Contains`.

### `ComparableSetHashCode` (both comparers) — 🔧 modernize

[FormKeyHashSetComparer.cs:39](SynthEBD/General_Aux/FormKeyHashSetComparer.cs#L39) · XOR is
commutative, so the `OrderBy(x => x.ToString())` does no work toward order-independence (and the
`ToString()` sort is the expensive part). The `first`-flag special case is also unnecessary since
`0 ^ x == x`. Reduces to `e.Aggregate(0, (h, k) => h ^ k.GetHashCode())`.

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

### `_7ZipInterface.GetArchiveContents` — 🐞 possible bug (dead error check)

[7ZipInterface.cs:113](SynthEBD/General_Aux/7ZipInterface.cs#L113) · A `StringBuilder standardOutputCapture`
is declared, but the `OutputDataReceived` handler appends each line to `outputLines`, never to
`standardOutputCapture`. So `outputStr = standardOutputCapture.ToString()` is always empty and the
`Contains("Can't open as archive")` failure check can never fire here — corrupt-archive failures slip
through as an empty content list. (The sibling `ExtractArchive` appends correctly.)

### `_7ZipInterface.ExtractArchive` — 🐞 possible bug (null callback)

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

### `MiscFunctions.StringHashSetsEqualCaseInvariant` — 🐞 edge case + 🔧 modernize

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

### `PatchableRaceResolver` — 🐞 possible bug + 🔧 modernize

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

### `BSAHandler` — 🐞 possible bug (out param never set)

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

### `RecordPathParser` array-index bounds guard — 🐞 possible bug (×2)

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

### `RecordPathParser.RemovePairedParens` — 🐞 possible bug

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

### `MiscValidation` ini parsers — 🐞 possible bug (wrong guard)

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

### `NPCAttribute*.CloneAsNew` — 🐞 possible bug (shallow copy + dropped fields)

[NPCAttribute.cs:263](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L263) (and the other 10) · Most
`CloneAsNew` factories assign the collection by reference — `output.FormKeys = input.FormKeys;` — so
the "clone" shares the *same* `HashSet` as the original; mutating one mutates the other. Only
`NPCAttributeCustom` deep-copies (and only its Record branch). Separately, several clones **drop
fields**: every type omits `Not`, and `NPCAttributeMisc.CloneAsNew`
([:789](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L789)) omits `Mood`, `Aggression`, `EvalGender`,
and `NPCGender`. If these are used for UI "duplicate" actions, the duplicate silently diverges.

### `NPCAttribute.Equals(NPCAttribute)` — 🐞 possible bug (order-dependent set compare)

[NPCAttribute.cs:28](SynthEBD/Classes_Aux/Models/NPCAttribute.cs#L28) · Compares `SubAttributes` by
`ToArray()` then index-by-index, but `SubAttributes` is an unordered `HashSet`. Two attributes with the
same sub-attributes enumerated in different order would compare unequal (and, paired with the XOR
`GetHashCode`, could land in a set inconsistently). Use `SetEquals`, or order both sides the same way
the hash does.

### `NPCAttributeCustom.GetHashCode` — 🐞 possible NRE

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
