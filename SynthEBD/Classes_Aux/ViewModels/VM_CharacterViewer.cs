using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using MediaColor = System.Windows.Media.Color;

namespace SynthEBD;

/// <summary>
/// Controls which editing features are available in the character viewer.
/// </summary>
[Flags]
public enum ViewerMode
{
    ReadOnly = 0,
    TextureEdit = 1,
    BodySlideEdit = 2,
    Full = TextureEdit | BodySlideEdit
}

/// <summary>
/// ViewModel for the 3D character viewer. Manages the OpenGL rendering scene,
/// loaded mesh data, textures, and user interaction.
/// </summary>
public class VM_CharacterViewer : VM
{
    private readonly NifMeshBuilder _meshBuilder;
    private readonly NpcMeshResolver _npcMeshResolver;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BsdFileParser _bsdFileParser;
    private readonly BodyTriFileParser _bodyTriFileParser;
    private readonly GameAssetResolver _assetResolver;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;

    private CancellationTokenSource? _loadCts;

    // GL rendering objects — created once, reused across NPC loads
    public GlRenderer Renderer { get; } = new();
    public GlTextureManager? TextureManager { get; private set; }
    public OrbitCamera Camera { get; } = new();

    /// <summary>Tracks which GlMesh corresponds to which body part for texture overrides.</summary>
    private readonly Dictionary<string, GlMesh> _meshesByBodyPart = new();

    /// <summary>Tracks BuiltMesh per body part for BodySlide and normal override resampling.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _builtMeshesByBodyPart = new();

    /// <summary>Cached built meshes for reapplying BodySlide without reloading.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _cachedBodyMeshes = new();

    private List<OsdFile>? _cachedOsdFiles;

    /// <summary>Disk path of the currently-loaded body NIF, used to locate the
    /// sibling .tri for topology-matched morphing (BodySlide "Build Morphs" output).</summary>
    private string? _cachedBodyNifDiskPath;

    /// <summary>Parsed sibling .tri for the current body NIF, cached across preset/weight
    /// changes so we don't re-parse on every RefreshPreview. Null when no .tri is
    /// present next to the NIF -- in that case we fall back to the OSD path.</summary>
    private BodyTriFile? _cachedBodyTri;

    private NpcMeshResolver.NpcMeshPaths? _cachedMeshPaths;

    /// <summary>NPC's HairColor record (HCLR) resolved from HeadData.HairColor FormLink,
    /// in 0..1 linear floats. Null if the NPC has no HairColor set or it fails to resolve.
    /// In-game Skyrim uses this to override the NIF's baked BSLSP hairTintColor.</summary>
    private (float R, float G, float B)? _npcHairColorFromRecord;

    /// <summary>Cached texture info per mesh for ReapplyAllTextures.</summary>
    private readonly Dictionary<GlMesh, TextureApplyInfo> _textureApplyInfoByMesh = new();

    private record TextureApplyInfo(
        Dictionary<int, string> EffectiveTextures,
        bool IsHairTint, float HairTintR, float HairTintG, float HairTintB,
        bool IsFaceTint, string? FaceTintPath);

    private readonly VM_Settings_General _generalSettings;
    private readonly FaceGenPreviewService _faceGenPreviewService;

    /// <summary>True when the GL context has been initialized.</summary>
    public bool IsGlInitialized { get; private set; }

    /// <summary>Pending scene data waiting for GL context to become available.</summary>
    private (List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadResults,
             NpcMeshResolver.NpcMeshPaths MeshPaths)? _pendingScene;

    /// <summary>True from the moment a new NPC load starts until the render callback
    /// has rebuilt the scene. Routes texture overrides to the pending queue so they
    /// aren't applied to meshes that are about to be destroyed by ClearScene().</summary>
    private bool _sceneRebuildPending;

    /// <summary>Pending texture overrides to apply after scene setup.</summary>
    private List<FilePathReplacement>? _pendingTextureOverrides;

    /// <summary>Pending BodySlide to apply after scene setup.</summary>
    private (BodySlideSetting Preset, int Weight)? _pendingBodySlide;

    public VM_CharacterViewer(
        NpcMeshResolver npcMeshResolver,
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdFileParser,
        BodyTriFileParser bodyTriFileParser,
        GameAssetResolver assetResolver,
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        VM_Settings_General generalSettings,
        FaceGenPreviewService faceGenPreviewService,
        Logger logger)
    {
        _meshBuilder = new NifMeshBuilder(logger);
        _npcMeshResolver = npcMeshResolver;
        _bodySlideDeformer = bodySlideDeformer;
        _bsdFileParser = bsdFileParser;
        _bodyTriFileParser = bodyTriFileParser;
        _assetResolver = assetResolver;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _generalSettings = generalSettings;
        _faceGenPreviewService = faceGenPreviewService;
        _logger = logger;

        // Load persisted lighting state *before* XAML binds. If we defer this to
        // InitializeGl (which runs from the first GL render callback), the
        // ComboBox's two-way binding fires first and overwrites the persisted
        // selection with the field's default — that bug caused selections to
        // appear not to persist across sessions.
        InitializeLightingState();

        // Push height changes through to the renderer's model matrix. Either
        // source (NPC record default or per-assignment override) triggers a
        // recompute, and the owning VM only needs to set HeightOverride.
        this.WhenAnyValue(x => x.NpcBaseHeight, x => x.HeightOverride)
            .Subscribe(_ => ApplyCharacterScale())
            .DisposeWith(this);
    }

    /// <summary>Pushes the effective NPC-height scale to the renderer. Override
    /// wins when set; otherwise the NPC record's Height is used.</summary>
    private void ApplyCharacterScale()
    {
        float scale = HeightOverride ?? NpcBaseHeight;
        if (!float.IsFinite(scale) || scale <= 0f) scale = 1.0f;
        Renderer.ModelScale = scale;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VIEWER STATE
    // ═══════════════════════════════════════════════════════════════════════

    public ViewerMode Mode { get; set; } = ViewerMode.ReadOnly;
    public string StatusText { get; set; } = "No mesh loaded";
    public bool IsLoading { get; set; }
    public int NpcWeight { get; set; } = 50;

    /// <summary>The NPC record's Height field, a uniform full-model scale
    /// multiplier (1.0 = default). Refreshed whenever a new NPC is loaded;
    /// acts as the fallback when no per-assignment override is set.</summary>
    public float NpcBaseHeight { get; set; } = 1.0f;

    /// <summary>Per-assignment Height override (Consistency / Specific NPC
    /// Assignment Height field). When non-null, replaces <see cref="NpcBaseHeight"/>
    /// as the applied scale. Null means "fall back to the NPC record".</summary>
    public float? HeightOverride { get; set; }

    /// <summary>Viewport background color, bound to XAML.</summary>
    public MediaColor BackgroundColor { get; set; } = MediaColor.FromRgb(105, 105, 105);

    // ═══════════════════════════════════════════════════════════════════════
    //  LIGHTING CONTROLS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Suppresses renderer push-backs while a preset or constructor is applying values,
    /// so each property setter doesn't independently stomp on sibling lights.</summary>
    private bool _applyingPreset;

    // Intensity is expressed in percent (100 = 1.0 multiplier). Upper bound is
    // deliberately loose — the shader does not clamp, so values above 100 are
    // useful for heavily-attenuating materials.
    private const double IntensityMax = 500;

    public double AmbientIntensity { get; set; } = CharacterViewerLightingPresets.DefaultLayout.Ambient;

    public double KeyLightIntensity   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.KeyIntensity;
    public double KeyLightAzimuth     { get; set; } = CharacterViewerLightingPresets.DefaultLayout.KeyAzimuth;
    public double KeyLightElevation   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.KeyElevation;
    public MediaColor KeyLightColor   { get; set; } = MediaColor.FromRgb(255, 245, 224); // warm key
    public bool KeyLightEnabled       { get; set; } = true;

    public double FillLightIntensity  { get; set; } = CharacterViewerLightingPresets.DefaultLayout.FillIntensity;
    public double FillLightAzimuth    { get; set; } = CharacterViewerLightingPresets.DefaultLayout.FillAzimuth;
    public double FillLightElevation  { get; set; } = CharacterViewerLightingPresets.DefaultLayout.FillElevation;
    public MediaColor FillLightColor  { get; set; } = MediaColor.FromRgb(230, 237, 255);
    public bool FillLightEnabled      { get; set; } = true;

    public double RimLightIntensity   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.RimIntensity;
    public double RimLightAzimuth     { get; set; } = CharacterViewerLightingPresets.DefaultLayout.RimAzimuth;
    public double RimLightElevation   { get; set; } = CharacterViewerLightingPresets.DefaultLayout.RimElevation;
    public MediaColor RimLightColor   { get; set; } = MediaColor.FromRgb(255, 243, 217);
    public bool RimLightEnabled       { get; set; } = true;

    /// <summary>Master toggle for the per-light control UI and the 3D arrow gizmos.
    /// When false, the lighting controls are hidden and arrows are not drawn.</summary>
    public bool ShowLightControls { get; set; } = false;

    /// <summary>0 = none, 1 = key, 2 = fill, 3 = rim. Set when the user clicks
    /// an arrow in the 3D view (or from the UI). Controls which light the
    /// per-light editor panel edits.</summary>
    public int SelectedLightIndex { get; set; } = 0;

    public IReadOnlyList<CharacterViewerLightingLayout> LightingLayouts { get; private set; } =
        CharacterViewerLightingPresets.BuiltInLayouts;

    public IReadOnlyList<CharacterViewerLightingColorScheme> LightingColorSchemes { get; private set; } =
        CharacterViewerLightingPresets.BuiltInColorSchemes;

    public CharacterViewerLightingLayout SelectedLightingLayout { get; set; } =
        CharacterViewerLightingPresets.DefaultLayout;

    public CharacterViewerLightingColorScheme SelectedLightingColorScheme { get; set; } =
        CharacterViewerLightingPresets.DefaultColorScheme;

    public RelayCommand SaveLayoutPresetCommand { get; private set; } = null!;
    public RelayCommand SaveColorSchemePresetCommand { get; private set; } = null!;
    public RelayCommand DeleteSelectedLayoutCommand { get; private set; } = null!;
    public RelayCommand DeleteSelectedColorSchemeCommand { get; private set; } = null!;

    private void InitializeLightingState()
    {
        // Rebuild the combined (built-in + user) preset lists.
        var layouts = new List<CharacterViewerLightingLayout>(CharacterViewerLightingPresets.BuiltInLayouts);
        layouts.AddRange(_generalSettings.UserLightingLayouts);
        LightingLayouts = layouts;

        var schemes = new List<CharacterViewerLightingColorScheme>(CharacterViewerLightingPresets.BuiltInColorSchemes);
        schemes.AddRange(_generalSettings.UserLightingColorSchemes);
        LightingColorSchemes = schemes;

        // Resolve persisted selections (empty / unknown names fall back to defaults).
        SelectedLightingLayout = CharacterViewerLightingPresets.FindLayoutOrDefault(
            _generalSettings.CharacterViewerLightingLayout, _generalSettings.UserLightingLayouts);
        SelectedLightingColorScheme = CharacterViewerLightingPresets.FindColorSchemeOrDefault(
            _generalSettings.CharacterViewerLightingColorScheme, _generalSettings.UserLightingColorSchemes);

        // Seed per-light fields from the resolved layout/scheme before any UI binding
        // runs, so the editor panel opens with values that match the picked preset.
        ApplyPresetToFields(SelectedLightingLayout, SelectedLightingColorScheme);

        // Watch selection changes and push them to the renderer + settings.
        this.WhenAnyValue(x => x.SelectedLightingLayout).Skip(1).Subscribe(layout =>
        {
            if (layout == null) return;
            _generalSettings.CharacterViewerLightingLayout = layout.Name;
            ApplyPresetToFields(layout, SelectedLightingColorScheme);
            PushAllLightsToRenderer();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.SelectedLightingColorScheme).Skip(1).Subscribe(scheme =>
        {
            if (scheme == null) return;
            _generalSettings.CharacterViewerLightingColorScheme = scheme.Name;
            KeyLightColor = MediaFromVec3(scheme.KeyColor);
            FillLightColor = MediaFromVec3(scheme.FillColor);
            RimLightColor = MediaFromVec3(scheme.RimColor);
            PushAllLightsToRenderer();
        }).DisposeWith(this);

        // Any per-light edit (or enable toggle) re-pushes to the renderer.
        // Skip(1) suppresses the initial value emission so we don't push during
        // construction when the renderer isn't yet initialized. Split per-light
        // because ReactiveUI's WhenAnyValue overloads cap out at a modest arity.
        this.WhenAnyValue(x => x.AmbientIntensity)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(
            x => x.KeyLightIntensity, x => x.KeyLightAzimuth, x => x.KeyLightElevation,
            x => x.KeyLightColor, x => x.KeyLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(
            x => x.FillLightIntensity, x => x.FillLightAzimuth, x => x.FillLightElevation,
            x => x.FillLightColor, x => x.FillLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(
            x => x.RimLightIntensity, x => x.RimLightAzimuth, x => x.RimLightElevation,
            x => x.RimLightColor, x => x.RimLightEnabled)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(x => x.SelectedLightIndex)
            .Skip(1).Subscribe(_ => PushAllLightsToRenderer()).DisposeWith(this);

        this.WhenAnyValue(x => x.ShowLightControls).Subscribe(v =>
        {
            Renderer.ShowKeyLightVisualization = v;
            if (!v) SelectedLightIndex = 0;
        }).DisposeWith(this);

        // Commands
        SaveLayoutPresetCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => SaveCurrentAsLayoutPreset());
        SaveColorSchemePresetCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => SaveCurrentAsColorScheme());
        DeleteSelectedLayoutCommand = new RelayCommand(
            canExecute: _ => SelectedLightingLayout != null && !SelectedLightingLayout.IsBuiltIn,
            execute: _ => DeleteSelectedLayout());
        DeleteSelectedColorSchemeCommand = new RelayCommand(
            canExecute: _ => SelectedLightingColorScheme != null && !SelectedLightingColorScheme.IsBuiltIn,
            execute: _ => DeleteSelectedColorScheme());
    }

    /// <summary>Normalizes the per-light fields from a layout+scheme, suppressing
    /// intermediate renderer pushes so the combined state is sent once at the end.</summary>
    private void ApplyPresetToFields(CharacterViewerLightingLayout layout,
        CharacterViewerLightingColorScheme colors)
    {
        _applyingPreset = true;
        try
        {
            AmbientIntensity   = layout.Ambient;
            KeyLightIntensity  = layout.KeyIntensity;
            KeyLightAzimuth    = layout.KeyAzimuth;
            KeyLightElevation  = layout.KeyElevation;
            KeyLightColor      = MediaFromVec3(colors.KeyColor);

            FillLightIntensity = layout.FillIntensity;
            FillLightAzimuth   = layout.FillAzimuth;
            FillLightElevation = layout.FillElevation;
            FillLightColor     = MediaFromVec3(colors.FillColor);

            RimLightIntensity  = layout.RimIntensity;
            RimLightAzimuth    = layout.RimAzimuth;
            RimLightElevation  = layout.RimElevation;
            RimLightColor      = MediaFromVec3(colors.RimColor);
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    private void PushAllLightsToRenderer()
    {
        if (_applyingPreset) return;

        // Clamp & push. Intensity is divided by 100 since the shader expects a multiplier.
        var amb = Math.Clamp(AmbientIntensity, 0, IntensityMax) / 100.0;
        var keyI = Math.Clamp(KeyLightIntensity, 0, IntensityMax) / 100.0;
        var fillI = Math.Clamp(FillLightIntensity, 0, IntensityMax) / 100.0;
        var rimI = Math.Clamp(RimLightIntensity, 0, IntensityMax) / 100.0;

        Renderer.SetAmbientIntensity((float)amb);
        Renderer.SetKeyLight ((float)KeyLightAzimuth,  (float)KeyLightElevation,  (float)keyI,  Vec3FromMedia(KeyLightColor));
        Renderer.SetFillLight((float)FillLightAzimuth, (float)FillLightElevation, (float)fillI, Vec3FromMedia(FillLightColor));
        Renderer.SetRimLight ((float)RimLightAzimuth,  (float)RimLightElevation,  (float)rimI,  Vec3FromMedia(RimLightColor));

        Renderer.Lights[1].Type = KeyLightEnabled  ? 2 : 0;
        Renderer.Lights[2].Type = FillLightEnabled ? 2 : 0;
        Renderer.Lights[3].Type = RimLightEnabled  ? 2 : 0;

        Renderer.SelectedLightIndex = SelectedLightIndex;
    }

    private void SaveCurrentAsLayoutPreset()
    {
        string? name = PromptForName("Save Lighting Layout",
            "Enter a name for this lighting layout preset:",
            SelectedLightingLayout?.Name ?? "My Layout");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Prevent clobbering a built-in name, which would be unreachable after save
        // (FindByName returns the built-in first).
        foreach (var b in CharacterViewerLightingPresets.BuiltInLayouts)
        {
            if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                MessageWindow.DisplayNotificationOK("Name in use",
                    $"'{name}' is a built-in preset name. Please choose a different name.");
                return;
            }
        }

        var existing = _generalSettings.UserLightingLayouts.FirstOrDefault(l =>
            string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null &&
            !MessageWindow.DisplayNotificationYesNo("Overwrite preset?",
                $"A user preset named '{name}' already exists. Overwrite it?"))
        {
            return;
        }
        if (existing != null) _generalSettings.UserLightingLayouts.Remove(existing);

        var preset = new CharacterViewerLightingLayout
        {
            Name = name, IsBuiltIn = false,
            Ambient = AmbientIntensity,
            KeyAzimuth = KeyLightAzimuth, KeyElevation = KeyLightElevation, KeyIntensity = KeyLightIntensity,
            FillAzimuth = FillLightAzimuth, FillElevation = FillLightElevation, FillIntensity = FillLightIntensity,
            RimAzimuth = RimLightAzimuth, RimElevation = RimLightElevation, RimIntensity = RimLightIntensity,
        };
        _generalSettings.UserLightingLayouts.Add(preset);
        RebuildLayoutList();
        SelectedLightingLayout = preset;
    }

    private void SaveCurrentAsColorScheme()
    {
        string? name = PromptForName("Save Color Scheme",
            "Enter a name for this color scheme preset:",
            SelectedLightingColorScheme?.Name ?? "My Colors");
        if (string.IsNullOrWhiteSpace(name)) return;

        foreach (var b in CharacterViewerLightingPresets.BuiltInColorSchemes)
        {
            if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                MessageWindow.DisplayNotificationOK("Name in use",
                    $"'{name}' is a built-in preset name. Please choose a different name.");
                return;
            }
        }

        var existing = _generalSettings.UserLightingColorSchemes.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null &&
            !MessageWindow.DisplayNotificationYesNo("Overwrite preset?",
                $"A user color scheme named '{name}' already exists. Overwrite it?"))
        {
            return;
        }
        if (existing != null) _generalSettings.UserLightingColorSchemes.Remove(existing);

        var scheme = new CharacterViewerLightingColorScheme
        {
            Name = name, IsBuiltIn = false,
            KeyColor = Vec3FromMedia(KeyLightColor),
            FillColor = Vec3FromMedia(FillLightColor),
            RimColor = Vec3FromMedia(RimLightColor),
        };
        _generalSettings.UserLightingColorSchemes.Add(scheme);
        RebuildColorSchemeList();
        SelectedLightingColorScheme = scheme;
    }

    private void DeleteSelectedLayout()
    {
        var sel = SelectedLightingLayout;
        if (sel == null || sel.IsBuiltIn) return;
        if (!MessageWindow.DisplayNotificationYesNo("Delete preset?",
                $"Delete the user lighting layout '{sel.Name}'?")) return;

        _generalSettings.UserLightingLayouts.Remove(sel);
        RebuildLayoutList();
        SelectedLightingLayout = CharacterViewerLightingPresets.DefaultLayout;
    }

    private void DeleteSelectedColorScheme()
    {
        var sel = SelectedLightingColorScheme;
        if (sel == null || sel.IsBuiltIn) return;
        if (!MessageWindow.DisplayNotificationYesNo("Delete preset?",
                $"Delete the user color scheme '{sel.Name}'?")) return;

        _generalSettings.UserLightingColorSchemes.Remove(sel);
        RebuildColorSchemeList();
        SelectedLightingColorScheme = CharacterViewerLightingPresets.DefaultColorScheme;
    }

    private void RebuildLayoutList()
    {
        var layouts = new List<CharacterViewerLightingLayout>(CharacterViewerLightingPresets.BuiltInLayouts);
        layouts.AddRange(_generalSettings.UserLightingLayouts);
        LightingLayouts = layouts;
    }

    private void RebuildColorSchemeList()
    {
        var schemes = new List<CharacterViewerLightingColorScheme>(CharacterViewerLightingPresets.BuiltInColorSchemes);
        schemes.AddRange(_generalSettings.UserLightingColorSchemes);
        LightingColorSchemes = schemes;
    }

    private static string? PromptForName(string title, string message, string defaultValue)
    {
        string? result = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                Title = title,
                Width = 380,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.DimGray,
            };

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            };
            System.Windows.Controls.Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 13,
            };
            textBox.SelectAll();
            textBox.Focus();
            System.Windows.Controls.Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK", Width = 70, Margin = new Thickness(0, 0, 6, 0), IsDefault = true,
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel", Width = 70, IsCancel = true,
            };
            okButton.Click += (_, _) => { result = textBox.Text; window.Close(); };
            cancelButton.Click += (_, _) => { result = null; window.Close(); };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            window.Content = grid;
            window.Loaded += (_, _) => textBox.Focus();
            window.ShowDialog();
        });
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static MediaColor MediaFromVec3(OpenTK.Mathematics.Vector3 v) =>
        MediaColor.FromRgb(
            (byte)Math.Clamp((int)Math.Round(v.X * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(v.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(v.Z * 255f), 0, 255));

    private static OpenTK.Mathematics.Vector3 Vec3FromMedia(MediaColor c) =>
        new(c.R / 255f, c.G / 255f, c.B / 255f);

    public void LogLightingSettings()
    {
        _logger.LogMessage($"CharacterViewer: LIGHTING — Layout='{SelectedLightingLayout?.Name}', " +
            $"Colors='{SelectedLightingColorScheme?.Name}', Ambient={AmbientIntensity:F0}%, " +
            $"Key={KeyLightIntensity:F0}%@({KeyLightAzimuth:F0}°,{KeyLightElevation:F0}°), " +
            $"Fill={FillLightIntensity:F0}%@({FillLightAzimuth:F0}°,{FillLightElevation:F0}°), " +
            $"Rim={RimLightIntensity:F0}%@({RimLightAzimuth:F0}°,{RimLightElevation:F0}°)");
    }

    /// <summary>
    /// Tests whether a screen-space click lies on one of the light-direction
    /// arrow gizmos. Returns the light index (1=key, 2=fill, 3=rim) or 0 for miss.
    /// Uses ray vs. capsule distance along the arrow's shaft — sufficient for
    /// gizmo picking since the arrows are drawn thick enough.
    /// </summary>
    public int HitTestLightArrow(float mouseX, float mouseY, float viewportWidth, float viewportHeight)
    {
        if (!ShowLightControls) return 0;

        var (origin, rayDir) = Camera.ScreenPointToRay(mouseX, mouseY, viewportWidth, viewportHeight);

        int best = 0;
        float bestDepth = float.MaxValue;
        for (int slot = 1; slot <= 3; slot++)
        {
            if (!Renderer.TryGetArrowSegment(slot, out var tail, out var tip, out var radius)) continue;
            if (RayCapsuleHit(origin, rayDir, tail, tip, radius, out float tAlongRay))
            {
                if (tAlongRay < bestDepth)
                {
                    bestDepth = tAlongRay;
                    best = slot;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Closest-distance ray vs. capsule test. Returns true if the perpendicular
    /// distance from the ray to the segment (tail, tip) is ≤ radius, and
    /// outputs the parametric ray depth at the closest point.
    /// </summary>
    private static bool RayCapsuleHit(
        OpenTK.Mathematics.Vector3 rayOrigin, OpenTK.Mathematics.Vector3 rayDir,
        OpenTK.Mathematics.Vector3 tail, OpenTK.Mathematics.Vector3 tip,
        float radius, out float tAlongRay)
    {
        tAlongRay = 0f;
        var d1 = rayDir; // assume ~unit length
        var d2 = tip - tail;
        float len2 = d2.LengthSquared;
        if (len2 < 1e-6f) return false;

        var r = rayOrigin - tail;
        float a = OpenTK.Mathematics.Vector3.Dot(d1, d1);
        float e = OpenTK.Mathematics.Vector3.Dot(d2, d2);
        float f = OpenTK.Mathematics.Vector3.Dot(d2, r);
        float c = OpenTK.Mathematics.Vector3.Dot(d1, r);
        float b = OpenTK.Mathematics.Vector3.Dot(d1, d2);
        float denom = a * e - b * b;

        float s, t;
        if (denom != 0f) s = Math.Clamp((b * f - c * e) / denom, 0f, float.MaxValue);
        else s = 0f;
        t = (b * s + f) / e;

        if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, float.MaxValue); }
        else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, float.MaxValue); }

        var closestOnRay = rayOrigin + d1 * s;
        var closestOnSeg = tail + d2 * t;
        float distSq = (closestOnRay - closestOnSeg).LengthSquared;

        if (distSq > radius * radius) return false;
        tAlongRay = s;
        return s >= 0f;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HIT TESTING
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs a ray cast from screen coordinates and returns the closest hit mesh, or null.
    /// Uses Möller-Trumbore ray-triangle intersection against CPU-side geometry.
    /// </summary>
    public GlMesh? HitTest(float mouseX, float mouseY, float viewportWidth, float viewportHeight)
    {
        var (origin, direction) = Camera.ScreenPointToRay(mouseX, mouseY, viewportWidth, viewportHeight);

        GlMesh? closestMesh = null;
        float closestDist = float.MaxValue;

        foreach (var mesh in Renderer.Meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.CpuPositions == null || mesh.CpuIndices == null) continue;

            if (RayIntersectsMesh(origin, direction, mesh, out float dist) && dist < closestDist)
            {
                closestDist = dist;
                closestMesh = mesh;
            }
        }

        return closestMesh;
    }

    /// <summary>
    /// Möller-Trumbore ray-triangle intersection test against a mesh's CPU-side geometry.
    /// </summary>
    private static bool RayIntersectsMesh(
        OpenTK.Mathematics.Vector3 rayOrigin,
        OpenTK.Mathematics.Vector3 rayDir,
        GlMesh mesh,
        out float hitDistance)
    {
        hitDistance = float.MaxValue;
        bool anyHit = false;

        var positions = mesh.CpuPositions!;
        var indices = mesh.CpuIndices!;
        const float epsilon = 1e-6f;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var v0Sys = positions[indices[i]];
            var v1Sys = positions[indices[i + 1]];
            var v2Sys = positions[indices[i + 2]];

            // Convert System.Numerics.Vector3 to OpenTK.Mathematics.Vector3
            var v0 = new OpenTK.Mathematics.Vector3(v0Sys.X, v0Sys.Y, v0Sys.Z);
            var v1 = new OpenTK.Mathematics.Vector3(v1Sys.X, v1Sys.Y, v1Sys.Z);
            var v2 = new OpenTK.Mathematics.Vector3(v2Sys.X, v2Sys.Y, v2Sys.Z);

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = OpenTK.Mathematics.Vector3.Cross(rayDir, edge2);
            float a = OpenTK.Mathematics.Vector3.Dot(edge1, h);

            if (a > -epsilon && a < epsilon) continue; // parallel

            float f = 1f / a;
            var s = rayOrigin - v0;
            float u = f * OpenTK.Mathematics.Vector3.Dot(s, h);
            if (u < 0f || u > 1f) continue;

            var q = OpenTK.Mathematics.Vector3.Cross(s, edge1);
            float v = f * OpenTK.Mathematics.Vector3.Dot(rayDir, q);
            if (v < 0f || u + v > 1f) continue;

            float t = f * OpenTK.Mathematics.Vector3.Dot(edge2, q);
            if (t > epsilon && t < hitDistance)
            {
                hitDistance = t;
                anyHit = true;
            }
        }

        return anyHit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GL INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called once after the GL context is ready (from the view's OnRender or Loaded event).
    /// </summary>
    public void InitializeGl(string shaderDirectory)
    {
        if (IsGlInitialized) return;

        TextureManager = new GlTextureManager(_assetResolver, _logger);
        TextureManager.Initialize();
        Renderer.Initialize(shaderDirectory);

        // Selections were resolved from settings in the ctor; push the current
        // field values to the renderer now that it's initialized.
        PushAllLightsToRenderer();
        Renderer.ShowKeyLightVisualization = ShowLightControls;

        IsGlInitialized = true;

        _logger.LogMessage("CharacterViewer: GL initialized");
    }

    /// <summary>
    /// Called from the GL render callback to process any pending scene setup.
    /// All GL calls (mesh upload, texture loading) happen here where the
    /// GL context is guaranteed to be current.
    /// </summary>
    public void ProcessPendingScene()
    {
        if (_pendingScene == null || !IsGlInitialized) return;

        var (loadResults, meshPaths) = _pendingScene.Value;
        _pendingScene = null;

        ClearScene();
        _cachedMeshPaths = meshPaths;

        int totalShapes = 0;
        foreach (var (bodyPart, meshSource, meshes) in loadResults)
        {
            Dictionary<int, string>? txstOverrides = null;
            if (bodyPart != "Head" && meshPaths.TxstTextures.TryGetValue(bodyPart, out var txst))
                txstOverrides = txst;

            foreach (var built in meshes)
            {
                var glMesh = CreateGlMesh(built);
                glMesh.MeshSource = meshSource;

                var effectiveTextures = new Dictionary<int, string>(built.TexturePaths);
                if (txstOverrides != null)
                    foreach (var (slot, path) in txstOverrides)
                        effectiveTextures[slot] = path;

                bool isHairTint = false;
                float hairR = 0, hairG = 0, hairB = 0;
                bool isFaceTint = false;
                string? faceTintPath = null;

                ApplyTexturesToGlMesh(glMesh, built, effectiveTextures, meshPaths,
                    ref isHairTint, ref hairR, ref hairG, ref hairB,
                    ref isFaceTint, ref faceTintPath);

                _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
                    new Dictionary<int, string>(effectiveTextures),
                    isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

                glMesh.BodyPart = bodyPart;
                Renderer.AddMesh(glMesh);

                if (bodyPart == "Head")
                {
                    if (built.IsPrimaryHeadShape || !_meshesByBodyPart.ContainsKey(bodyPart))
                        _meshesByBodyPart[bodyPart] = glMesh;
                    if (built.IsPrimaryHeadShape || !_builtMeshesByBodyPart.ContainsKey(bodyPart))
                        _builtMeshesByBodyPart[bodyPart] = built;
                }
                else
                {
                    if (!_meshesByBodyPart.ContainsKey(bodyPart))
                        _meshesByBodyPart[bodyPart] = glMesh;
                    if (!_builtMeshesByBodyPart.ContainsKey(bodyPart))
                        _builtMeshesByBodyPart[bodyPart] = built;
                }

                if (bodyPart == "Body")
                {
                    _cachedBodyMeshes[built.ShapeName] = built;

                    // Cache the body NIF's disk path once per scene so ApplyBodySlide
                    // can probe for a sibling .tri (BodySlide's "Build Morphs" output).
                    // The .tri is topology-matched to this NIF, so it avoids the OSD
                    // path's reference-mesh mismatch.
                    if (_cachedBodyNifDiskPath == null && meshSource?.ResolvedDiskPath != null)
                    {
                        _cachedBodyNifDiskPath = meshSource.ResolvedDiskPath;
                    }
                }

                totalShapes++;
            }
        }

        StatusText = totalShapes > 0
            ? $"Loaded {totalShapes} shape(s) for NPC"
            : "No renderable shapes found for NPC";
        IsLoading = false;

        _logger.LogMessage($"CharacterViewer: Scene setup complete — {totalShapes} shapes, " +
            $"{Renderer.Meshes.Count} GL meshes");

        // Scene is now rebuilt — clear the rebuild flag before draining the
        // pending-override queue so ApplyTextureOverrides takes the direct path.
        _sceneRebuildPending = false;

        // Process any pending overrides that were queued before the scene was ready
        if (_pendingTextureOverrides != null)
        {
            var overrides = _pendingTextureOverrides;
            _pendingTextureOverrides = null;
            ApplyTextureOverrides(overrides);
        }

        if (_pendingBodySlide != null)
        {
            var (preset, weight) = _pendingBodySlide.Value;
            _pendingBodySlide = null;
            ApplyBodySlide(preset, weight);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING — Full NPC
    // ═══════════════════════════════════════════════════════════════════════

    public async Task LoadNpcAsync(FormKey npcFormKey, ILinkCache linkCache, string? overrideHeadMeshAbsolutePath = null)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        // Mark a rebuild as in-flight so any ApplyTextureOverrides calls arriving
        // between now and when ProcessPendingScene finishes are queued rather than
        // applied to the soon-to-be-destroyed current meshes.
        _sceneRebuildPending = true;

        IsLoading = true;
        StatusText = "Resolving NPC meshes...";

        try
        {
            _npcHairColorFromRecord = null;
            if (linkCache.TryResolve<Mutagen.Bethesda.Skyrim.INpcGetter>(npcFormKey, out var npcGetter))
            {
                NpcWeight = Math.Clamp((int)npcGetter.Weight, 0, 100);
                _logger.LogMessage("CharacterViewer: NPC weight = " + NpcWeight +
                    " (raw " + npcGetter.Weight.ToString("F2") + ")");

                // NPC.Height is a full-model uniform scale multiplier (1.0 default).
                // Guard against zero/negative values from malformed records to avoid
                // a collapsed or mirrored render.
                float recordHeight = npcGetter.Height;
                NpcBaseHeight = (float.IsFinite(recordHeight) && recordHeight > 0f) ? recordHeight : 1.0f;
                _logger.LogMessage("CharacterViewer: NPC height = " + NpcBaseHeight.ToString("F3") +
                    " (raw " + recordHeight.ToString("F3") + ")");

                // Resolve the NPC's HairColor FormLink (HCLR record) — in-game, this
                // overrides the default hairTintColor baked into the hair NIF's BSLSP.
                // Logging both lets us diagnose mismatches between reference images
                // (which show the NPC's HCLR color) and the viewer (which currently
                // uses only the NIF's baked tint).
                if (npcGetter.HairColor.IsNull)
                {
                    _logger.LogMessage("CharacterViewer: NPC.HairColor FormLink is null — " +
                        "no HCLR override available; viewer will use NIF's baked BSLSP tint.");
                }
                else
                {
                    var hclr = npcGetter.HairColor.TryResolve(linkCache);
                    if (hclr != null)
                    {
                        var c = hclr.Color;
                        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
                        _npcHairColorFromRecord = (r, g, b);
                        string hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                        _logger.LogMessage("CharacterViewer: NPC.HairColor HCLR=" +
                            npcGetter.HairColor.FormKey.ToString() +
                            " name='" + (hclr.Name?.String ?? "?") + "'" +
                            " RGB=(" + c.R + "," + c.G + "," + c.B + ")" +
                            " float=(" + r.ToString("F3") + "," + g.ToString("F3") + "," + b.ToString("F3") + ")" +
                            " hex=" + hex);
                    }
                    else
                    {
                        _logger.LogMessage("CharacterViewer: NPC.HairColor FormLink " +
                            npcGetter.HairColor.FormKey.ToString() + " failed to resolve.");
                    }
                }
            }

            var meshPaths = await Task.Run(() => _npcMeshResolver.ResolveMeshPaths(npcFormKey, linkCache), cts.Token);
            if (meshPaths == null)
            {
                StatusText = "Could not resolve NPC mesh paths";
                // No scene will be queued for ProcessPendingScene to rebuild, so
                // clear the flag now — otherwise future ApplyTextureOverrides
                // calls would be queued forever.
                if (_loadCts == cts) _sceneRebuildPending = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(overrideHeadMeshAbsolutePath))
            {
                meshPaths = meshPaths.WithHeadMeshPath(overrideHeadMeshAbsolutePath);
                _logger.LogMessage("CharacterViewer: head mesh path overridden -> " + overrideHeadMeshAbsolutePath);
            }

            _cachedMeshPaths = meshPaths;
            cts.Token.ThrowIfCancellationRequested();

            StatusText = "Loading meshes...";
            var loadResults = await Task.Run(() => LoadAllMeshParts(meshPaths), cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            // Store pending scene data — GL work is deferred to the render callback
            // where the GL context is guaranteed to be current.
            int totalShapes = loadResults.Sum(r => r.Meshes.Count);
            Application.Current.Dispatcher.Invoke(() =>
            {
                _pendingScene = (loadResults, meshPaths);
                StatusText = totalShapes > 0
                    ? $"Loaded {totalShapes} shape(s), setting up scene..."
                    : "No renderable shapes found for NPC";
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogMessage("CharacterViewer: NPC load cancelled");
            // If a newer load took over, _loadCts != cts and that newer load owns
            // the flag. Only clear the flag if we're still the current (unreplaced)
            // load — meaning cancellation came from outside, not from a new load.
            if (_loadCts == cts) _sceneRebuildPending = false;
        }
        catch (Exception ex)
        {
            // Many exceptions (NullReferenceException, IndexOutOfRangeException, …) have an
            // unhelpful or empty Message. Log the full chained exception so the Status Log
            // actually reveals the failure instead of silently switching tabs.
            StatusText = $"Error: {ex.Message}";
            _logger.LogError("CharacterViewer: Failed to load NPC " + npcFormKey + Environment.NewLine
                + ExceptionLogger.GetExceptionStack(ex));
            if (_loadCts == cts) _sceneRebuildPending = false;
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE APPLICATION
    // ═══════════════════════════════════════════════════════════════════════

    private void ApplyTexturesToGlMesh(GlMesh glMesh, NifMeshBuilder.BuiltMesh built,
        Dictionary<int, string> effectiveTextures, NpcMeshResolver.NpcMeshPaths meshPaths,
        ref bool isHairTint, ref float hairR, ref float hairG, ref float hairB,
        ref bool isFaceTint, ref string? faceTintPath)
    {
        if (TextureManager == null) return;

        // Diffuse (slot 0) — with special handling for hair tint and face tint.
        // Two tinting modes match the Skyrim engine (and NPC Portrait Creator):
        //   1. SLSF1_Greyscale_To_Palette_Color flag set:
        //      Texture is greyscale; shader: baseColor.rrr * tint_color * greyscaleToPaletteScale
        //   2. BSLSP_HAIRTINT shader type only (flag NOT set):
        //      Texture is full RGB; shader: baseColor.rgb *= tint_color (simple multiply)
        if (built.IsHairTintShader && built.HairTintColor.HasValue &&
            effectiveTextures.TryGetValue(0, out string? hairDiffuse))
        {
            var (tR, tG, tB) = built.HairTintColor.Value;
            isHairTint = true; hairR = tR; hairG = tG; hairB = tB;
            glMesh.DiffuseTexture = TextureManager.LoadTexture(hairDiffuse);
            glMesh.TintColor = new System.Numerics.Vector3(tR, tG, tB);

            if (built.HasGreyscaleToPaletteFlag)
            {
                glMesh.HasGreyscaleToPalette = true;
                glMesh.GreyscaleToPaletteScale = built.GreyscaleToPaletteScale;
                RecordTextureSource(glMesh, "Diffuse (hair tint, greyscale-to-palette)", hairDiffuse);
                _logger.LogMessage("CharacterViewer: Hair tint (greyscale-to-palette): " +
                    "tint=(" + tR.ToString("F3") + "," + tG.ToString("F3") + "," + tB.ToString("F3") + ")" +
                    " scale=" + built.GreyscaleToPaletteScale.ToString("F2") +
                    " -> baseColor.rrr * tint * scale" +
                    " | diffuse=" + System.IO.Path.GetFileName(hairDiffuse));
            }
            else
            {
                glMesh.HasTintColor = true;
                RecordTextureSource(glMesh, "Diffuse (hair tint, RGB multiply)", hairDiffuse);
                _logger.LogMessage("CharacterViewer: Hair tint (simple RGB multiply): " +
                    "tint=(" + tR.ToString("F3") + "," + tG.ToString("F3") + "," + tB.ToString("F3") + ")" +
                    " -> baseColor.rgb *= tint" +
                    " | diffuse=" + System.IO.Path.GetFileName(hairDiffuse));
            }
        }
        else if (built.IsPrimaryHeadShape && effectiveTextures.TryGetValue(0, out string? headDiffuse) &&
                 meshPaths.FaceTintPath != null)
        {
            isFaceTint = true;
            faceTintPath = meshPaths.FaceTintPath;
            // Load diffuse and face tint as separate textures so they can be
            // toggled independently. The shader blends them via has_face_tint_map.
            glMesh.DiffuseTexture = TextureManager.LoadTexture(headDiffuse);
            glMesh.FaceTintTexture = TextureManager.LoadTexture(meshPaths.FaceTintPath);
            glMesh.HasFaceTintMap = true;
            RecordTextureSource(glMesh, "Diffuse", headDiffuse);
            RecordTextureSource(glMesh, "Face Tint", meshPaths.FaceTintPath);
        }
        else if (effectiveTextures.TryGetValue(0, out string? diffusePath))
        {
            glMesh.DiffuseTexture = TextureManager.LoadTexture(diffusePath);
            RecordTextureSource(glMesh, "Diffuse", diffusePath);
        }
        else
        {
            glMesh.DiffuseTexture = TextureManager.WhiteTexture;
        }

        // Normal map (slot 1)
        if (effectiveTextures.TryGetValue(1, out string? normalPath))
        {
            glMesh.NormalTexture = TextureManager.LoadTexture(normalPath);
            glMesh.HasNormalMap = true;
            glMesh.IsModelSpace = built.IsModelSpaceNormals;
            RecordTextureSource(glMesh, "Normal Map", normalPath);
        }
        else
        {
            glMesh.NormalTexture = TextureManager.WhiteTexture;
        }

        // Skin/subsurface map (slot 2) — only meaningful for skin/face shader types.
        // For other shader types (eye, hair, default, etc.) slot 2 has a different meaning
        // (glow, environment, etc.) and applying it as a skin map would add incorrect red SSS tinting.
        bool isSkinShader = built.ShaderType == 4  // BSLSP_FACE
                         || built.ShaderType == 5; // BSLSP_SKINTINT
        if (isSkinShader && effectiveTextures.TryGetValue(2, out string? skinPath))
        {
            glMesh.SkinTexture = TextureManager.LoadTexture(skinPath);
            glMesh.HasSkinMap = true;
            RecordTextureSource(glMesh, "Skin/SSS", skinPath);
        }
        else
        {
            glMesh.SkinTexture = TextureManager.WhiteTexture;
        }

        // Specular map (slot 7)
        if (effectiveTextures.TryGetValue(7, out string? specPath))
        {
            glMesh.SpecularTexture = TextureManager.LoadTexture(specPath);
            glMesh.HasSpecularMap = true;
            glMesh.HasSpecular = true;
            RecordTextureSource(glMesh, "Specular", specPath);
        }
        else
        {
            glMesh.SpecularTexture = TextureManager.WhiteTexture;
            // Check shader flags for specular enable even without a map
            glMesh.HasSpecular = (built.ShaderFlags1 & (1u << 0)) != 0;
        }

        if (!glMesh.HasFaceTintMap)
            glMesh.FaceTintTexture = TextureManager.WhiteTexture;

        // Material properties
        glMesh.MaterialGlossiness = built.Glossiness;
        glMesh.MaterialSpecularStrength = built.SpecularStrength;
        glMesh.SpecularColor = built.SpecularColor;
        glMesh.SubsurfaceRolloff = built.SubsurfaceRolloff;
        glMesh.RimlightPower = built.RimlightPower;
        glMesh.HasVertexColors = built.HasVertexColors;
        glMesh.UvScale = built.UvScale;
        glMesh.UvOffset = built.UvOffset;

        // Emissive (SLSF1_OwnEmit, bit 22)
        if ((built.ShaderFlags1 & (1u << 22)) != 0)
        {
            glMesh.HasEmissive = true;
            glMesh.EmissiveColor = built.EmissiveColor;
            glMesh.EmissiveMultiple = built.EmissiveMultiple;
        }

        // Shader type flags (bit positions per Nifskope/Bethesda spec)
        glMesh.HasHairSoftLighting = (built.ShaderFlags1 & (1u << 18)) != 0; // SLSF1_Hair_Soft_Lighting
        glMesh.HasSoftLighting = (built.ShaderFlags2 & (1u << 25)) != 0; // SLSF2_Soft_Lighting
        glMesh.HasRimLighting = (built.ShaderFlags2 & (1u << 26)) != 0; // SLSF2_Rim_Lighting

        // Skin tint (shader type 5 = ST_SkinTint): apply NPC's QNAM TextureLighting color
        if (built.ShaderType == 5 && meshPaths.TextureLightingColor.HasValue)
        {
            var (r, g, b) = meshPaths.TextureLightingColor.Value;
            glMesh.HasTintColor = true;
            glMesh.TintColor = new System.Numerics.Vector3(r, g, b);
        }

        // Eye shader (shader type 16 = ST_EyeEnvmap)
        if (built.ShaderType == 16)
        {
            glMesh.IsEye = true;
        }

        // Environment mapping (SLSF1_Environment_Mapping bit 7, or SLSF1_Eye_Environment_Mapping bit 17)
        bool hasEnvMap = (built.ShaderFlags1 & (1u << 7)) != 0;
        bool hasEyeEnvMap = (built.ShaderFlags1 & (1u << 17)) != 0;
        if ((hasEnvMap || hasEyeEnvMap) && effectiveTextures.TryGetValue(4, out string? envMapPath))
        {
            var envTex = TextureManager.LoadCubemap(envMapPath);
            if (envTex != 0)
            {
                glMesh.EnvMapTexture = envTex;
                glMesh.HasEnvironmentMap = true;
                glMesh.EnvMapScale = built.EnvironmentMapScale;
                glMesh.EyeCubemapScale = built.EyeCubemapScale;
                RecordTextureSource(glMesh, "Environment Cubemap", envMapPath);
            }

            if (effectiveTextures.TryGetValue(5, out string? envMaskPath))
            {
                glMesh.EnvMaskTexture = TextureManager.LoadTexture(envMaskPath);
                glMesh.HasEnvMask = true;
                RecordTextureSource(glMesh, "Environment Mask", envMaskPath);
            }
        }

        // Detail map (SLSF1_Facegen_Detail_Map, bit 10)
        if ((built.ShaderFlags1 & (1u << 10)) != 0 && effectiveTextures.TryGetValue(3, out string? detailPath))
        {
            glMesh.DetailTexture = TextureManager.LoadTexture(detailPath);
            glMesh.HasDetailMap = true;
            RecordTextureSource(glMesh, "Detail Map", detailPath);
        }

        // Double-sided (brow, eyelash, hair — thin geometry visible from both sides)
        glMesh.IsDoubleSided = built.IsDoubleSided;

        // Alpha test / blend
        if (built.HasAlphaTest || built.HasAlphaBlend)
        {
            if (glMesh.DiffuseTexture != TextureManager.WhiteTexture)
            {
                glMesh.UseAlphaTest = built.HasAlphaTest;
                glMesh.HasAlphaBlend = built.HasAlphaBlend;
                glMesh.AlphaThreshold = built.AlphaThreshold;
            }
            else
            {
                glMesh.IsRendering = false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    public void ApplyTextureOverrides(IEnumerable<FilePathReplacement> overrides)
    {
        var overrideList = overrides.ToList();

        // Queue when the scene is empty, the texture manager isn't ready, OR a
        // rebuild is in-flight. The rebuild check is what catches the subgroup
        // re-selection case: between LoadNpcAsync queueing _pendingScene and the
        // render callback running ClearScene()+rebuild, _meshesByBodyPart still
        // holds the previous meshes and without this flag we'd apply overrides
        // to meshes that are about to be destroyed.
        if (_meshesByBodyPart.Count == 0 || TextureManager == null || _sceneRebuildPending)
        {
            _logger.LogMessage("CharacterViewer: ApplyTextureOverrides queuing " + overrideList.Count +
                " override(s); meshes=" + _meshesByBodyPart.Count +
                ", texMgr=" + (TextureManager != null) +
                ", rebuildPending=" + _sceneRebuildPending);
            _pendingTextureOverrides = overrideList;
            return;
        }

        _logger.LogMessage("CharacterViewer: ApplyTextureOverrides applying " + overrideList.Count +
            " override(s); tracked body parts: [" + string.Join(", ", _meshesByBodyPart.Keys) + "]");

        foreach (var replacement in overrideList)
        {
            string dest = replacement.Destination;
            if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(replacement.Source))
                continue;

            string? bodyPart = ParseBodyPart(dest);
            int? slot = ParseTextureSlot(dest);
            if (bodyPart == null || slot == null)
            {
                _logger.LogMessage("CharacterViewer: Override unparseable — dest='" + dest + "'");
                continue;
            }

            // For Head, target only the primary head shape (the face — face/hair/eyes
            // are separate shapes with different meaning for each slot). For non-head
            // body parts, apply to every shape in that NIF: some NIFs contain multiple
            // body-part shapes (e.g. hands + fingernails, body + belt) and previously
            // only the first-registered shape got the override, leaving the hovered
            // shape showing the original texture.
            List<GlMesh> targets;
            if (bodyPart == "Head")
            {
                if (!_meshesByBodyPart.TryGetValue(bodyPart, out var headMesh))
                {
                    _logger.LogMessage("CharacterViewer: No Head mesh tracked for override — dest='" + dest + "'");
                    continue;
                }
                targets = new List<GlMesh> { headMesh };
            }
            else
            {
                targets = Renderer.Meshes.Where(m => m.BodyPart == bodyPart).ToList();
                if (targets.Count == 0)
                {
                    _logger.LogMessage("CharacterViewer: No meshes with BodyPart='" + bodyPart +
                        "' (slot " + slot + ") — dest='" + dest + "'");
                    continue;
                }
            }

            foreach (var mesh in targets)
            {
                if (slot.Value == 0)
                {
                    mesh.DiffuseTexture = TextureManager.LoadTexture(replacement.Source);
                    RecordTextureSource(mesh, "Diffuse", replacement.Source);
                }
                else if (slot.Value == 1)
                {
                    // Shader handles MSN natively; no CPU resampling needed.
                    mesh.NormalTexture = TextureManager.LoadTexture(replacement.Source);
                    mesh.HasNormalMap = true;
                    RecordTextureSource(mesh, "Normal Map", replacement.Source);
                }
                else if (slot.Value == 2)
                {
                    // Skin/SSS — only meaningful on skin-shader meshes; harmless on others
                    // since HasSkinMap gates shader sampling.
                    mesh.SkinTexture = TextureManager.LoadTexture(replacement.Source);
                    mesh.HasSkinMap = true;
                    RecordTextureSource(mesh, "Skin/SSS", replacement.Source);
                }
                else if (slot.Value == 7)
                {
                    mesh.SpecularTexture = TextureManager.LoadTexture(replacement.Source);
                    mesh.HasSpecularMap = true;
                    mesh.HasSpecular = true;
                    RecordTextureSource(mesh, "Specular", replacement.Source);
                }
            }

            _logger.LogMessage("CharacterViewer: Slot " + slot + " override '" + replacement.Source +
                "' → " + bodyPart + " (" + targets.Count + " shape(s))");
        }
    }

    /// <summary>
    /// Resolves a texture's origin via the asset resolver and records it on
    /// the mesh for display in the hover tooltip. If the slot already has an
    /// entry, replaces it (used when texture overrides update a slot).
    /// </summary>
    private void RecordTextureSource(GlMesh mesh, string slotLabel, string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return;

        var source = _assetResolver.ResolveAssetSource(gamePath);

        for (int i = 0; i < mesh.TextureSources.Count; i++)
        {
            if (mesh.TextureSources[i].SlotLabel == slotLabel)
            {
                mesh.TextureSources[i] = (slotLabel, source);
                return;
            }
        }
        mesh.TextureSources.Add((slotLabel, source));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BODYSLIDE
    // ═══════════════════════════════════════════════════════════════════════

    // Kill-switch for the BodySlide deformation path. Originally set true while the
    // neck seam was under investigation; flipped back to false once the viewer was
    // integrated into the OBody editor. Kept as a mutable static (not const) so the
    // branch remains live code and this can be toggled again without code changes
    // if the seam or another deformation artifact returns.
    private static bool _bodySlideDisabled = false;

    public void ApplyBodySlide(BodySlideSetting preset, int weight)
    {
        if (_bodySlideDisabled)
        {
            NpcWeight = Math.Clamp(weight, 0, 100);
            _logger.LogMessage("CharacterViewer: [BodySlideDisabled] ApplyBodySlide bypassed" +
                " (preset='" + (preset?.Label ?? "?") + "', weight=" + NpcWeight + ")");
            return;
        }

        // If scene isn't set up yet (pending GL work), queue for later.
        // Checking Count == 0 alone isn't enough: during a scene rebuild the previous
        // scene's meshes linger in _cachedBodyMeshes until the new load overwrites them,
        // so an ApplyBodySlide fired mid-rebuild would deform the OLD meshes and the
        // replacement load would then discard the deformation. Also gate on
        // _sceneRebuildPending so we always queue until the rebuild has committed.
        if (_cachedBodyMeshes.Count == 0 || _sceneRebuildPending)
        {
            _pendingBodySlide = (preset, weight);
            return;
        }

        NpcWeight = Math.Clamp(weight, 0, 100);

        try
        {
            // Preferred path: if a sibling .tri exists next to the worn body NIF
            // (BodySlide's "Build Morphs" output), use it. Its sparse vertex deltas
            // are authored against this exact NIF's topology, so we sidestep the
            // OSD path's reference-mesh mismatch (chopped deformation bands).
            TryLoadSiblingBodyTri();

            // OSD fallback: only load the slider-group OSD catalog when we don't
            // have a .tri to use. Avoids a wasted ShapeData scan on every
            // preset/weight change for the common case.
            if (_cachedBodyTri == null && preset.SliderGroup != null)
                LoadOsdFilesForGroup(preset.SliderGroup);

            bool haveDeltas = _cachedBodyTri != null
                           || (_cachedOsdFiles != null && _cachedOsdFiles.Count > 0);
            if (!haveDeltas) return;

            foreach (var kvp in _cachedBodyMeshes)
            {
                string shapeName = kvp.Key;
                var originalMesh = kvp.Value;

                // Find the GL mesh for this shape
                var glMesh = Renderer.Meshes.FirstOrDefault(m => m.ShapeName == shapeName);
                if (glMesh == null) continue;

                // Start from bind-pose positions
                var sourcePositions = originalMesh.BindPosePositions ?? originalMesh.Positions;
                var positions = new Vector3[sourcePositions.Length];
                Array.Copy(sourcePositions, positions, sourcePositions.Length);

                // Apply deformation -- prefer .tri (topology-matched, no LCP stripping),
                // fall back to OSD for meshes without "Build Morphs" output.
                if (_cachedBodyTri != null)
                {
                    _bodySlideDeformer.ApplyDeformationFromTri(positions, preset, NpcWeight, _cachedBodyTri, shapeName);
                }
                else
                {
                    _bodySlideDeformer.ApplyDeformation(positions, preset, NpcWeight, _cachedOsdFiles!, shapeName);
                }

                // Recalculate normals
                var sourceNormals = originalMesh.BindPoseNormals ?? originalMesh.Normals;
                var normals = new Vector3[sourceNormals.Length];
                Array.Copy(sourceNormals, normals, sourceNormals.Length);
                BodySlideDeformer.RecalculateNormals(positions, originalMesh.Indices, normals);

                // Re-apply skinning
                if (originalMesh.Skinning != null)
                    NifMeshBuilder.ApplySkinning(positions, normals, originalMesh.Skinning, positions, normals);

                // Re-upload vertex data to GPU
                var vertexData = BuildInterleavedVertexData(positions, normals,
                    originalMesh.TextureCoordinates, originalMesh.Tangents, originalMesh.Bitangents,
                    originalMesh.VertexColors);
                glMesh.UpdateVertexData(vertexData);

                // Update CPU-side positions for hit testing
                glMesh.CpuPositions = positions;
            }
        }
        catch (Exception ex)
        {
            // Surface deformer / skinning / GPU-upload failures with a full stack so the
            // Status Log actually shows what went wrong. Without this catch the exception
            // bubbles up to VM_BodySlideSetting.RefreshPreview, which used to swallow it
            // silently via LogMessage and the user only saw the tab-switch with no detail.
            _logger.LogError("CharacterViewer: ApplyBodySlide failed for preset '"
                + (preset?.Label ?? "?") + "' at weight " + NpcWeight + Environment.NewLine
                + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BODYGEN OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies a stack of BodyGen templates by parsing and summing their Specs into
    /// a virtual BodySlideSetting, then routing through the existing ApplyBodySlide
    /// path. Matches BodyGen runtime behavior where templates stack additively on
    /// the same NPC.
    /// </summary>
    public void ApplyBodyGen(IEnumerable<BodyGenConfig.BodyGenTemplate> templates, string sliderGroup, int weight)
    {
        var list = templates?.Where(t => t != null).ToList() ?? new List<BodyGenConfig.BodyGenTemplate>();
        if (list.Count == 0) return;

        var merged = BodyGenSpecsParser.ParseAndMerge(
            list.Select(t => t.Specs ?? string.Empty),
            sliderGroup,
            out var errors);

        if (errors.Count > 0)
        {
            _logger.LogMessage("CharacterViewer.ApplyBodyGen parse warnings: " + string.Join("; ", errors));
        }

        if (merged.SliderValues.Count == 0) return;
        ApplyBodySlide(merged, weight);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HEADPART OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reloads <paramref name="npcFormKey"/> with <paramref name="assignments"/> applied
    /// as head-part overrides. Generates a preview FaceGen NIF via FaceGenPatcher and
    /// hands its path to <see cref="LoadNpcAsync"/> as the head-mesh override, matching
    /// the flow used by the Headparts editor (single-type) but supporting a full
    /// multi-type dictionary.
    /// </summary>
    public async Task ApplyHeadPartsAsync(FormKey npcFormKey, ILinkCache linkCache, IReadOnlyDictionary<HeadPart.TypeEnum, FormKey> assignments, CancellationToken ct = default)
    {
        if (npcFormKey.IsNull || linkCache == null) return;

        var validAssignments = assignments?
            .Where(kv => !kv.Value.IsNull)
            .ToDictionary(kv => kv.Key, kv => kv.Value) ?? new();

        if (validAssignments.Count == 0)
        {
            await LoadNpcAsync(npcFormKey, linkCache);
            return;
        }

        string? nifPath;
        try
        {
            nifPath = await _faceGenPreviewService.GeneratePreviewFaceGenAsync(npcFormKey, validAssignments, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer.ApplyHeadPartsAsync: preview FaceGen generation failed: " + ex.Message);
            nifPath = null;
        }

        ct.ThrowIfCancellationRequested();
        await LoadNpcAsync(npcFormKey, linkCache, overrideHeadMeshAbsolutePath: nifPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCENE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    public void ClearScene()
    {
        Renderer.ClearMeshes();
        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedOsdFiles = null;
        _cachedBodyNifDiskPath = null;
        _cachedBodyTri = null;
    }

    private bool _disposed;

    /// <summary>
    /// Releases GL resources (shaders, VBO/VAO, loaded textures), cancels any in-flight
    /// NPC-load async work, and drops scene caches. Called when the owning parent VM
    /// (e.g. VM_BodyGenTemplateMenu, VM_BodySlideSetting) is itself disposed — which
    /// in turn happens when its grandparent (e.g. a BodyGen config being swapped) is
    /// torn down.
    ///
    /// GL delete calls must ideally run while the GL context is current. When the
    /// owning UserControl has already been unloaded, the context may no longer be
    /// current on this thread; in that case GL.DeleteBuffer / DeleteTexture on most
    /// drivers are silent no-ops (the resources are reclaimed when the context itself
    /// is destroyed). We wrap in try/catch so a stray driver throw doesn't propagate
    /// out of the dispose chain and bring down the settings load.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancel any in-flight NPC load so its continuation doesn't race with
        // the scene being torn down.
        try
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
        }
        catch { /* best-effort */ }
        _loadCts = null;

        // Drop every scene-level cache; _pending* holders would otherwise pin
        // BuiltMesh data (with its vertex/index buffers) until GC.
        _pendingScene = null;
        _pendingTextureOverrides = null;
        _pendingBodySlide = null;
        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedOsdFiles = null;
        _cachedBodyNifDiskPath = null;
        _cachedBodyTri = null;
        _cachedMeshPaths = null;

        try
        {
            if (IsGlInitialized)
            {
                TextureManager?.Dispose();
                TextureManager = null;
                Renderer.Dispose();
                IsGlInitialized = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("VM_CharacterViewer.Dispose: GL cleanup threw: "
                + ExceptionLogger.GetExceptionStack(ex));
        }

        // Tears down reactive subscriptions added via DisposeWith(this).
        base.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadAllMeshParts(
        NpcMeshResolver.NpcMeshPaths meshPaths)
    {
        var results = new List<(string, AssetSource?, List<NifMeshBuilder.BuiltMesh>)>();

        nifly.NifFile? skeletonNif = null;
        if (!string.IsNullOrWhiteSpace(meshPaths.SkeletonPath))
        {
            string? skelDiskPath = _assetResolver.ResolveAssetPath(meshPaths.SkeletonPath);
            if (skelDiskPath != null)
            {
                skeletonNif = new nifly.NifFile();
                if (skeletonNif.Load(skelDiskPath) != 0)
                {
                    skeletonNif.Dispose();
                    skeletonNif = null;
                }
            }
        }

        try
        {
            void TryLoad(string bodyPart, string? gamePath)
            {
                if (string.IsNullOrWhiteSpace(gamePath)) return;
                var source = _assetResolver.ResolveAssetSource(gamePath);
                if (source.ResolvedDiskPath == null) return;
                var meshes = _meshBuilder.BuildFromFile(source.ResolvedDiskPath, skeletonNif);
                if (meshes.Count == 0) return;

                // Weight morph: armor meshes ship as _0/_1 pairs that the game engine
                // linearly interpolates by NpcWeight (0..100). The FaceGen head is already
                // baked at the NPC's weight so it needs no morph. Skinning is linear in
                // vertex position, so blending the already-skinned world-space positions
                // is equivalent to blending bind-pose and re-skinning (both _0 and _1
                // share the same skeleton and skinToBone transforms).
                if (bodyPart != "Head" && NpcWeight < 100)
                {
                    string? weight0Path = TryGetWeightZeroPath(gamePath);
                    if (weight0Path != null)
                    {
                        var weight0Source = _assetResolver.ResolveAssetSource(weight0Path);
                        if (weight0Source.ResolvedDiskPath != null)
                        {
                            var meshes0 = _meshBuilder.BuildFromFile(weight0Source.ResolvedDiskPath, skeletonNif);
                            float t = NpcWeight / 100f;
                            BlendWeightMorph(meshes0, meshes, t, bodyPart);
                        }
                        else
                        {
                            _logger.LogMessage("CharacterViewer: [WeightMorph] '" + bodyPart +
                                "' weight-0 '" + weight0Path + "' not found — using _1.nif unmorphed");
                        }
                    }
                    else
                    {
                        _logger.LogMessage("CharacterViewer: [WeightMorph] '" + bodyPart +
                            "' path '" + gamePath + "' does not end in _1.nif — skipping weight morph");
                    }
                }

                results.Add((bodyPart, source, meshes));
            }

            TryLoad("Body", meshPaths.BodyMeshPath);
            TryLoad("Hands", meshPaths.HandsMeshPath);
            TryLoad("Feet", meshPaths.FeetMeshPath);
            TryLoad("Head", meshPaths.HeadMeshPath);
        }
        finally
        {
            skeletonNif?.Dispose();
        }

        return results;
    }

    /// <summary>
    /// Creates a GlMesh from a BuiltMesh, uploading interleaved vertex data and indices.
    /// </summary>
    private GlMesh CreateGlMesh(NifMeshBuilder.BuiltMesh built)
    {
        var vertexData = BuildInterleavedVertexData(
            built.Positions, built.Normals, built.TextureCoordinates,
            built.Tangents, built.Bitangents, built.VertexColors);

        var glMesh = new GlMesh();
        glMesh.Upload(vertexData, built.Indices);
        glMesh.ShapeName = built.ShapeName;
        glMesh.IsPrimaryHeadShape = built.IsPrimaryHeadShape;

        // Store CPU-side geometry for ray-based hit testing
        glMesh.CpuPositions = (Vector3[])built.Positions.Clone();
        glMesh.CpuIndices = (int[])built.Indices.Clone();

        return glMesh;
    }

    /// <summary>
    /// Builds interleaved vertex data array for the GL mesh.
    /// Layout per vertex: position(3) + normal(3) + texcoord(2) + color(4) + tangent(3) + bitangent(3) = 18 floats
    /// </summary>
    private static float[] BuildInterleavedVertexData(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        Vector3[] tangents, Vector3[] bitangents, Vector4[]? vertexColors = null)
    {
        int vertCount = positions.Length;
        var data = new float[vertCount * 18];

        for (int i = 0; i < vertCount; i++)
        {
            int offset = i * 18;
            var p = positions[i];
            var n = i < normals.Length ? normals[i] : Vector3.UnitY;
            var uv = i < uvs.Length ? uvs[i] : Vector2.Zero;
            var t = i < tangents.Length ? tangents[i] : Vector3.Zero;
            var b = i < bitangents.Length ? bitangents[i] : Vector3.Zero;

            // Position
            data[offset]     = p.X;
            data[offset + 1] = p.Y;
            data[offset + 2] = p.Z;
            // Normal
            data[offset + 3] = n.X;
            data[offset + 4] = n.Y;
            data[offset + 5] = n.Z;
            // UV
            data[offset + 6] = uv.X;
            data[offset + 7] = uv.Y;
            // Vertex color
            if (vertexColors != null && i < vertexColors.Length)
            {
                var vc = vertexColors[i];
                data[offset + 8]  = vc.X;
                data[offset + 9]  = vc.Y;
                data[offset + 10] = vc.Z;
                data[offset + 11] = vc.W;
            }
            else
            {
                data[offset + 8]  = 1f;
                data[offset + 9]  = 1f;
                data[offset + 10] = 1f;
                data[offset + 11] = 1f;
            }
            // Tangent
            data[offset + 12] = t.X;
            data[offset + 13] = t.Y;
            data[offset + 14] = t.Z;
            // Bitangent
            data[offset + 15] = b.X;
            data[offset + 16] = b.Y;
            data[offset + 17] = b.Z;
        }

        return data;
    }

    /// <summary>
    /// Looks for a sibling .tri next to the currently-loaded body NIF and parses it
    /// on first use. Result (including parse failure → null) is cached per scene so
    /// this is a no-op on subsequent preset/weight changes. Silently does nothing
    /// if no body NIF path has been captured yet.
    ///
    /// Probes both naming conventions Skyrim uses: the .tri may share the NIF's
    /// stem verbatim (e.g. `custombody.nif` → `custombody.tri`) or may be the
    /// weight-stripped form (e.g. `FemaleBody_1.nif` → `femalebody.tri`), since
    /// vanilla / BodySlide-built bodies ship paired `_0`/`_1` NIFs but a single
    /// shared .tri whose morph deltas are identical between weight variants.
    /// </summary>
    private void TryLoadSiblingBodyTri()
    {
        if (_cachedBodyTri != null) return;
        if (_cachedBodyNifDiskPath == null) return;

        string? triPath = ProbeSiblingTriPath(_cachedBodyNifDiskPath);
        if (triPath == null)
        {
            _logger.LogMessage("CharacterViewer: No sibling .tri found for '" + _cachedBodyNifDiskPath +
                "' -- falling back to OSD path (chopping bug possible if topology mismatches reference).");
            _cachedBodyNifDiskPath = null; // don't re-probe
            return;
        }

        _cachedBodyTri = _bodyTriFileParser.Parse(triPath);
        if (_cachedBodyTri == null)
        {
            _logger.LogMessage("CharacterViewer: Sibling .tri at '" + triPath +
                "' failed to parse -- falling back to OSD path.");
            _cachedBodyNifDiskPath = null;
            return;
        }

        int totalMorphs = 0;
        foreach (var shape in _cachedBodyTri.Shapes) totalMorphs += shape.Morphs.Count;
        _logger.LogMessage("CharacterViewer: Using sibling .tri '" + triPath + "' (" +
            _cachedBodyTri.Shapes.Count + " shape(s), " + totalMorphs + " total morph(s))");
    }

    /// <summary>
    /// Returns the first existing .tri sibling for <paramref name="nifDiskPath"/>,
    /// trying the stripped-weight-suffix form first (matches vanilla/BodySlide
    /// convention). Null if neither exists.
    /// </summary>
    private static string? ProbeSiblingTriPath(string nifDiskPath)
    {
        string dir = Path.GetDirectoryName(nifDiskPath) ?? "";
        string stem = Path.GetFileNameWithoutExtension(nifDiskPath);

        // Strip trailing _0 or _1 weight suffix if present.
        string strippedStem = stem;
        if (stem.Length > 2 && stem[^2] == '_' && (stem[^1] == '0' || stem[^1] == '1'))
        {
            strippedStem = stem.Substring(0, stem.Length - 2);
        }

        if (!string.Equals(strippedStem, stem, StringComparison.Ordinal))
        {
            string strippedPath = Path.Combine(dir, strippedStem + ".tri");
            if (File.Exists(strippedPath)) return strippedPath;
        }

        string samePath = Path.Combine(dir, stem + ".tri");
        if (File.Exists(samePath)) return samePath;

        return null;
    }

    private void LoadOsdFilesForGroup(string sliderGroup)
    {
        string dataFolder = _environmentProvider.DataFolderPath;
        string shapeDataRoot = Path.Combine(dataFolder, "CalienteTools", "BodySlide", "ShapeData");
        if (!Directory.Exists(shapeDataRoot))
        {
            _cachedOsdFiles = new List<OsdFile>();
            return;
        }

        // Primary path: look up the body type in the registry and parse the OSD/BSD files in
        // the entry's declared ShapeDataFolders. If the entry is a superset of another body
        // (e.g. CBBE 3BA ⊃ CBBE), include the parent's folders too -- a 3BA preset may move
        // CBBE-shared sliders whose deltas live only in the CBBE shape data.
        var registry = _patcherState?.OBodySettings?.BodyTypeRegistry;
        var entry = FindRegistryEntry(registry, sliderGroup);
        if (entry != null)
        {
            var folders = new List<string>();
            CollectShapeDataFolders(entry, registry, folders, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registryOsd = new List<OsdFile>();
            foreach (var rawPath in folders)
            {
                var sub = rawPath.Replace('/', Path.DirectorySeparatorChar)
                                 .Replace('\\', Path.DirectorySeparatorChar)
                                 .TrimStart(Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(shapeDataRoot, sub);
                if (File.Exists(fullPath))
                {
                    var osd = string.Equals(Path.GetExtension(fullPath), ".bsd", StringComparison.OrdinalIgnoreCase)
                        ? _bsdFileParser.ParseBsdFile(fullPath)
                        : _bsdFileParser.ParseOsdFile(fullPath);
                    if (osd != null && seen.Add(osd.ShapeName)) registryOsd.Add(osd);
                }
                else if (Directory.Exists(fullPath))
                {
                    foreach (var osd in _bsdFileParser.ParseAllOsdInDirectory(fullPath, recursive: false))
                    {
                        if (osd != null && seen.Add(osd.ShapeName)) registryOsd.Add(osd);
                    }
                }
            }
            _cachedOsdFiles = registryOsd;
            return;
        }

        // Fallback (registry miss / "Unknown" preset): legacy substring scan over every direct
        // child of ShapeData, then full-tree scan if no name contained the group string.
        var matchingDirs = Directory.GetDirectories(shapeDataRoot)
            .Where(d => Path.GetFileName(d).Contains(sliderGroup, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingDirs.Length == 0)
            matchingDirs = Directory.GetDirectories(shapeDataRoot);

        var allOsd = new List<OsdFile>();
        foreach (var dir in matchingDirs)
            allOsd.AddRange(_bsdFileParser.ParseAllOsdInDirectory(dir));

        _cachedOsdFiles = allOsd;
    }

    private static BodyTypeRegistryEntry FindRegistryEntry(List<BodyTypeRegistryEntry> registry, string name)
    {
        if (registry == null || string.IsNullOrWhiteSpace(name)) return null;
        foreach (var e in registry)
        {
            if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
        }
        return null;
    }

    private static void CollectShapeDataFolders(BodyTypeRegistryEntry entry, List<BodyTypeRegistryEntry> registry, List<string> folders, HashSet<string> visited)
    {
        if (entry == null || !visited.Add(entry.Name)) return;
        if (entry.ShapeDataFolders != null)
        {
            foreach (var f in entry.ShapeDataFolders)
            {
                if (!string.IsNullOrWhiteSpace(f)) folders.Add(f);
            }
        }
        if (!string.IsNullOrWhiteSpace(entry.SupersetOfBodyType))
        {
            var parent = FindRegistryEntry(registry, entry.SupersetOfBodyType);
            CollectShapeDataFolders(parent, registry, folders, visited);
        }
    }

    // Derives the weight-0 counterpart path for a NIF path that ends in "_1.nif".
    // Returns null if the input doesn't follow the standard BodySlide weight-pair naming.
    private static string? TryGetWeightZeroPath(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return null;
        const string suffix = "_1.nif";
        if (gamePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return gamePath.Substring(0, gamePath.Length - suffix.Length) + "_0.nif";
        return null;
    }

    // Linearly interpolates weight-0 geometry into the weight-1 meshes by factor t,
    // where t = NpcWeight / 100 (t=0 → all weight-0, t=1 → all weight-1). Matches the
    // game engine's body-weight morph. Operates in-place on meshes1 arrays.
    // Shape matching is by ShapeName; mismatched shapes or vertex counts are skipped.
    private void BlendWeightMorph(List<NifMeshBuilder.BuiltMesh> meshes0,
        List<NifMeshBuilder.BuiltMesh> meshes1, float t, string bodyPart)
    {
        foreach (var m1 in meshes1)
        {
            var m0 = meshes0.FirstOrDefault(m => m.ShapeName == m1.ShapeName);
            if (m0 == null)
            {
                _logger.LogMessage("CharacterViewer: [WeightMorph] '" + bodyPart + "' shape '" +
                    m1.ShapeName + "' has no match in weight-0 NIF — skipping");
                continue;
            }
            if (m0.Positions.Length != m1.Positions.Length)
            {
                _logger.LogMessage("CharacterViewer: [WeightMorph] '" + bodyPart + "' shape '" +
                    m1.ShapeName + "' vertex count mismatch (_0=" + m0.Positions.Length +
                    ", _1=" + m1.Positions.Length + ") — skipping");
                continue;
            }

            int n = m1.Positions.Length;
            for (int i = 0; i < n; i++)
                m1.Positions[i] = Vector3.Lerp(m0.Positions[i], m1.Positions[i], t);

            BlendAndRenormalize(m0.Normals, m1.Normals, t, n);
            BlendAndRenormalize(m0.Tangents, m1.Tangents, t, n);
            BlendAndRenormalize(m0.Bitangents, m1.Bitangents, t, n);

            if (m0.BindPosePositions != null && m1.BindPosePositions != null &&
                m0.BindPosePositions.Length == n && m1.BindPosePositions.Length == n)
            {
                for (int i = 0; i < n; i++)
                    m1.BindPosePositions[i] = Vector3.Lerp(m0.BindPosePositions[i], m1.BindPosePositions[i], t);
            }
            if (m0.BindPoseNormals != null && m1.BindPoseNormals != null &&
                m0.BindPoseNormals.Length == n && m1.BindPoseNormals.Length == n)
            {
                BlendAndRenormalize(m0.BindPoseNormals, m1.BindPoseNormals, t, n);
            }

            _logger.LogMessage("CharacterViewer: [WeightMorph] '" + bodyPart + "' shape '" +
                m1.ShapeName + "' blended " + n + " verts at t=" + t.ToString("F2"));
        }
    }

    private static void BlendAndRenormalize(Vector3[] src0, Vector3[] src1, float t, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var v = Vector3.Lerp(src0[i], src1[i], t);
            float len = v.Length();
            src1[i] = len > 1e-6f ? v / len : src1[i];
        }
    }

    public static string? ParseBodyPart(string destination)
    {
        if (destination.StartsWith("HeadTexture", StringComparison.OrdinalIgnoreCase))
            return "Head";
        if (destination.Contains("SkinTexture", StringComparison.OrdinalIgnoreCase) ||
            destination.Contains("WorldModel", StringComparison.OrdinalIgnoreCase))
        {
            if (destination.Contains("BipedObjectFlag.Body", StringComparison.OrdinalIgnoreCase)) return "Body";
            if (destination.Contains("BipedObjectFlag.Hands", StringComparison.OrdinalIgnoreCase)) return "Hands";
            if (destination.Contains("BipedObjectFlag.Feet", StringComparison.OrdinalIgnoreCase)) return "Feet";
        }
        return null;
    }

    public static int? ParseTextureSlot(string destination)
    {
        if (destination.Contains("BacklightMaskOrSpecular", StringComparison.OrdinalIgnoreCase)) return 7;
        if (destination.Contains("NormalOrGloss", StringComparison.OrdinalIgnoreCase)) return 1;
        if (destination.Contains("GlowOrDetailMap", StringComparison.OrdinalIgnoreCase)) return 2;
        if (destination.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)) return 0;
        if (destination.Contains("Height", StringComparison.OrdinalIgnoreCase)) return 3;
        return null;
    }
}
