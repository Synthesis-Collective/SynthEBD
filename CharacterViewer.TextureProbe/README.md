# Compressed DDS verification

This Windows console probe creates a hidden real OpenGL 4.0 context. It is a
developer verification tool, not an installed consumer or part of the solution.
Run from the repository root:

```powershell
dotnet build CharacterViewer.TextureProbe/CharacterViewer.TextureProbe.csproj -c Release
dotnet run --project CharacterViewer.TextureProbe -c Release --no-build -- verify C:/Temp/dds-verify
dotnet test SynthEBD.Tests/SynthEBD.Tests.csproj -c Release --filter 'FullyQualifiedName~CompressedDdsTests|FullyQualifiedName~TextureVisibilityLockTests|FullyQualifiedName~GlRendererDisposalSymmetryTests'
```

The fixtures specify independent RGBA expectations, including distinct authored
mips, block orientation, BC1 cutout alpha, legacy/DX10 formats, sRGB labels,
BC4 grayscale, BC5 channels, and BC7 mode 6. The sampler uses a shader and an
RGBA8 framebuffer. Deliberate channel, alpha, and mip mutations must be detected.
Signed RGTC and unsupported layouts must fall back. CPU tinting, transparent
proxy detection, physical asset variants, cube faces, ownership, context teardown
and error diagnostics are exercised too. Partial rectangular chains test exact
threshold boundaries, resident isolation across thresholds, and shader sampling
beyond the supplied levels (which must clamp to the final mip).

The OOM check reuses the retained viewprobe GraphicsReuseRed method: request an
impossible 1 TiB buffer, confirm GL refuses it, and verify that the queued refusal
cannot become a successful cached upload. It does not fill available VRAM. The
eviction check charges large logical sizes to tiny actual handles; it tests cache
accounting only. Neither test is a VRAM benchmark. Drivers that cannot supply the
expected refusal fail this check explicitly.

## Measurement

Create a JSON array of absolute DDS paths. Then run each mode in separate,
alternating processes, at least five pairs:

```powershell
dotnet run --project CharacterViewer.TextureProbe -c Release --no-build -- bench C:/Temp/dds-decoded textures.json decoded
dotnet run --project CharacterViewer.TextureProbe -c Release --no-build -- bench C:/Temp/dds-authored textures.json unlimited
```

Each process measures three phases: empty CPU/GPU caches; retained decoded CPU
cache with GPU textures cleared; resident GPU reuse. Files whose stems do not
end in `_n` or `_s` receive an explicit CPU transparency check. This is a declared
texture-harness workload, not a reconstruction of every host's material slots.
Host end-to-end measurements must be reported separately. OS file caches are
not flushed. The cache can exceed its pinned budget for textures protected by
the current render epoch, as the real renderer can.

Results include submission and `GL.Finish` completion time, actual decode calls
and bytes, retained 2D decoded bytes, managed allocations, process private bytes
and working set. Logical compressed storage is checked against each GL mip's
reported block size; RGBA storage is calculated from the exact mip dimensions.
Logical bytes are not a driver allocation/VRAM residency measurement. Process
memory is sampled at phase boundaries, not at its peak. No persistent compressed
CPU cache is introduced.

Eligible compressed reads separate file open/read and header validation/allocation.
Pfim's existing decode timing includes I/O and format parsing. Those components
remain inseparable for decoded loads. Warm cache lookup time is not decode time.

Every result records the loaded renderer path and SHA256. Failed probes print an
exception and exit 1. Do not accept an output folder left by a failed attempt.

## Retained local evaluation

The benchmark's final argument accepts `decoded`, a positive final-mip dimension,
or `unlimited`. `inventory OUTPUT TEXTURE_LIST_JSON` records validated formats,
terminal dimensions, payload sizes and input hashes without a GL context.
For actual consumers, set `CVR_DDS_MAX_FINAL_MIP_DIMENSION` to the same values.
Runtime diagnostics print the selected limit alongside the loaded library hash.
The default is decoded; `unlimited` changes minification even for single-level DDS.

See [the threshold evaluation](MIP_THRESHOLD_VALIDATION.md) and the historical
[full-chain evaluation](VALIDATION.md). The machine-specific raw evidence and job
plans are retained at `S:/Dev/SynthEBD/dds-evidence`. They use houseCARL's existing
`scripts/runlog.py` Runner and unchanged `renderequiv.py` comparator. Source
archives, input hashes, all failed attempts, and original images are retained.
The repository does not include game assets, copied host dependencies, or packages.
