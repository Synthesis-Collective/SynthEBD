using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace CharacterViewer.Rendering;

/// <summary>How a superimposed guest model is drawn over the primary one.</summary>
public enum GuestOverlayStyle
{
    /// <summary>Fully textured and lit, exactly like the primary model. Honest, but the two
    /// models interpenetrate and are hard to tell apart — useful mainly for spotting where
    /// one silhouette pokes out of the other.</summary>
    Textured,

    /// <summary>Flat-tinted and semi-transparent, drawn without depth writes so the primary
    /// model stays visible through it. The default: reads clearly as "the other one".</summary>
    Translucent,

    /// <summary>Triangle edges only, in the guest tint. Cheapest to read at a glance and
    /// never hides the primary model, at the cost of showing no volume.</summary>
    Wireframe,
}

public partial class VM_CharacterViewer
{
    // ═══════════════════════════════════════════════════════════════════════
    //  GUEST OVERLAY SCENE ("superimpose")
    //
    //  A complete SECOND scene installed alongside the primary one so two
    //  BodySlide presets can be compared at the same origin. Used by SynthEBD's
    //  BodySlide Compare window, where pane B projects its model into pane A.
    //
    //  Why the guest is loaded by THIS VM rather than transplanted from the
    //  other pane's viewer: GL objects are per-context and cannot be shared
    //  across two GLWpfControls. The other pane's GlMeshes are meaningless
    //  here, so the guest goes through this VM's own builder, texture manager
    //  and context — the same pipeline as the primary scene, just installed
    //  into a separate mesh list that ClearScene's bookkeeping never sees.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Everything needed to (re-)install a guest overlay without re-parsing NIFs.
    /// Retained in <see cref="_guestRequest"/> so the overlay can be rebuilt after the
    /// primary scene's ClearScene destroys its GL meshes, and when the style/scope changes.</summary>
    private sealed record GuestSceneRequest(
        List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadResults,
        ResolvedNpcMeshPaths MeshPaths,
        MorphSet? Morphs,
        int Weight,
        List<OsdFile>? OsdFiles,
        string CacheKey);

    /// <summary>The guest's GL meshes, tracked separately from <c>_meshesByBodyPart</c> so
    /// nothing in the primary scene's texture / morph / slot-occupancy bookkeeping treats
    /// them as part of the NPC being edited.</summary>
    private readonly List<GlMesh> _guestMeshes = new();

    /// <summary>Retained guest request; non-null whenever an overlay is meant to be showing.</summary>
    private GuestSceneRequest? _guestRequest;

    /// <summary>Set when <see cref="_guestRequest"/> needs (re-)installing on the next render
    /// tick — a fresh load, a style/scope change, or a primary ClearScene having torn the
    /// guest meshes down along with everything else.</summary>
    private bool _guestInstallPending;

    /// <summary>Parsed sibling .tri for the guest's body NIF, cached alongside the request.</summary>
    private BodyTriFile? _guestBodyTri;

    /// <summary>Generation guard for overlapping <see cref="LoadGuestAsync"/> calls: only the
    /// newest load queues its result. Mirrors the primary scene's cancellation semantics
    /// without needing a second CancellationTokenSource, since guest loads are pure
    /// parse-and-queue work with no GL side effects until the install tick.</summary>
    private int _guestLoadGeneration;

    private GuestOverlayStyle _guestStyle = GuestOverlayStyle.Translucent;
    private bool _guestBodyOnly = true;

    /// <summary>
    /// How the guest overlay is drawn. Changing it restyles the existing meshes in place —
    /// no reload, no re-parse.
    /// </summary>
    public GuestOverlayStyle GuestStyle
    {
        get => _guestStyle;
        set
        {
            if (_guestStyle == value) return;
            _guestStyle = value;
            foreach (var mesh in _guestMeshes) ApplyGuestStyle(mesh);
        }
    }

    /// <summary>
    /// When true, only the guest's body mesh is overlaid; head, hair and worn gear are
    /// skipped. Changing it re-installs from the retained request (the shape set itself
    /// changes, so an in-place restyle can't express it) but still avoids a NIF re-parse.
    /// </summary>
    public bool GuestBodyOnly
    {
        get => _guestBodyOnly;
        set
        {
            if (_guestBodyOnly == value) return;
            _guestBodyOnly = value;
            if (_guestRequest != null) _guestInstallPending = true;
        }
    }

    /// <summary>Flat tint applied to the guest in Translucent and Wireframe styles. A warm
    /// orange chosen to sit clear of the teal classifier wireframe, the green
    /// missing-texture wireframe, and the skin tones underneath it.</summary>
    public System.Numerics.Vector3 GuestTintColor { get; set; } = new(1.0f, 0.45f, 0.12f);

    /// <summary>Opacity of the guest in <see cref="GuestOverlayStyle.Translucent"/>.</summary>
    public float GuestAlpha { get; set; } = 0.45f;

    /// <summary>True while a guest overlay is loaded or queued for install.</summary>
    public bool HasGuestOverlay => _guestRequest != null;

    /// <summary>
    /// Loads <paramref name="identity"/> as a guest overlay on top of the current primary
    /// scene, deformed by <paramref name="morphs"/> at <paramref name="weight"/>.
    ///
    /// <para>Neutral entry point (no Mutagen / SynthEBD types) — SynthEBD calls it through
    /// <c>CharacterViewerSynthEbdExtensions.LoadGuestNpcAsync</c>, which resolves the FormKey
    /// and translates a <c>BodySlideSetting</c> into the <see cref="MorphSet"/> and OSD
    /// context this takes.</para>
    ///
    /// <para>Parsing happens off-thread; the GL install is deferred to the render callback
    /// exactly like the primary scene's. Repeated calls describing the same overlay
    /// short-circuit, so a host may call this freely whenever its inputs change.</para>
    /// </summary>
    public async Task LoadGuestAsync(NpcIdentity identity, MorphSet? morphs, int weight,
        List<OsdFile>? osdFiles = null)
    {
        if (RenderingUnavailable || ForceRenderingUnavailableForTesting)
        {
            // The software fallback renders a single offscreen scene from retained inputs and
            // has no concept of a second model, so there is nothing meaningful to show.
            LogVerbose("CharacterViewer: guest overlay skipped — rendering unavailable.");
            return;
        }

        // Identity + preset + weight fully determine the overlay's geometry. Style and scope
        // are deliberately NOT in the key: both are applied post-install and must not force a
        // reload (style restyles in place, scope re-installs from the retained request).
        string cacheKey = identity.CacheKey + "|" + (morphs?.Label ?? "") + "|" + weight;
        if (_guestRequest?.CacheKey == cacheKey && !_guestInstallPending) return;

        int myGen = ++_guestLoadGeneration;

        try
        {
            var meshPaths = await Task.Run(() => _previewCache.GetOrResolveMeshPaths(identity));
            if (meshPaths == null)
            {
                _logger.LogError("CharacterViewer: could not resolve guest NPC " + identity.CacheKey);
                return;
            }

            if (myGen != _guestLoadGeneration) return;

            // Reuse the primary scene's resolution snapshot: the guest is being composited
            // into this viewer's world, so it must see the same scope chain the host scene
            // resolved against or its textures would fall back to the vanilla data folder.
            List<(string BodyPart, AssetSource? MeshSource, List<NifMeshBuilder.BuiltMesh> Meshes)> loadResults;
            using (_assetResolver.PushScopes(_currentSceneScopes, _currentSceneFolders,
                       _currentSceneVanillaLooseOverridesBsa,
                       _currentSceneVanillaLooseOverridesModLoose,
                       _currentSceneAllowLoadOrderFallback))
            {
                loadResults = _renderThread is InlineRenderThreadMarshaller
                    ? LoadAllMeshParts(meshPaths, CancellationToken.None)
                    : await Task.Run(() => LoadAllMeshParts(meshPaths, CancellationToken.None));
            }

            if (myGen != _guestLoadGeneration) return;

            var request = new GuestSceneRequest(
                loadResults, meshPaths, morphs, Math.Clamp(weight, 0, 100), osdFiles, cacheKey);

            _renderThread.Invoke(() =>
            {
                // Re-check inside the marshalled block: a newer load may have won the race
                // between the generation check above and this queue write.
                if (myGen != _guestLoadGeneration) return;
                _guestRequest = request;
                _guestBodyTri = null; // probed lazily at install, against the guest's own body NIF
                _guestInstallPending = true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer.LoadGuestAsync failed for " + identity.CacheKey
                + ": " + ex.ToString());
        }
    }

    /// <summary>
    /// Removes the guest overlay and forgets its retained request. Safe to call when no
    /// overlay is showing. GL deletes go through the render-thread marshaller so they land
    /// in this viewer's context (see Part 5 of RENDERING_PIPELINE.md).
    /// </summary>
    public void ClearGuestScene()
    {
        // Bump the generation so an in-flight LoadGuestAsync can't re-queue the overlay the
        // caller just asked to remove.
        _guestLoadGeneration++;

        _guestRequest = null;
        _guestBodyTri = null;
        _guestInstallPending = false;

        if (_guestMeshes.Count == 0) return;

        var doomed = _guestMeshes.ToList();
        _guestMeshes.Clear();

        _renderThread.Invoke(() =>
        {
            foreach (var mesh in doomed)
            {
                Renderer.RemoveMesh(mesh);
                _textureApplyInfoByMesh.Remove(mesh);
                mesh.Dispose();
            }
        });
    }

    /// <summary>
    /// Drains a pending guest install. Called at the top of <see cref="ProcessPendingScene"/>,
    /// so it runs on the render thread with this viewer's context current.
    ///
    /// <para>Gated on a quiescent primary scene. A primary install begins with
    /// <c>ClearScene</c>, which calls <c>Renderer.ClearMeshes()</c> and therefore disposes the
    /// guest's meshes too — installing a guest while a primary load is in flight would just
    /// build meshes that are about to be destroyed. <see cref="NotifyGuestMeshesDestroyed"/>
    /// re-arms the install so the overlay comes back on its own once the primary scene
    /// commits, which is what makes changing the host pane's own preset non-destructive to
    /// the overlay.</para>
    /// </summary>
    private void ProcessPendingGuestScene()
    {
        if (!_guestInstallPending || _guestRequest == null) return;
        if (_sceneInstall != null || _pendingScene != null || _sceneRebuildPending) return;
        if (TextureManager == null) return;

        _guestInstallPending = false;

        try
        {
            InstallGuestScene(_guestRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: guest overlay install failed: " + ex.ToString());
        }
    }

    /// <summary>
    /// Called from <c>ClearScene</c> when the renderer's mesh list — guest meshes included —
    /// has been torn down. Drops the stale GlMesh references and re-arms the install so the
    /// overlay is rebuilt once the incoming primary scene commits.
    /// </summary>
    private void NotifyGuestMeshesDestroyed()
    {
        if (_guestMeshes.Count > 0)
        {
            // Already disposed by Renderer.ClearMeshes; just drop our references. Their
            // texture-apply entries went with _textureApplyInfoByMesh.Clear() in ClearScene.
            _guestMeshes.Clear();
        }
        if (_guestRequest != null) _guestInstallPending = true;
    }

    /// <summary>
    /// Builds and installs the guest's GL meshes in one tick. Unlike the primary scene this is
    /// not sliced across frames: the guest is an explicit, user-initiated comparison rather
    /// than the default view, so a single hitch on toggling it is preferable to a progressive
    /// reveal that reads as flicker against the already-drawn primary model.
    /// </summary>
    private void InstallGuestScene(GuestSceneRequest request)
    {
        // Replace whatever is currently overlaid, without going through ClearGuestScene —
        // that would drop the retained request we are installing from.
        foreach (var mesh in _guestMeshes)
        {
            Renderer.RemoveMesh(mesh);
            _textureApplyInfoByMesh.Remove(mesh);
            mesh.Dispose();
        }
        _guestMeshes.Clear();

        // The guest resolves textures against the primary scene's scope chain — see the
        // matching PushScopes in LoadGuestAsync. ProcessPendingScene's own bracket is already
        // open around this call, so nothing further is needed here.
        var installOrder = request.LoadResults
            .Where(r => !_guestBodyOnly || r.BodyPart == "Body")
            .OrderBy(r => InstallOrderRank(r.BodyPart));

        // Probe the guest's own sibling .tri before deforming: it is topology-matched to the
        // guest's body NIF, which may be a completely different body mod from the primary's.
        // Falls through to the request's OSD files when absent.
        if (request.Morphs != null)
        {
            TryLoadGuestBodyTri(request);
        }

        int installed = 0;
        foreach (var (bodyPart, meshSource, meshes) in installOrder)
        {
            request.MeshPaths.TxstTextures.TryGetValue(bodyPart, out var txstOverrides);

            foreach (var built in meshes)
            {
                if (IsInvisibleCollisionProxy(built, built.TexturePaths)) continue;

                var glMesh = CreateGlMesh(built);
                glMesh.MeshSource = meshSource;
                glMesh.BodyPart = bodyPart;

                try
                {
                    InstallGuestShapeTextures(request, bodyPart, txstOverrides, built, glMesh);
                }
                catch (Exception)
                {
                    // Mirror InstallOneShape's cancel path: the GL buffers are already
                    // uploaded, so dispose rather than leak them, and drop just this shape.
                    glMesh.Dispose();
                    throw;
                }

                // Deform AFTER texturing so a failure in either leaves no half-installed mesh
                // in the renderer. Body shapes only — the morph deltas are body topology.
                if (request.Morphs != null && bodyPart == "Body")
                {
                    ApplyGuestShapeMorph(request, built, glMesh);
                }

                ApplyGuestStyle(glMesh);

                // Guest meshes take no part in slot occupancy: they are a comparison overlay,
                // not gear worn by the primary NPC, so they must neither hide the primary
                // model's shapes nor be hidden by them.
                glMesh.BipedSlots = 0;
                glMesh.HidesSlots = 0;

                Renderer.AddMesh(glMesh);
                _guestMeshes.Add(glMesh);
                installed++;
            }
        }

        LogVerbose("CharacterViewer: guest overlay installed (" + installed + " shape(s), style="
            + _guestStyle + ", bodyOnly=" + _guestBodyOnly + ", key=" + request.CacheKey + ")");
    }

    /// <summary>Applies the guest's textures using the same routine as the primary scene, so
    /// a Textured-style overlay is materially identical to what the other pane renders.</summary>
    private void InstallGuestShapeTextures(GuestSceneRequest request, string bodyPart,
        Dictionary<int, string>? txstOverrides, NifMeshBuilder.BuiltMesh built, GlMesh glMesh)
    {
        var effectiveTextures = new Dictionary<int, string>(built.TexturePaths);
        // Same shader-type-5 gate as the primary path: ARMA TXST overrides target the body
        // part's skin, so they must not bleed onto non-skin shapes sharing the NIF.
        if (txstOverrides != null && built.ShaderType == 5)
        {
            foreach (var (slot, path) in txstOverrides) effectiveTextures[slot] = path;
        }

        bool isHairTint = false;
        float hairR = 0, hairG = 0, hairB = 0;
        bool isFaceTint = false;
        string? faceTintPath = null;

        ApplyTexturesToGlMesh(glMesh, built, effectiveTextures, request.MeshPaths,
            ref isHairTint, ref hairR, ref hairG, ref hairB,
            ref isFaceTint, ref faceTintPath,
            isWornHairSlotItem: bodyPart == "Hair");

        _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
            new Dictionary<int, string>(effectiveTextures),
            isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);
    }

    /// <summary>Deforms one guest body shape by the guest's own preset at the guest's own
    /// weight and uploads the result. Shares <c>DeformShape</c> with the primary scene, so the
    /// two panes' geometry is produced by identical math.</summary>
    private void ApplyGuestShapeMorph(GuestSceneRequest request, NifMeshBuilder.BuiltMesh built, GlMesh glMesh)
    {
        try
        {
            var (positions, normals) = DeformShape(
                built, built.ShapeName, request.Morphs!, request.Weight,
                _guestBodyTri, request.OsdFiles);

            glMesh.UpdateVertexData(BuildInterleavedVertexData(
                positions, normals, built.TextureCoordinates,
                built.Tangents, built.Bitangents, built.VertexColors));
            glMesh.CpuPositions = positions;
        }
        catch (Exception ex)
        {
            // An undeformed guest is still a useful overlay (it shows the base body), so log
            // and keep the shape rather than dropping it.
            _logger.LogError("CharacterViewer: guest morph failed for shape '"
                + built.ShapeName + "': " + ex.ToString());
        }
    }

    /// <summary>Probes for a .tri sibling of the guest's body NIF, matching the primary
    /// scene's <c>TryLoadSiblingBodyTri</c>. Absent .tri simply leaves the OSD path in play.</summary>
    private void TryLoadGuestBodyTri(GuestSceneRequest request)
    {
        if (_guestBodyTri != null) return;

        string? bodyNifPath = request.LoadResults
            .FirstOrDefault(r => r.BodyPart == "Body")
            .MeshSource?.ResolvedDiskPath;
        if (bodyNifPath == null) return;

        string? triPath = ProbeSiblingTriPath(bodyNifPath);
        if (triPath == null)
        {
            LogVerbose("CharacterViewer: no sibling .tri for guest body '" + bodyNifPath
                + "' — falling back to the OSD path.");
            return;
        }

        _guestBodyTri = _bodyTriFileParser.Parse(triPath);
        if (_guestBodyTri == null)
        {
            LogVerbose("CharacterViewer: guest sibling .tri at '" + triPath
                + "' failed to parse — falling back to the OSD path.");
        }
    }

    /// <summary>
    /// Applies <see cref="GuestStyle"/> to one guest mesh. Expressed purely through existing
    /// per-mesh GlMesh material fields, so no new renderer pass is needed:
    /// <list type="bullet">
    ///   <item><b>Textured</b> — leave the mesh exactly as the texture pass built it.</item>
    ///   <item><b>Translucent</b> — flat tint, sub-1 <c>MaterialAlpha</c>, alpha-blended with
    ///     depth writes off so it sorts into the blend pass and never occludes the primary
    ///     model. Depth-write-off is what keeps two interpenetrating bodies readable.</item>
    ///   <item><b>Wireframe</b> — the renderer's existing wireframe-fallback path, which draws
    ///     edges only and skips the solid passes entirely.</item>
    /// </list>
    /// </summary>
    private void ApplyGuestStyle(GlMesh mesh)
    {
        // Reset the fields this method owns, so switching styles is not order-dependent.
        mesh.RenderAsWireframeFallback = false;
        mesh.WireframeColorOverride = null;
        mesh.MaterialAlpha = 1f;

        switch (_guestStyle)
        {
            case GuestOverlayStyle.Textured:
                // Nothing to do: the mesh keeps whatever blend/alpha state its own
                // NiAlphaProperty asked for, same as a primary-scene shape.
                break;

            case GuestOverlayStyle.Translucent:
                mesh.HasAlphaBlend = true;
                mesh.UseAlphaTest = false;
                mesh.DepthWrite = false;
                mesh.MaterialAlpha = Math.Clamp(GuestAlpha, 0.05f, 1f);
                mesh.HasTintColor = true;
                mesh.TintColor = GuestTintColor;
                // Standard src-alpha / one-minus-src-alpha, matching the blend indices the
                // NIF loader emits for ordinary translucent shapes.
                mesh.SrcBlendIndex = 6;
                mesh.DstBlendIndex = 7;
                break;

            case GuestOverlayStyle.Wireframe:
                // Borrows the missing-texture fallback's DRAW behavior (skipped by every
                // solid pass, edges only) but overrides its color — that green specifically
                // means "diffuse failed to decode" and must not be claimed by the overlay.
                mesh.RenderAsWireframeFallback = true;
                mesh.WireframeColorOverride = GuestTintColor;
                break;
        }
    }
}
