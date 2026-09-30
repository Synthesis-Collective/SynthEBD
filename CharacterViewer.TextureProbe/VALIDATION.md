> Historical evaluation of the initial 1x1-only policy. The follow-up replaces
> its experimental switch with a final-mip dimension parameter. See
> [the current threshold evaluation](MIP_THRESHOLD_VALIDATION.md) for the latest
> implementation, measurements, visual differences and recommendation.

# Compressed DDS validation — 2026-09-29

## Recommendation and scope

Keep the optimization **disabled by default**. `CVR_DDS_UPLOAD=authored-mips`
is an explicit visual-policy choice for scratch/preview use. Authored mipmaps
change sampled values; the user approved this behavior after the real-texture
pilot. Evidence renders requiring the established answer should force `decoded`.
No packages were published and no production installation was changed.

Implementation is on `codex/compressed-dds`, based on SynthEBD
`b1798eb860a81899adda0ebe98a1ae21586cd6bc`. SynthEBD's convention requires the
user to commit staged changes. The original checkout's unrelated changes were
preserved. The originating handoff stays InProgress until user confirmation.

## Build and runtime identity

All runs used Windows, an RTX 5090 with 32607 MiB reported memory, NVIDIA 610.88,
and OpenGL 4.0. Candidate renderer:

`F15C46AFB40A1DE5D46AC2FA62955FD02585F57AB41C169A42803ACBFA921FBE`

Baseline renderer:

`36E1260A1A528C1FCAE0C87D28BBC9A2066A578DC9272BDD369401881DE81AF5`

| Consumer | Validation |
| --- | --- |
| SynthEBD | Release solution build; 46 focused tests; application assembly reference resolves to the candidate in the test process; app output DLL has the same SHA256 |
| NPC Plugin Chooser 2 | Source `113b102bcb4bcbee8178cf277eb9d83b98b7cad7`; Release baseline/candidate builds; existing real-renderer FrontendVmHarness and InternalMugshotGenerator; loaded assembly path/hash in each result |
| houseCARL/viewprobe | Source `fa1deb130b523f3701cbfa116563ee7d81fc9362`; matched scratch builds using CharacterViewerRenderingPath; loaded renderer hash in reports and candidate runtime log |

NPC2 scratch wiring points its project reference at the selected renderer and
its native dependencies at their original read-only source paths. An isolated
editorconfig prevents accidental inheritance from the enclosing SynthEBD tree.
Consumer feature source is unchanged. This validates local source consumption;
the NuGet fallback and live SynthEBD UI were not exercised. Renderer version
constants remain 2.10.0; a future package release must update both version fields
and host package references separately.

## Correctness

The real GL suite passes **251 assertions**. Coverage includes all claimed
formats, legacy/DX10 layouts, 1x1/4x4/7x5 dimensions, every authored mip, sRGB
labels with linear sampling, independent RGBA shader samples, orientation,
cutout/blended alpha, malformed/truncated input, capability refusal, signed
fallback classification, uncompressed fallback, decoded cube faces, face/hair
tint immutability, transparent proxy classification, physical variant isolation,
cache clear/reload, eviction accounting, OOM refusal and two separate contexts.
Channel, alpha and generated-mip mutations are detected. GL confirms compressed
storage and exact block bytes for each uploaded level. OOM injection tests a
queued real refusal before upload; it does not exhaust the GPU or qualify every
driver's possible mid-upload failure.

The early real-texture pilot covered DXT1/3/5 and BC7. Base-level decoder versus
GPU differences were zero or one byte. Generated compressed mips differed by up
to 150/255 and authored mips by up to 172/255 on the pilot normal maps. These
results caused the explicit opt-in decision; they were not used to loosen the
equality comparator. Signed RGTC differs from Pfim's contract and is excluded.

### NPC: Lydia, skin, FaceTint, blended hair and cutout hair

All 12 fresh-process frames are pixel-exact across baseline, candidate authored,
and candidate decoded. Missing mesh/texture lists are empty. This vanilla
character requests 33 ordinary partial-chain textures and one uncompressed
texture, so authored mode falls back throughout. It demonstrates compatibility,
not a compressed character speedup. Cube and CPU prewarm work still occurs.

**The strict repeatability gate is not fully met.** Baseline resident renders
can differ from the fresh frame at three forehead pixels by one channel value.
A second resident output class differs at 16 pixels by one value. The entire
set of three decoded-pixel hashes occurs in both baseline and candidate; the
candidate introduces no new class. Shadow-off and AO-off diagnostic ablations
both retain the three-pixel transition. Their flags were verified in render logs.
The underlying baseline cause is unresolved. No tolerance, mask, alignment or
pixel exclusion was added to make this pass. Original outputs, coordinates and
class membership are retained in `npc-final/comparisons.json` and
`npc-final/pixel-classes.json`.

### Scenes

The retained WindhelmExterior01 fixture is exact with the switch off and on.
Only 3 of its 192 ordinary uploads are eligible; 188 lack complete mip chains
and one is uncompressed. This is a useful fallback control with little benefit.

The modded Hall of Countenance scene loads 586 ordinary textures, of which 15
are eligible. Candidate decoded pixels are exact. Authored mode changes
36,200/409,600 pixels, maximum channel delta 20/255, mean absolute RGBA delta
0.07605/255. Alpha and depth are exact. The visible change is concentrated on
the pelts using eligible authored normal maps. Full images and fixed upper,
center and lower crops were assessed; no changed pixels were excluded. The
center crop has 4,588 changed pixels/max 13; lower crop 19,053/max 19; upper crop
has zero changes. This is an intentional visual difference, not equivalence.

Across the 12 scene processes, there are exactly two image pixel hashes (decoded
and authored) and one depth hash. Thus both routes repeat exactly here, and the
two candidate-off runs equal every baseline. The unchanged `renderequiv.py`
reports frames equal for off, frames different for authored, and depth/mask
evidence is assessed separately. Its overall verdict remains different: assembly
identities and diagnostic console output differ. No parts are waived. A complete
JSON leaf comparison identifies only two assembly hashes and six output paths
as report differences at visibility budget 4; every substantive report value
is equal. Those first four masks are empty and are not evidence of nonempty-mask
correctness. Increasing the coverage budget to 4096 produced 933 unique masks,
including six nonempty masks and 2,247 red (hidden) samples. All 933 masks and
depth dumps are exact across baseline, candidate off, candidate authored and
authored repeat. There are no visible/green predicted-culled samples in this
fixture; it does not qualify arbitrary alpha-bearing evidence. Full report leaf
diffs contain only the two assembly hashes and output paths (1,189 mask path
references because some shapes belong to several planes). Every substantive
report value is equal. Authored repeats have equal frames, reports and masks.
The overall mask-run comparator still records console/census as unprovided;
the bounded timing runs compare those inputs separately and keep their raw
diagnostic differences. No overall comparator PASS is claimed or manufactured.

## Texture benchmark

Five alternating decoded/authored process pairs per workload. "Interior" is
every ordinary texture requested by the modded scene, mapped to retained source
files; all 586 mapped successfully. "Eligible" is all 15 eligible members of
that same set. Input SHA256 values are in `texture-inputs.json`. No lower mips,
resolution, visible content or comparison thresholds were substituted.

Times are medians in milliseconds, with `GL.Finish` before completion. Fresh
means empty application caches in a new process; OS file caches were not flushed.
Warm CPU means retained decoded pixels and cleared GPU textures. Resident means
existing GPU handles. The harness explicitly classifies diffuse-like filenames
through the CPU transparency API; it is not a complete host material pipeline.

| Workload / state | Decoded ms | Authored ms | Interpretation |
| --- | ---: | ---: | --- |
| Interior, fresh | 5563.05 | 5106.37 | 8.2% lower median; individual paired reductions 4.4–14.1%, with cache/order drift |
| Interior, warm CPU | 1812.77 | 1794.00 | 1.0% lower; ranges overlap |
| Interior, resident | 1.97 | 2.04 | No improvement |
| Eligible, fresh | 561.24 | 275.60 | 50.9% lower in this texture harness |
| Eligible, warm CPU | 111.00 | 36.76 | 66.9% lower |
| Eligible, resident | 0.59 | 0.71 | No improvement; tiny absolute difference |

Fresh interior ranges: decoded 5325–6484 ms, authored 5091–5567 ms. Fresh
eligible ranges: decoded 524–601 ms, authored 263–283 ms. Read/parse, decode,
submission, allocations and process memory per run remain in `benchmark.json`;
`benchmark-summary.json` contains stage medians. Pfim read/parse time is included
in its decode time, not independently measured. Submission alone is not GPU
completion. These data do not establish a cold-disk speedup.

| Memory measure | Decoded | Authored | Scope |
| --- | ---: | ---: | --- |
| Interior logical GPU mip storage | 3634.60 MiB | 3462.10 MiB | 4.7% reduction |
| Eligible logical GPU mip storage | 230.00 MiB | 57.50 MiB | 75.0% reduction |
| Interior retained decoded 2D pixels | 2725.95 MiB | 2626.95 MiB | Six fewer actual decodes; 99 MiB saved |
| Eligible retained decoded 2D pixels | 172.50 MiB | 73.50 MiB | Diffuse CPU consumers remain |
| Interior process private bytes, fresh endpoint median | 8300.98 MiB | 8212.63 MiB | Only 1.1% lower; includes runtime/driver/heap slack |
| Eligible process private bytes, fresh endpoint median | 824.37 MiB | 425.01 MiB | Measured endpoint, not a peak or pure texture allocation |

Compressed logical sizes are driver-queried; RGBA sizes are exact mip arithmetic.
These are not physical VRAM residency measurements. No per-process physical VRAM
measurement was collected. Working sets and managed allocations are retained in
raw results. There is no persistent duplicate compressed CPU cache. Temporary
block buffers and existing decoded buffers can coexist. A 1x1 block texture can
cost 8 bytes (BC1/4) or 16 bytes (BC2/3/5/7), versus 4 RGBA bytes; aggregate savings
do not imply savings for tiny textures.

## End-to-end consumer timing

NPC2 uses the real generator, 512x512 Lydia with outfit, identical settings and
verbose render logging. Five alternating baseline/authored process pairs:

| State | Baseline median | Authored median | Ranges (baseline / authored) |
| --- | ---: | ---: | --- |
| Fresh | 4015.94 ms | 4000.20 ms | 3859–4422 / 3877–4054 ms |
| Resident | 1405.96 ms | 1446.11 ms | 1325–1662 / 1293–1644 ms |
| Resident repeat | 1358.69 ms | 1324.00 ms | 1315–1412 / 1306–1384 ms |

The overlapping distributions establish **no NPC loading improvement**. Resident
decode time is zero for both builds. Texture-only savings must not be presented
as these consumer results.

Viewprobe's full modded scene plus research report (640x640, MSAA off, fixed
camera, retained occlusion rule, visibility budget 4) was measured in five
alternating baseline/authored pairs. Median process time is **37.531 s baseline
versus 38.125 s authored**, a 1.6% higher median. Ranges are 36.609–38.812 s and
36.703–40.141 s respectively. This establishes **no end-to-end improvement**;
it is not evidence of a statistically distinct regression either. Two decoded
candidate controls took 36.672 and 36.891 s. The task's GPU jobs ran serially.

## Reproduction and adoption

Local evidence root: `S:/Dev/SynthEBD/dds-evidence`. Source archive commit IDs and
archive hashes: `source-identities.json`. Build argument vectors and logs:
`final-build-jobs.json`, `npc2-build-jobs-04.json`, `synthebd-build.log`,
`host-tests/final.trx`. Probe: `verify-05/result.json` and `verify-05.log`.

Run a retained job plan with `python run_jobs.py <plan.json>` from the evidence
directory. Use a new output root for scene reports: viewprobe refuses to replace
an existing report. Relevant plans are `texture-bench-jobs.json`,
`npc-final-jobs.json`, `qualified-scene-jobs.json` and `mask-coverage-jobs.json`.
`summarize_bench.py`, `summarize_scene.py`, `compare_npc.py`, `compare_images.py`
and `compare_reports.py` reproduce derived results. Consumer snapshots use the
same source and original asset inputs for both renderer builds. Installed game,
retained mod inputs, .NET SDK/NuGet dependencies and a real GL context are needed.

For local adoption, build a consumer against this source and verify its loaded
renderer hash, then opt in before cache creation. Keep exact evidence jobs on
`decoded`. Rollback is unset/decoded plus a fresh cache/context. No package,
installation or global environment variable was changed. Package fallback,
other GPU vendors, signed compressed uploads, partial chains, and general
authored-alpha evidence equivalence remain unqualified.

The host's existing on-disk mugshot cache does not include this environment
variable in its identity. This evaluation calls GenerateAsync directly into
new output folders. Broader host adoption must force regeneration or include
the mip policy in image-cache identity; changing the variable alone does not
qualify already cached thumbnails.
