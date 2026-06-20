namespace CharacterViewer.Rendering.Offscreen;

/// <summary>
/// Per-render wall-clock phase breakdown, populated by the offscreen renderer
/// when a host passes a fresh instance via <see cref="OffscreenRenderRequest.TimingsOut"/>.
/// This is pure data, not logging — it carries zero I/O cost, so a host can
/// collect representative timings with verbose per-asset logging OFF (the
/// verbose trace itself perturbs the measurement enough to invalidate it).
///
/// <para>All values are CPU wall-clock milliseconds. Note that GL is
/// asynchronous: <see cref="DrawMs"/> measures only the CPU time issuing the
/// draw calls; the GPU's actual execution is folded into <see cref="ReadbackMs"/>,
/// where glReadPixels blocks until the frame completes. For a coarse build-vs-
/// upload-vs-GPU attribution (which is what render-strategy decisions need) that
/// split is sufficient; true GPU timing would require GL timer queries.</para>
/// </summary>
public sealed class RenderTimings
{
    /// <summary>GL/VM setup before the scene loads — InitializeGl (shader
    /// compile) plus request→VM property forwarding. Per-render today because
    /// the VM is rebuilt each render; a non-trivial value flags shader compile
    /// as a candidate to hoist.</summary>
    public double SetupMs { get; set; }

    /// <summary>LoadAsync: NIF resolve/extract + parse + CPU skinning of every
    /// body part. The unique, uncached head NIF dominates this for high-poly mods.</summary>
    public double BuildMs { get; set; }

    /// <summary>Subset of <see cref="BuildMs"/> spent resolving asset paths (scope
    /// walk + File.Exists + BSA locate). Uncacheable under strict scopes, so prewarm
    /// can't reduce it — a per-render floor.</summary>
    public double ResolveMs { get; set; }

    /// <summary>Subset of <see cref="BuildMs"/> spent in actual NIF parse on the
    /// render thread (cache misses: NifFile.Load + skinning). Trends to ~0 when
    /// prewarm has warmed every part; a high value means the render thread is still
    /// re-parsing (eviction or un-prewarmed meshes). <see cref="BuildMs"/> −
    /// ResolveMs − ParseMs ≈ the clone + weight-morph cost.</summary>
    public double ParseMs { get; set; }

    /// <summary>Native NifFile.Load (file parse) cost for this NPC's cache-missed
    /// parts, summed across the prewarm worker AND any render-thread re-parse. Unlike
    /// <see cref="ParseMs"/> (render-thread only) this captures the offloaded prewarm
    /// parse, so for a fully-prewarmed render it is the real per-NPC parse cost. With
    /// <see cref="BuildShapesMs"/> it splits parse into native-Load vs managed marshaling.</summary>
    public double LoadMs { get; set; }

    /// <summary>BuildAllShapes cost (C#-side per-vertex SWIG marshaling + CPU skinning)
    /// for this NPC's cache-missed parts, summed across prewarm worker and render thread.
    /// The companion to <see cref="LoadMs"/>; a high share here points at the managed
    /// interop / GC cost rather than the native file parse.</summary>
    public double BuildShapesMs { get; set; }

    /// <summary>ProcessPendingSceneToCompletion + mesh/texture/morph overrides:
    /// texture decode-on-miss and the GL upload of textures and vertex buffers.
    /// High value here is what would make pinning shared GL resources pay off.</summary>
    public double InstallMs { get; set; }

    /// <summary>Subset of <see cref="InstallMs"/> spent in actual DDS decode
    /// (cache misses). InstallMs − DecodeMs approximates the GL-upload portion.
    /// Decode is CPU and parallelizable / cacheable; a high decode share argues
    /// for pre-decoding on worker threads, a high upload share for pinning
    /// shared GL textures resident.</summary>
    public double DecodeMs { get; set; }

    /// <summary>CPU time issuing the draw pass (vm.Renderer.Render). Typically
    /// small; see the class remark about GL asynchrony.</summary>
    public double DrawMs { get; set; }

    /// <summary>MSAA resolve blit + glReadPixels (blocks on GPU completion) +
    /// vertical flip + alpha stamp.</summary>
    public double ReadbackMs { get; set; }

    /// <summary>PNG (or BGRA) encode of the read-back pixels.</summary>
    public double EncodeMs { get; set; }

    /// <summary>Sum of all phases — the renderer's own view of total render
    /// cost, comparable to the host's RenderToPngAsync timing.</summary>
    public double TotalMs => SetupMs + BuildMs + InstallMs + DrawMs + ReadbackMs + EncodeMs;
}
