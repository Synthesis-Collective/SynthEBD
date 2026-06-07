# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What SynthEBD is

SynthEBD is a standalone WPF patcher (the Mutagen-based successor to the zEBD zEdit patcher) for **controlled randomization of NPC appearance** in Skyrim SE/AE/VR. It assigns four largely independent axes to NPCs: **Assets** (textures/meshes), **Body Shape** (BodySlide presets or BodyGen morphs), **Head Parts**, and **Height**. At its core it feeds data into the "Everybody's Different Redone" (EBD) runtime framework, and additionally writes plugin records, FaceGen NIFs, Papyrus script JSON, and SkyPatcher ini directives. It can run standalone or as a Synthesis patcher.

## Build, run, test

The solution targets **.NET 8** (WPF, `net8.0`, Windows-only). Use the same commands as CI ([.github/workflows/publish.yml](.github/workflows/publish.yml)):

```powershell
dotnet restore SynthEBD.sln
dotnet build SynthEBD.sln -c Release --no-restore
dotnet publish SynthEBD/SynthEBD.csproj -c Release -p:PublishSingleFile=false
```

Tests use **xUnit** + **FluentAssertions**. Note `SynthEBD.Tests` is **not** included in `SynthEBD.sln`, so target the test project directly:

```powershell
dotnet test SynthEBD.Tests                                                   # all tests
dotnet test SynthEBD.Tests --filter "FullyQualifiedName~BodySlideGroupClassifier"   # subset
dotnet test SynthEBD.Tests --filter "FullyQualifiedName=SynthEBD.Tests.BodySlideGroupClassifierTests.NoCatalogs_ReturnsUnknown"  # single
```

Tests cover the BodySlide group classifier (ML.NET-based), form-key replacement logic, and slider-catalog regeneration.

### Build gotcha

[SynthEBD/SynthEBD.csproj](SynthEBD/SynthEBD.csproj) references `gong-wpf-dragdrop` via a NuGet `PackageReference` only. (It previously also carried a hardcoded local `<Reference>` HintPath into `E:\Downloads\...`, which broke builds on any other machine; that local reference was removed in commit `e64f6a98`, so no manual fix is needed anymore.) Ignore the stray `SynthEBD_pwwzxdzj_wpftmp.csproj` (a transient WPF temp project).

## Solution structure

- **SynthEBD/** — the main WPF/Synthesis application (WinExe entry point).
- **CharacterViewer.Rendering/** — standalone OpenTK/OpenGL 4.3 renderer library (replaced HelixToolkit), referenced by SynthEBD via `ProjectReference`.
- **SynthEBD.Tests/** — xUnit test project (not in the .sln).
- **BatchConfigUpdater/** — auxiliary tool.

## Architecture of the main app (SynthEBD/)

**Composition / entry point.** [App.xaml.cs](SynthEBD/App.xaml.cs) is the entry point and wires up **Autofac** DI via [MainModule.cs](SynthEBD/MainModule.cs) (registers singletons like `Logger`, `PatcherState`, `SaveLoader`, settings IO handlers, plus factory delegates for transient VMs). There are three startup paths driven by the Synthesis pipeline: standalone UI, `OpenForSettings` (settings UI inside Synthesis), and `CanRunPatch` (validation). `SynthEBDPaths` resolves all settings/output directories.

**MVVM + reactive conventions.**
- All view models inherit from [Classes_Aux/ViewModels/VM.cs](SynthEBD/Classes_Aux/ViewModels/VM.cs) (implements `INotifyPropertyChanged` + `IDisposableDropoff`, owns a `CompositeDisposable`).
- **PropertyChanged.Fody** auto-weaves `INotifyPropertyChanged` — do not hand-write `OnPropertyChanged` for simple auto-properties.
- **ReactiveUI** observable chains: `this.WhenAnyValue(x => x.Prop).Subscribe(...).DisposeWith(this)` — always dispose subscriptions via the VM's dropoff.
- Views bind to VMs through type-routed `DataTemplate`s; navigation swaps the displayed VM ([MainWindow_ViewModel.cs](SynthEBD/MainWindow_ViewModel.cs), `NavPanel/`).

**Settings model + persistence.** Each feature has a POCO model in `Settings/Settings_*/Settings_*.cs`, a view model (`VM_*`), and an IO handler in `Settings/SettingsIO/SettingsIO_*.cs` that serializes via `JSONhandler<T>` to JSON on disk. The flow is **VM ⇄ Model**: load JSON → model → VM at startup ([SaveLoader.cs](SynthEBD/SaveLoader.cs)), and `DumpViewModelToModel()` → save JSON on persist. `PatcherState` is the central runtime state container.

**Attribute groups & race groupings — local-vs-General resolution (key modularity design).** Distribution rules throughout the app — asset-pack subgroups, whole-config rules, BodyGen/OBody/HeadPart rules, and the verbose-logging NPC selector — gate NPCs by **NPC attributes** (`NPCAttribute`) and **race groupings**. An attribute may reference a named **attribute group** by label (`NPCAttributeGroup.SelectedLabels`); a rule references named **race groupings** by label (`Allowed/DisallowedRaceGroupings`, a `HashSet<string>`). These named definitions deliberately exist at **two scopes**:

- **Main / General** — `GeneralSettings.AttributeGroups` and `GeneralSettings.RaceGroupings`: the centralized, user-managed set.
- **Local / plugin** — every shareable config carries its *own* copies so a downloaded config can ship newly-defined groups it relies on: `AssetPack.AttributeGroups` / `AssetPack.RaceGroupings`, `BodyGenConfig.AttributeGroups`, `OBodySettings.AttributeGroups`.

This duality is the backbone of the design's modularity: **local** definitions let users *share config files and distribution rules* that reference attributes/race-groups the recipient hasn't defined, while the **General** set enables *centralized management*. Two toggles pick precedence (both default **on**): `GeneralSettings.OverwritePluginAttGroups` and `OverwritePluginRaceGroups` — when on, a General definition **supersedes** a local one of the same label; when off, the local/plugin definition is used.

Attribute-group resolution is centralized in `NPCAttribute.GetAttributeGroupByLabel(label, localSet, patcherState, logger)`: if `OverwritePluginAttGroups`, return the matching `GeneralSettings.AttributeGroups` entry; otherwise fall back to the caller's `localSet`. At **load**, the SettingsIO handlers (`SettingsIO_AssetPack`/`_BodyGen`/`_OBody`) also copy each General group into a config's local set for any label it lacks (local-wins on collision), so the local set is a superset fallback.

**Convention when wiring an attribute-gated feature:** pass the feature's *own local set* to `AttributeMatcher.MatchNPCtoAttributeList` as the group source — `subgroup.ParentAssetPack.Source.AttributeGroups` (AssetSelector), `bodyGenConfig.AttributeGroups` (BodyGenSelector), `OBodySettings.AttributeGroups` (OBodySelector), etc. For a **General-level** feature (e.g. the verbose-logging NPC selector) pass `GeneralSettings.AttributeGroups`. **Always use the same group set for a rule's Allowed *and* Disallowed checks** — a mismatch there was bug **B5**.

**Known asymmetry (race groupings).** Race groupings have the parallel toggle (`OverwritePluginRaceGroups`) and per-config local lists, but — unlike attribute groups — currently lack an analogous `GetRaceGroupingByLabel` resolver *and* the load-time General→local merge. So race-grouping label resolution does **not** yet honor this local/General/toggle design symmetrically (tracked as **B48** in [CODE_REVIEW_NOTES.md](CODE_REVIEW_NOTES.md)).

**The patching pipeline.** [Patcher/Patcher.cs](SynthEBD/Patcher/Patcher.cs) `RunPatcher()` is the orchestrator. It runs the assignment axes per-NPC and then generates output. Key stages and their classes:
- Asset selection: `Patcher/Asset Patching/AssetSelector.cs`, `AssetAndBodyShapeSelector`
- Body shape: `Patcher/BodyGen Patching/BodyGenSelector.cs`, `Patcher/OBody Patching/OBodySelector.cs`
- Height: `Patcher/Height Patching/HeightPatcher`
- Head parts: `Patcher/Head Part Patching/HeadPartSelector`
- Record output: `Patcher/Asset Patching/RecordGenerator.cs` (WornArmor/HeadTexture overrides)
- FaceGen NIF baking: `Patcher/Asset Patching/FaceGenPatcher.cs`
- Runtime directives: `Patcher/PatcherAux/SkyPatcherInterface.cs`

Patcher behavior is governed by a **16-case truth table** (documented in `Patcher.cs` comments) over: asset patching mode (Script vs Nif) × asset SkyPatcher flag × headpart patching mode × headpart SkyPatcher flag. This decides whether surrogate records are made, NIFs are baked, scripts emitted, and which SkyPatcher ini lines are written. Read those comments before changing patch-mode logic.

**Mutagen/Synthesis.** Records are read/written through **Mutagen.Bethesda** (`ILinkCache<ISkyrimMod, ISkyrimModGetter>` for lookups, `state.LoadOrder` for the load order). **Synthesis.WPF** provides the patcher state and the `OpenForSettings`/`CanRunPatch`/`RunPatch` callbacks.

## CharacterViewer.Rendering

A self-contained real-time 3D character renderer (also consumed by NPC Plugin Chooser 2 — see [NPC_PLUGIN_CHOOSER_2_INTEGRATION.md](NPC_PLUGIN_CHOOSER_2_INTEGRATION.md)). It parses Skyrim NIFs via **niflysharp**, CPU-skins geometry, applies BodySlide `.tri`/`.osd` morphs (`NifMeshBuilder` + `BodySlideDeformer`), decodes DDS textures via **Pfim**, and renders with OpenGL — diffuse/normal/skin-tint/FaceTint/env maps, shadow mapping, SSAO, ACES tone-mapping. Off-screen PNG export uses ImageShar­p. It embeds into WPF through `OpenTK.GLWpfControl`, bridged by host adapters in [SynthEBD/CharacterViewerHost/](SynthEBD/CharacterViewerHost/) (`SynthEbdNpcMeshDataSourceAdapter`, `WpfDispatcherMarshaller`, etc.) that adapt SynthEBD's `NpcMeshResolver` to neutral interfaces.

### Rendering conventions (important)

- **GLSL shader files must be pure ASCII.** `.vert`/`.frag` files in `CharacterViewer.Rendering/Shaders/` cannot contain em-dashes, curly quotes, or non-breaking spaces — the NVIDIA compiler reports them as a misleading "unexpected $end at token EOF". Shaders are shipped via `<Content>` copy rules to `bin/.../Shaders/`.
- **Keep `RENDERING_PIPELINE.md` in lockstep** with renderer changes — update it in the *same* change, not as a follow-up. It is the authoritative reference for the NIF parsing → material → lighting → post-process pipeline.
- **Skyrim NIF axis convention:** the character faces **negative Z**; "frontmost" vertex = **minimum Z**, not maximum.
- Retain disabled debug-visualization shader flags and diagnostic logging rather than stripping them after a fix.

## Repo conventions

- `.editorconfig`: UTF-8, CRLF line endings. `CS4014` (unawaited Task) is an **error**; `CS1998` and `CS1591` are silenced.
- `Nullable` and `ImplicitUsings` are enabled in the main project. `CharacterViewer.Rendering` is added as a global `<Using>`, so SynthEBD files reference its types without per-file `using` directives.
