using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Generates temp FaceGen NIFs for the Headparts preview flow. Wraps
/// <see cref="FaceGenPatcher.PatchFaceGenNif"/> with a redirected output
/// root so preview artifacts never touch the real output Data folder.
///
/// Output files land under %TEMP%/SynthEBD_HeadPartPreview/ and are
/// deleted on application shutdown (see <see cref="Dispose"/>).
/// </summary>
public class FaceGenPreviewService : IDisposable
{
    private readonly FaceGenPatcher _faceGenPatcher;
    private readonly NPCInfo.Factory _npcInfoFactory;
    private readonly PatcherState _patcherState;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;

    private readonly string _previewRoot;
    private readonly object _bsaLoadLock = new();
    private bool _bsaIndexLoaded;

    public FaceGenPreviewService(
        FaceGenPatcher faceGenPatcher,
        NPCInfo.Factory npcInfoFactory,
        PatcherState patcherState,
        IEnvironmentStateProvider environmentProvider,
        Logger logger)
    {
        _faceGenPatcher = faceGenPatcher;
        _npcInfoFactory = npcInfoFactory;
        _patcherState = patcherState;
        _environmentProvider = environmentProvider;
        _logger = logger;

        _previewRoot = Path.Combine(Path.GetTempPath(), "SynthEBD_HeadPartPreview");
    }

    /// <summary>
    /// Generates a preview FaceGen NIF for <paramref name="previewNpc"/> with the
    /// specified head-part type swapped to <paramref name="headPartOverride"/>.
    /// Returns the absolute path to the generated NIF, or null on failure / cancellation.
    /// </summary>
    public async Task<string?> GeneratePreviewFaceGenAsync(
        FormKey previewNpc,
        HeadPart.TypeEnum type,
        FormKey headPartOverride,
        CancellationToken ct)
    {
        if (headPartOverride.IsNull) return null;
        var assignments = new Dictionary<HeadPart.TypeEnum, FormKey> { { type, headPartOverride } };
        return await GeneratePreviewFaceGenAsync(previewNpc, assignments, ct);
    }

    /// <summary>
    /// Multi-type overload. Generates a preview FaceGen NIF for <paramref name="previewNpc"/>
    /// with any number of head-part types overridden. Passes the assignments dictionary
    /// directly to <see cref="FaceGenPatcher.PatchFaceGenNif"/>, which already supports
    /// swapping multiple head parts in one pass.
    /// </summary>
    public async Task<string?> GeneratePreviewFaceGenAsync(
        FormKey previewNpc,
        Dictionary<HeadPart.TypeEnum, FormKey> assignments,
        CancellationToken ct)
    {
        if (previewNpc.IsNull || assignments == null || assignments.Count == 0)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_previewRoot);
        }
        catch (Exception ex)
        {
            _logger.LogError("FaceGenPreviewService: failed to create preview root '" + _previewRoot + "': " + ex.Message);
            return null;
        }

        EnsureBsaIndexLoaded();

        if (!_environmentProvider.LinkCache.TryResolve<INpcGetter>(previewNpc, out var npcRecord))
        {
            _logger.LogMessage("FaceGenPreviewService: preview NPC " + previewNpc + " not present in load order — cannot render preview.");
            return null;
        }

        ct.ThrowIfCancellationRequested();

        var linkedGroupsHashSet = _patcherState.GeneralSettings.LinkedNPCGroups.ToHashSet();
        var createdLinkGroupInfos = new HashSet<LinkedNPCGroupInfo>();
        var npcInfo = _npcInfoFactory(npcRecord, linkedGroupsHashSet, createdLinkGroupInfos);

        var emptyAssetContainers = new List<Patcher.SelectedAssetContainer>();

        try
        {
            bool ok = await Task.Run(() => _faceGenPatcher.PatchFaceGenNif(
                npcInfo,
                emptyAssetContainers,
                assignments,
                outputFormKey: null,
                previewOutputRoot: _previewRoot), ct);

            if (!ok)
            {
                _logger.LogMessage("FaceGenPreviewService: PatchFaceGenNif reported skipped for " + npcInfo.LogIDstring);
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("FaceGenPreviewService: PatchFaceGenNif threw for " + npcInfo.LogIDstring + ": " + ex.Message);
            return null;
        }

        string outputPath = FaceGenPatcher.ResolveFaceGenNifPathForFormKey(previewNpc, _previewRoot);
        if (!File.Exists(outputPath))
        {
            // PatchFaceGenNif returns true when there's no work to do without writing a NIF.
            // For preview, that means the swap was either validated away or skipped.
            _logger.LogMessage("FaceGenPreviewService: expected preview NIF not written at " + outputPath);
            return null;
        }

        return outputPath;
    }

    private void EnsureBsaIndexLoaded()
    {
        if (_bsaIndexLoaded) return;
        lock (_bsaLoadLock)
        {
            if (_bsaIndexLoaded) return;
            try
            {
                _faceGenPatcher.LoadBsaIndexCache();
                _bsaIndexLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError("FaceGenPreviewService: LoadBsaIndexCache failed: " + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_previewRoot))
            {
                Directory.Delete(_previewRoot, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("FaceGenPreviewService: preview cleanup failed: " + ex.Message);
        }
    }
}
