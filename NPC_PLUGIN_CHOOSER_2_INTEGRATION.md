# Integrating CharacterViewer.Rendering into NPC Plugin Chooser 2

This document is the handoff for wiring SynthEBD's `CharacterViewer.Rendering` module into NPC Plugin Chooser 2 (NPC2). The goal is to replace NPC2's C++ `NPC Portrait Creator` subprocess with an in-process .NET renderer that produces equivalent (or better) mugshots, plus a live preview window the user can manually fine-tune.

The intended user-facing change in NPC2's Settings → Mugshots panel:

- New **Renderer** combobox: `Internal` (default) | `NPC Portrait Creator (Legacy)`.
- `Legacy` keeps every existing setting + behavior unchanged.
- `Internal` shows a live `CharacterViewer` preview with a yellow rectangle marking the crop region that ends up in the saved PNG. The user adjusts camera rotation/pan/zoom, lighting, and background color directly in the preview; tweaks persist as global mugshot defaults.
- A **Reset** button restores defaults.
- Default framing **mirrors NPC Portrait Creator's algorithm exactly** — head + hair-above-head's-bottom — via the new `CameraFraming.MeshAware` API.
- The user can either accept the dynamic auto-framing or switch to a manual-override mode where their drag-to-pan/zoom values are saved verbatim.

---

## 1. What you're consuming

The reusable surface lives in `S:\Dev\SynthEBD\CharacterViewer.Rendering\` — a self-contained class library with no Mutagen, no PatcherState, no SynthEBD references. NPC2 references it via project reference:

```xml
<!-- NPC Plugin Chooser 2.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\SynthEBD\CharacterViewer.Rendering\CharacterViewer.Rendering.csproj" />
</ItemGroup>
```

Key types you'll touch:

| Type | Purpose |
|---|---|
| `VM_CharacterViewer` | The interactive viewer ViewModel. Bound to your XAML preview window. |
| `IOffscreenRenderer` / `OffscreenRendererFactory.Create(...)` | The headless mugshot generator. |
| `OffscreenRenderRequest` | Per-render config (paths, camera, lighting, background, output size). |
| `CameraFraming.MeshAware` | Hair-aware auto-framing mode. Composes `FramingShape` selectors + filters; replaces Portrait Creator's algorithm. |
| `CameraFraming.OrbitState` | Lossless explicit orbit-camera placement (Distance/Azimuth/Elevation/Target). Use this for manual-mode UI bindings — no eye-to-orbit conversion. |
| `MeshAwareCameraFitter.ApplyTo(vm, framing, w, h)` | Public static helper. Applies a `MeshAware` framing to a live `VM_CharacterViewer`'s `Camera`. The preview UC calls this so the on-screen framing matches what the offscreen renderer will produce. |
| `ResolvedNpcMeshPaths` | Mutagen-free POCO carrying body/hand/feet/head paths + NPC weight + hair color. |
| Six `I*` host abstractions (logger, settings, data folder, BSA, NPC mesh data source, render-thread marshaller) | NPC2 implements these against its own infrastructure. |

---

## 2. Host adapters NPC2 must implement

`CharacterViewer.Rendering` consumes these interfaces; SynthEBD has its own implementations in `SynthEBD/CharacterViewerHost/Adapters/` that you can use as references. NPC2 implements one per service.

### `ICharacterViewerLogger`
Routes diagnostic messages into NPC2's existing log sink.

```csharp
internal sealed class NpcChooserViewerLoggerAdapter : ICharacterViewerLogger
{
    private readonly /* your logger */ _inner;
    public NpcChooserViewerLoggerAdapter(/* your logger */ inner) => _inner = inner;
    public void LogMessage(string m) => _inner.LogInformation(m);
    public void LogError(string m) => _inner.LogError(m);
    public void LogError(string m, Exception ex) => _inner.LogError(ex, m);
}
```

### `ICharacterViewerSettings`
Bidirectionally syncs lighting layout / color scheme / verbose-log toggle with NPC2's settings storage. Must implement `INotifyPropertyChanged` and raise it when properties change.

```csharp
internal sealed class NpcChooserSettingsAdapter : ICharacterViewerSettings, INotifyPropertyChanged
{
    private readonly Settings _settings;
    public NpcChooserSettingsAdapter(Settings settings) => _settings = settings;

    public string CharacterViewerLightingLayout
    {
        get => _settings.MugshotLightingLayoutName ?? "";
        set
        {
            if (_settings.MugshotLightingLayoutName == value) return;
            _settings.MugshotLightingLayoutName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterViewerLightingLayout)));
        }
    }
    // ... same pattern for CharacterViewerLightingColorScheme, CharacterViewerVerboseLog
    // ... UserLightingLayouts / UserLightingColorSchemes are IList<T>; back them with
    //     ObservableCollection<T> so the viewer's WhenAnyValue subscriptions fire.
    public event PropertyChangedEventHandler? PropertyChanged;
}
```

### `IDataFolderProvider`
Slim view of the host's environment — just the Skyrim Data folder path plus an opaque "load order changed" token.

```csharp
internal sealed class NpcChooserDataFolderAdapter : IDataFolderProvider
{
    private readonly EnvironmentStateProvider _env;
    public NpcChooserDataFolderAdapter(EnvironmentStateProvider env) => _env = env;
    public string DataFolderPath => _env.DataFolderPath;
    public object? CurrentLoadOrderToken => _env.LinkCache;  // reference identity; new instance = invalidate caches
}
```

### `IBsaArchiveProvider`
Locate-and-extract for assets inside BSAs. NPC2 already has a `BsaHandler`; wrap it.

```csharp
internal sealed class NpcChooserBsaProviderAdapter : IBsaArchiveProvider
{
    private readonly BsaHandler _bsa;
    public NpcChooserBsaProviderAdapter(BsaHandler bsa) => _bsa = bsa;

    public void EnsureAllArchivesOpened() => _bsa.EnsureAllArchivesOpened();

    public bool TryLocateInBsa(string subpath, out string? containingBsaPath)
        => _bsa.TryFindFile(subpath, out containingBsaPath);

    public bool TryExtractToDisk(string subpath, string destPath)
        => _bsa.TryExtractToDisk(subpath, destPath);
}
```

### `INpcMeshDataSource`
Resolves an `NpcIdentity` (a string cache key) to a `ResolvedNpcMeshPaths`. NPC2 implements this against its own NPC resolution code (which lives independently of SynthEBD's `NpcMeshResolver` — they're two parallel implementations of the same conceptual operation).

```csharp
internal sealed class NpcChooserNpcMeshDataSourceAdapter : INpcMeshDataSource
{
    private readonly EnvironmentStateProvider _env;
    private readonly /* NPC2's mesh resolver */ _resolver;

    public NpcChooserNpcMeshDataSourceAdapter(/* injected */) { /* ... */ }

    public ResolvedNpcMeshPaths? Resolve(NpcIdentity identity)
    {
        // identity.CacheKey is a string-encoded FormKey by convention.
        if (!FormKey.TryFactory(identity.CacheKey, out var fk)) return null;
        var paths = _resolver.ResolveForNpc(fk, _env.LinkCache);
        if (paths == null) return null;

        return new ResolvedNpcMeshPaths
        {
            BodyMeshPath = paths.BodyNif,
            HandsMeshPath = paths.HandsNif,
            FeetMeshPath = paths.FeetNif,
            HeadMeshPath = paths.HeadNif,
            Sex = paths.IsFemale ? Sex.Female : Sex.Male,
            SkeletonPath = paths.SkeletonNif,
            ResolutionChains = paths.ResolutionDiagnostics ?? new(),
            TxstTextures = paths.TxstByPart ?? new(),
            FaceTintPath = paths.FaceTintDdsPath,
            TextureLightingColor = paths.QnamRgb,
            NpcWeight = paths.Weight,
            NpcBaseHeight = paths.HeightScale,
            HairColorRgb = paths.HclrRgb,
        };
    }

    public object? CurrentInvalidationToken => _env.LinkCache;
}
```

### `IRenderThreadMarshaller`
SynthEBD's WPF host wraps `Application.Current.Dispatcher.Invoke`. NPC2 does the same — same UI framework, same pattern.

```csharp
internal sealed class WpfDispatcherMarshaller : IRenderThreadMarshaller
{
    public void Invoke(Action a) => Application.Current.Dispatcher.Invoke(a);
}
```

The offscreen renderer doesn't need this — `OffscreenRendererFactory.Create(...)` constructs its VM with `InlineRenderThreadMarshaller` (the default), so `Application.Current` is never touched.

---

## 3. DI registration (Splat.Autofac)

Register the adapters and the leaf services from `CharacterViewer.Rendering`. NPC2 already uses Autofac; this slots into `App.xaml.cs`'s container build.

```csharp
// Host adapters
builder.RegisterType<NpcChooserViewerLoggerAdapter>().As<ICharacterViewerLogger>().SingleInstance();
builder.RegisterType<NpcChooserSettingsAdapter>().As<ICharacterViewerSettings>().SingleInstance();
builder.RegisterType<NpcChooserDataFolderAdapter>().As<IDataFolderProvider>().SingleInstance();
builder.RegisterType<NpcChooserBsaProviderAdapter>().As<IBsaArchiveProvider>().SingleInstance();
builder.RegisterType<NpcChooserNpcMeshDataSourceAdapter>().As<INpcMeshDataSource>().SingleInstance();
builder.RegisterType<WpfDispatcherMarshaller>().As<IRenderThreadMarshaller>().SingleInstance();

// CharacterViewer.Rendering leaves
builder.RegisterType<CharacterViewerLogGate>().AsSelf().SingleInstance();
builder.RegisterType<GameAssetResolver>().AsSelf().SingleInstance();
builder.RegisterType<BsdFileParser>().AsSelf().SingleInstance();
builder.RegisterType<BodyTriFileParser>().AsSelf().SingleInstance();
builder.RegisterType<BodySlideDeformer>().AsSelf().SingleInstance();
builder.RegisterType<CharacterPreviewCache>().AsSelf().SingleInstance();
builder.RegisterType<VM_CharacterViewer>().AsSelf();  // transient — one per preview window

// The offscreen renderer is a managed singleton so the GameWindow + FBO
// are amortized across many mugshot renders. Its lifetime is tied to the
// container; the host disposes it on shutdown.
builder.Register(c => OffscreenRendererFactory.Create(
        c.Resolve<CharacterPreviewCache>(),
        c.Resolve<BodySlideDeformer>(),
        c.Resolve<BsdFileParser>(),
        c.Resolve<BodyTriFileParser>(),
        c.Resolve<GameAssetResolver>(),
        c.Resolve<ICharacterViewerSettings>(),
        c.Resolve<CharacterViewerLogGate>(),
        c.Resolve<ICharacterViewerLogger>()))
    .As<IOffscreenRenderer>()
    .SingleInstance();
```

> **GLFW main-thread requirement:** `OffscreenRendererFactory.Create(...)` must be called from the process's main (WPF UI) thread on first use. The simplest path: don't lazy-resolve it during a background-thread mugshot job. Instead, eagerly resolve `IOffscreenRenderer` once during app startup (e.g. in `App.OnStartup` after `Build()` completes) so the GameWindow is constructed on the WPF thread. Subsequent renders from any thread work fine — GL contexts are portable; only GLFW init has the main-thread constraint.

---

## 4. Settings.cs schema additions

```csharp
public enum MugshotRenderer
{
    Internal,            // default
    LegacyPortraitCreator,
}

// Inside Settings:
public MugshotRenderer SelectedRenderer { get; set; } = MugshotRenderer.Internal;

// Internal-renderer-specific config block. Persisted as one nested object
// so the legacy options aren't shadowed when the user toggles back.
public InternalMugshotSettings InternalMugshot { get; set; } = new();

public sealed class InternalMugshotSettings
{
    // Camera mode: Auto (CameraFraming.MeshAware, hair-aware) or Manual
    // (user's saved Distance/Azimuth/Elevation/Target).
    public InternalMugshotCameraMode CameraMode { get; set; } = InternalMugshotCameraMode.Auto;

    // Auto-mode tunables (mirror Portrait Creator's existing knobs).
    public float HeadTopFraction { get; set; } = 0.95f;     // top of bbox at 95% of frame height
    public float HeadBottomFraction { get; set; } = 0.10f;
    public float Yaw { get; set; } = 180f;                  // facing the camera
    public float Pitch { get; set; } = 0f;                  // straight on
    public float HairAbovePadding { get; set; } = 0f;       // extra padding around hair-above-head
    public bool IncludeAccessories { get; set; } = true;    // include hair / brows / mouth in framing

    // Manual-mode camera state (saved when the user drags the preview).
    public float ManualDistance { get; set; } = 200f;
    public float ManualAzimuth { get; set; } = 180f;
    public float ManualElevation { get; set; } = 0f;
    public float ManualTargetX { get; set; } = 0f;
    public float ManualTargetY { get; set; } = 120f;
    public float ManualTargetZ { get; set; } = 0f;

    // Lighting: the named preset the user selected in the preview's
    // lighting dropdown. The actual layout/scheme objects come from
    // CharacterViewerLightingPresets + ICharacterViewerSettings.UserLighting*.
    public string LightingLayoutName { get; set; } = "";    // empty = default
    public string LightingColorSchemeName { get; set; } = "";

    // Background — the FBO clear color.
    public byte BackgroundR { get; set; } = 105;
    public byte BackgroundG { get; set; } = 105;
    public byte BackgroundB { get; set; } = 105;

    // Output dimensions — the saved PNG size. The preview window is fixed
    // at the host's UI size (e.g. 600×800); the yellow rectangle inside
    // it shows the crop region that gets saved at this resolution.
    public int OutputWidth { get; set; } = 512;
    public int OutputHeight { get; set; } = 512;
}

public enum InternalMugshotCameraMode { Auto, Manual }
```

---

## 5. Settings UI — the Renderer combobox

In `SettingsView.xaml`, inside the existing Mugshots groupbox (around the `UsePortraitCreatorFallback` checkbox today), add:

```xml
<StackPanel Orientation="Vertical" Margin="0,8,0,0">
    <DockPanel>
        <TextBlock Text="Renderer:" VerticalAlignment="Center" Margin="0,0,8,0"/>
        <ComboBox SelectedValue="{Binding SelectedRenderer}"
                  SelectedValuePath="Tag"
                  HorizontalAlignment="Left" Width="240">
            <ComboBoxItem Content="Internal" Tag="{x:Static settings:MugshotRenderer.Internal}"/>
            <ComboBoxItem Content="NPC Portrait Creator (Legacy)"
                          Tag="{x:Static settings:MugshotRenderer.LegacyPortraitCreator}"/>
        </ComboBox>
    </DockPanel>

    <!-- Visible only when Internal is selected. The existing Portrait Creator
         options panel below should bind Visibility to the Legacy choice. -->
    <ContentControl Content="{Binding InternalMugshotEditor}"
                    Visibility="{Binding IsInternalRenderer, Converter={StaticResource BoolToVis}}"/>

    <ContentControl Content="{Binding LegacyPortraitCreatorEditor}"
                    Visibility="{Binding IsLegacyRenderer, Converter={StaticResource BoolToVis}}"/>
</StackPanel>
```

`VM_Settings.IsInternalRenderer` returns `SelectedRenderer == MugshotRenderer.Internal` and similarly for legacy. Bind the existing Portrait Creator UI block (delete settings, max parallel, etc.) inside the Legacy ContentControl so the existing UX is byte-identical when the user picks Legacy.

---

## 6. The Internal preview window

Build a UserControl `UC_InternalMugshotPreview.xaml` that hosts `UC_CharacterViewer` from SynthEBD (or your own minimal copy — see § 6.1) and overlays the yellow crop rectangle.

### 6.1 Reusing SynthEBD's UC_CharacterViewer

SynthEBD's `UC_CharacterViewer.xaml` lives in `s:\Dev\SynthEBD\SynthEBD\Classes_Aux\Views\` and references `IntEqualsToVisibilityConverter` + the BB-pick classifier UI (which NPC2 doesn't need). Two options:

1. **Copy the XAML, strip the classifier sections** — the toolbar, status bar, GL viewport, and lighting controls all carry over verbatim. Delete the `BoxCriterionValues` ObjectDataProvider, the BB-pick toolbar buttons, and the `Picks` ListBox.
2. **Write a minimal NPC2 XAML from scratch** — just `<glWpf:GLWpfControl/>` + a couple of toolbar buttons (lighting dropdown, background-color picker, reset). The viewer's public surface (`InitializeGl`, `LoadByIdentityAsync`, `Renderer`, `Camera`, `SelectedLightingLayout`, etc.) is fully bindable without any SynthEBD types.

Option 2 is cleaner and gets you a NPC2-specific look. ~150 lines of XAML.

### 6.2 The yellow crop overlay

The preview is rendered at the WPF UC's logical size (whatever you give it in XAML — say 600×800). The saved PNG is rendered offscreen at `OutputWidth × OutputHeight` (e.g. 512×512). The yellow rectangle shows where the offscreen render's framing band would land if overlaid on the live preview at the same camera state.

Geometry: if the preview camera and the offscreen camera have **the same orbit params** (Distance/Azimuth/Elevation/Target), the projected character is at the same world position. The yellow rect is the intersection of the offscreen FOV with the preview's image plane:

- Offscreen output is `(W_out, H_out)` at vertical FOV `fov`.
- Preview is `(W_prev, H_prev)` at the same vertical FOV.
- The offscreen aspect ratio determines how much horizontal ground it covers vs. the preview's aspect.
- Vertical extent matches (same FOV) — the rect's height is `H_prev` × `(min(W_out/W_prev * H_prev/H_out, 1))`.
- Horizontal extent is `W_prev × (W_out/H_out × H_prev/W_prev)` clamped to [0, W_prev].

In practice: render the rect as a `<Rectangle>` overlay on top of the GLWpfControl with `Stroke="#FFFF00"` `StrokeThickness="2"` and bind `Width`/`Height` to the computed crop size. Recompute on viewport size change AND on `OutputWidth/OutputHeight` change.

### 6.3 Auto vs Manual camera mode

The combobox `Camera` (Auto / Manual) toggles between:

- **Auto**: viewer's mouse-orbit handlers are detached. After each load (and on every Auto-mode tunable change), the preview calls `MeshAwareCameraFitter.ApplyTo(_vm, BuildPortraitCreatorAlgorithm(cfg), previewW, previewH)`. User-visible knobs are `HeadTopFraction`, `HeadBottomFraction`, `Yaw`, `Pitch`, `HairAbovePadding`, `IncludeAccessories` — sliders/numerics in the editor panel that recompute the framing immediately.
- **Manual**: mouse-orbit handlers are attached (forward `OrbitCamera.OnMouseDown/Move/Up/Wheel` from the GLWpfControl's mouse events — see SynthEBD's `UC_CharacterViewer.xaml.cs` lines 22–25 + ~640 for the 4-line wiring). The VM's `Camera.Distance/Azimuth/Elevation/Target` are bidirectionally bound to `InternalMugshotSettings.Manual*` so the values persist on drag-end. Auto-framing is bypassed; the saved state IS the framing, and the offscreen renderer reads it back via `CameraFraming.OrbitState` for byte-identical rendering.

**Switching Auto → Manual**: copy the current auto-computed camera values (`_vm.Camera.Distance` etc.) into the Manual fields so the user starts from the auto framing they were just looking at. Then attach the mouse handlers.

**Switching Manual → Auto**: detach the mouse handlers. Don't clear the Manual values — keep them saved so toggling back to Manual restores the user's previous tweak. Re-fire `MeshAwareCameraFitter.ApplyTo(...)` to display the auto framing.

### 6.4 Reset button

```csharp
// In InternalMugshotEditorVM:
public ICommand ResetCommand => new RelayCommand(_ => true, _ =>
{
    Settings.InternalMugshot = new InternalMugshotSettings();   // re-instantiate at defaults
    RaisePropertyChanged(nameof(Settings));                     // refresh bindings
    RefreshPreview();
});
```

---

## 7. Wiring up the offscreen renderer

When NPC2 generates mugshots (existing batch path), pick the renderer based on the setting:

```csharp
public async Task GenerateMugshotAsync(FormKey npc, string outputPath)
{
    if (_settings.SelectedRenderer == MugshotRenderer.LegacyPortraitCreator)
    {
        // Existing PortraitCreator subprocess path — unchanged.
        await _legacyPortraitCreator.GenerateAsync(npc, outputPath);
        return;
    }

    // Internal renderer:
    var identity = new NpcIdentity(npc.ToString(), npc.ToString());
    var paths = _npcMeshDataSource.Resolve(identity)
                ?? throw new InvalidOperationException($"Could not resolve mesh paths for {npc}");

    var request = BuildRequest(paths, _settings.InternalMugshot);
    byte[] png = await _offscreenRenderer.RenderToPngAsync(request);
    await File.WriteAllBytesAsync(outputPath, png);
}
```

### `BuildRequest` — translating settings into an `OffscreenRenderRequest`

```csharp
private OffscreenRenderRequest BuildRequest(ResolvedNpcMeshPaths paths, InternalMugshotSettings cfg)
{
    return new OffscreenRenderRequest
    {
        MeshPaths = paths,
        Width = cfg.OutputWidth,
        Height = cfg.OutputHeight,
        BackgroundRgb = (cfg.BackgroundR, cfg.BackgroundG, cfg.BackgroundB),

        // Lighting: look up by name from the viewer's preset list, or
        // fall back to defaults if the saved name no longer exists.
        Lighting = ResolveLayout(cfg.LightingLayoutName) ?? CharacterViewerLightingPresets.DefaultLayout,
        Colors = ResolveColors(cfg.LightingColorSchemeName) ?? CharacterViewerLightingPresets.DefaultColorScheme,

        Camera = cfg.CameraMode == InternalMugshotCameraMode.Manual
            ? BuildOrbitStateFromManual(cfg)
            : BuildPortraitCreatorAlgorithm(cfg),
    };
}

// Mirrors NPC Portrait Creator's framing exactly: head + hair-above-head's-bottom.
private static CameraFraming BuildPortraitCreatorAlgorithm(InternalMugshotSettings cfg)
{
    var shapes = new List<FramingShape>
    {
        // Primary head — the face mesh.
        new() { Selector = FramingShapeSelector.PrimaryHead.Instance },
    };

    if (cfg.IncludeAccessories)
    {
        // Hair / brows / mouth / eyes — but only the portion above the
        // primary head's lower bound. Floor-length braids don't blow out
        // the frame; eyebrows and mouth (which sit above head's bottom)
        // pass through unchanged.
        shapes.Add(new FramingShape
        {
            Selector = FramingShapeSelector.HeadAccessories.Instance,
            Filter = FramingShapeFilter.AboveLowerYOfPrimaryHead.Instance,
            Padding = cfg.HairAbovePadding,
        });
    }

    return new CameraFraming.MeshAware(
        shapes,
        FrameTopFraction: cfg.HeadTopFraction,
        FrameBottomFraction: cfg.HeadBottomFraction,
        Yaw: cfg.Yaw,
        Pitch: cfg.Pitch);
}

private static CameraFraming BuildOrbitStateFromManual(InternalMugshotSettings cfg)
{
    // Manual camera state is OrbitCamera-native (Distance, Azimuth, Elevation,
    // Target). CameraFraming.OrbitState takes the same shape verbatim, so
    // there's no lossy conversion — the saved PNG's framing matches the
    // preview's framing bit-for-bit.
    return new CameraFraming.OrbitState(
        Distance:  cfg.ManualDistance,
        Azimuth:   cfg.ManualAzimuth,
        Elevation: cfg.ManualElevation,
        TargetX:   cfg.ManualTargetX,
        TargetY:   cfg.ManualTargetY,
        TargetZ:   cfg.ManualTargetZ);
}
```

### Live preview ↔ offscreen consistency

For the yellow crop rectangle to show *exactly* where the saved PNG will frame, the live preview and the offscreen renderer must produce the same projection. Both code paths share the same canonical helpers — there's no duplicate math to keep in sync:

1. **Auto mode** — preview applies the framing via `MeshAwareCameraFitter.ApplyTo(vm, framing, previewW, previewH)` after each `LoadByIdentityAsync` completes (and after any tunable change). The offscreen renderer calls the same fitter internally on every render. Same input → same camera state → same projection.

2. **Manual mode** — preview's `OrbitCamera` is the source of truth. The user drags / scrolls / pans; preview saves `Distance / Azimuth / Elevation / Target` into `InternalMugshotSettings.Manual*` on drag-end. The offscreen renderer takes those values verbatim via `CameraFraming.OrbitState` (no lossy eye-to-orbit conversion), so the saved PNG matches the preview exactly.

3. **Lighting + background** — both paths read from `ICharacterViewerSettings` (host adapter) and the request's `BackgroundRgb`, so they're already in lockstep.

```csharp
// In NPC2's preview UC, when an Auto-mode tunable changes:
private void OnAutoModeTunablesChanged()
{
    var framing = BuildPortraitCreatorAlgorithm(_settings.InternalMugshot);
    MeshAwareCameraFitter.ApplyTo(_vm, framing, _previewSurfaceWidth, _previewSurfaceHeight);
    // _vm.Camera now reflects the same framing the offscreen renderer
    // would apply for this NPC at this preview size. Yellow crop overlay
    // recomputes from _vm.Camera + the offscreen output W/H.
}
```

---

## 8. Walking through the user flow

1. User opens Settings → Mugshots. **Renderer** combobox defaults to `Internal`.
2. NPC2 instantiates `VM_CharacterViewer` (transient — fresh per preview window) and binds it to a `UC_CharacterViewer`-style XAML preview.
3. NPC2 calls `vm.InitializeGl(ModuleResourceLocator.ShaderDirectory)` from its first GL render callback (same pattern SynthEBD uses; see `UC_CharacterViewer.xaml.cs`).
4. NPC2 calls `vm.LoadByIdentityAsync(identity, paths)` for the user's chosen "preview NPC" (e.g. Lydia or whoever they picked in their existing settings).
5. The VM's auto-framing kicks in (CameraFraming.MeshAware, configured per § 7's `BuildPortraitCreatorAlgorithm`). Yellow crop rect overlays the preview at the computed framing.
6. User adjusts background color / lighting → adapter fires INPC, viewer's `WhenAnyValue` re-pushes to renderer; settings persist.
7. User in Manual mode drags the preview → `vm.Camera.Distance/Azimuth/Elevation` change → bidirectional binding writes into `InternalMugshotSettings.Manual*`.
8. User clicks Reset → `InternalMugshotSettings` re-instantiated at defaults → `vm.LoadByIdentityAsync` re-runs → auto-framing re-applies.
9. User clicks "Generate mugshots" (existing batch button). NPC2 walks its NPC list and calls `_offscreenRenderer.RenderToPngAsync(BuildRequest(paths, _settings.InternalMugshot))` per NPC, writing each PNG to the existing output path scheme. The `CharacterPreviewCache` singleton amortizes NIF parses + DDS decodes across NPCs.

---

## 9. Known gaps / follow-ups

These are deliberately deferred. NPC2 can ship without any of them; if a particular gap blocks the integration, ping back.

| Gap | Workaround | Fix location |
|---|---|---|
| **Camera Roll and per-render FOV overrides** in `CameraFraming.Fixed` are ignored. `OrbitCamera` doesn't expose them. Only matters for hosts that want to tilt the camera or override the 45° default FOV; NPC2's manual mode uses `CameraFraming.OrbitState` which doesn't carry roll/FOV anyway. | If a user wants to roll the camera, they currently can't. | Extend `OrbitCamera` with a `Roll` property + a `FovOverride: float?` that takes precedence over the default 45° when set. |
| **Live preview ↔ offscreen lighting may differ subtly** if the preview's reactive lighting subscriptions race a render. | Force a render after applying lighting in the preview before letting the user inspect the result. | Could be tightened via a `vm.WaitForLightingApplied()` helper. Probably not necessary. |
| **GLFW main-thread requirement** prevents xUnit tests against `IOffscreenRenderer`. NPC2's CI can't smoke-test the renderer through unit tests. | Manual integration test in NPC2's running app. | Use a STA test runner or a manual debug-menu test harness. |

---

## 10. Two pre-flight checks before you start

1. **Mutagen version**: NPC2 uses `Mutagen.Bethesda.* 0.53.0-alpha.42`. `CharacterViewer.Rendering` has **no Mutagen dependency**, so this isn't an issue for the project reference. The only Mutagen surface NPC2 sees is its own — same as today.

2. **WPF target framework**: `CharacterViewer.Rendering.csproj` uses `<TargetFramework>net8.0</TargetFramework><TargetPlatformIdentifier>Windows</TargetPlatformIdentifier><UseWPF>true</UseWPF>`. NPC2's `<TargetFramework>net8.0-windows</TargetFramework>` is the equivalent shorthand; project reference resolves cleanly. (This was tested when SynthEBD bumped to its current configuration.)

---

## 11. Files to read before integrating

In SynthEBD, these are the canonical references:

| File | Why |
|---|---|
| [`SynthEBD\CharacterViewerHost\Adapters\*.cs`](S:\Dev\SynthEBD\SynthEBD\CharacterViewerHost\Adapters) | Reference adapter implementations — copy the patterns. |
| [`SynthEBD\CharacterViewerHost\Adapters\WpfDispatcherMarshaller.cs`](S:\Dev\SynthEBD\SynthEBD\CharacterViewerHost\Adapters\WpfDispatcherMarshaller.cs) | Identical to what NPC2 will write. |
| [`SynthEBD\Classes_Aux\Views\UC_CharacterViewer.xaml(.cs)`](S:\Dev\SynthEBD\SynthEBD\Classes_Aux\Views) | The preview UserControl pattern (GL init, mouse handling, camera binding). NPC2 should write a slimmer version. |
| [`SynthEBD\MainModule.cs`](S:\Dev\SynthEBD\SynthEBD\MainModule.cs) | Autofac registration order + the `RegisterBuildCallback` lazy-resolve trick for `SynthEbdViewerHostStateRegistry` (NPC2 only needs the registrations, not the host-state registry — that's SynthEBD-specific for ApplyBodySlide etc.). |
| [`CharacterViewer.Rendering\Offscreen\GameWindowOffscreenRenderer.cs`](S:\Dev\SynthEBD\CharacterViewer.Rendering\Offscreen\GameWindowOffscreenRenderer.cs) | The offscreen pipeline. Read once to understand the flow, then trust it. |
| [`CharacterViewer.Rendering\Offscreen\CameraFraming.cs`](S:\Dev\SynthEBD\CharacterViewer.Rendering\Offscreen\CameraFraming.cs) | The `MeshAware` framing API + `FramingShapeSelector` patterns. |

---

That's the integration. Total NPC2-side work is roughly: 6 host adapters (copy/adapt SynthEBD's), DI block, settings schema, Renderer combobox, preview UC + yellow rect overlay, `BuildRequest` wiring. Estimate: a focused day or two depending on how polished you want the preview UI.
