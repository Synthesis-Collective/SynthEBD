# DDS final-mip threshold evaluation

Follow-up to the initial full-chain-only evaluation. Implementation is in the
primary `S:/Dev/SynthEBD` checkout so it appears in GitKraken. Other concurrent
SynthEBD work is outside this change. No packages or installations were replaced.

## Policy

`CharacterPreviewCache.DdsMaximumFinalMipDimension` is a nullable positive integer.
Null preserves decoded RGBA with generated mips. A value bounds the larger
dimension of the last supplied DDS mip. `1` requires 1x1, `4` permits up to 4x4,
and `int.MaxValue` permits every otherwise-supported chain, including one level.
Environment equivalent: `CVR_DDS_MAX_FINAL_MIP_DIMENSION=decoded|INTEGER|unlimited`.
The former experimental boolean-style mode is removed. This parameter controls
eligibility, not visual error. Sampling clamps to the last supplied level.

The parser and prewarm inspection share the threshold. Resident keys include it
and the physical asset path. CPU tint and transparency operations still decode;
format, color-space, error and context ownership rules are unchanged.

### Actual coverage

The retained 586-texture interior manifest contains 159 BC1, 421 BC3 and six BC7
textures, all supported on the test GPU. Validated input hashes are retained.

| Maximum final dimension | Eligible textures |
| --- | ---: |
| 1 | 15 |
| 2 | 482 |
| 4 | 579 |
| 8 | 583 |
| 16 | 585 |
| unlimited | 586 |

467 chains end at 2x2 and 97 at 4x4. The remaining seven beyond `4` are two
rectangular candy textures ending at 16x2, four bark textures ending at 2x8,
and a single-level 512x256 salmon normal map. Thus the initial lack of broad
benefit was heavily influenced by our imposed 1x1 eligibility requirement.

## Texture and NPC measurements

RTX 5090, NVIDIA 610.88, real OpenGL 4.0. Five alternating process pairs per
workload. Medians below; OS file caches and unrelated user activity were not
controlled. No owned builds ran during accepted timing pairs.

| Measure | Decoded | Unlimited authored |
| --- | ---: | ---: |
| 586 textures, fresh completed load | 5,462.57 ms | 2,810.71 ms |
| Same, warm CPU cache | 1,792.08 ms | 591.75 ms |
| Same, resident GPU reuse | 1.97 ms | 1.27 ms |
| Logical GPU texture storage | 3,634.60 MiB | 782.03 MiB |
| Cached decoded CPU pixels | 2,725.95 MiB | 1,576.46 MiB |
| Fresh CPU decode calls | 586 | 302 |
| Fresh process private bytes, endpoint | 8,291.36 MiB | 3,646.49 MiB |
| Fresh working set, endpoint | 4,590.58 MiB | 2,935.64 MiB |
| Fresh cumulative managed allocation | 6,473.09 MiB | 4,521.31 MiB |
| Warm cumulative managed allocation | 1.00 MiB | 785.57 MiB |
| NPC2 fresh GenerateAsync | 3,741.47 ms | 3,779.83 ms |
| NPC2 resident GenerateAsync | 1,292.76 ms | 1,338.29 ms |
| NPC2 resident repeat GenerateAsync | 1,299.32 ms | 1,346.56 ms |
| NPC2 fresh private bytes, endpoint | 1,334.94 MiB | 901.77 MiB |
| NPC2 fresh CPU decode time | 243.65 ms | 101.51 ms |
| viewprobe whole scene/report process | 34.391 s | 30.344 s |

The isolated texture load is 49% faster fresh and 67% faster with warm CPU
caches. Resident reuse saves less than one millisecond, not a meaningful consumer
loading improvement. Warm authored loads allocate new block buffers because
there is no persistent compressed CPU cache; this is a real allocation cost.

Fresh texture ranges are 5,350–7,703 ms decoded and 2,715–3,768 ms authored.
Warm ranges are 1,768–1,879 ms and 586–708 ms. All five accepted fresh pairs
favor authored uploads; the slow replacement pair is retained in the statistics.
The texture harness deliberately classifies filenames without `_n`/`_s` as
diffuse. This exercises real CPU alpha decoding but is not a reconstruction of
every host slot. Actual hosts are measured separately.

NPC fresh ranges are 3,612–3,853 ms decoded and 3,661–4,864 ms authored.
There is **no measured NPC end-to-end speedup**. Its ordinary uploads change
from 34 decoded textures to 33 compressed plus one uncompressed fallback.
Their logical storage falls from 205.52 to 44.73 MiB. CPU decode savings do not
dominate the rest of this generator workload.

The recovered viewprobe set shows an **11.8% lower median whole-process time**.
All five paired reductions favor authored uploads (8.5–14.6%). Ranges are
33.703–35.000 s decoded and 29.844–30.828 s authored. Both arms use the same
short C: extraction root and C: evidence output, original camera/assets,
640x640/MSAA-off settings and visibility budget 4. The separate full-mask
qualification uses budget 4096 and is excluded from this timing table. The
earlier S: scene runs are not mixed into these measurements. No owned builds
or diagnostic renders overlapped the accepted recovered pairs.

Memory endpoints are actual process observations, not peaks. Logical GPU bytes
are verified block storage, not physical driver allocation or residency. No
per-process physical VRAM measurement is claimed. The cache may exceed its
nominal budget while all current-pass textures are protected from eviction.

## Visual findings and fixed criteria

The original acceptance criteria remain unchanged: exact decoded control,
independent exact format fixtures, all differing pixels reported, fixed full /
upper / center / lower regions, no tolerance or alignment adjustments.

At the retained College camera and Lydia portrait, `4` and `unlimited` produce
exactly equal color images. This is evidence for those captures, not a general
guarantee: the extra seven textures can matter elsewhere or at stronger
minification. `4` uses 793.67 MiB of logical scene texture storage versus 782.03
MiB unrestricted, only another 11.65 MiB saved by removing the limit.

Compared with decoded rendering:

| Capture | Changed pixels | Maximum channel delta | Mean absolute RGBA delta |
| --- | ---: | ---: | ---: |
| Lydia, 512x512 | 133,928 / 262,144 | 108 / 255 | 0.39939 / 255 |
| College interior, 640x640 | 388,530 / 409,600 | 70 / 255 | 1.46942 / 255 |

Full originals, fixed-crop metrics, difference images and enlarged maximum-delta
neighborhoods are retained. The portrait's maximum occurs at a thin hair strand
over clothing. The interior differs in surface detail and alpha-cutout edges.
Output alpha bytes match, but viewprobe forces an opaque final PNG, so that
alone cannot establish alpha-test equivalence. Six scene depth pixels change.

Two scratch-only ablations isolate the depth cause. Keeping `Pelt01/02/03.dds`
on the decoded route removes five changed depth pixels along the floor-pelt
edge. Keeping every texture with non-opaque decoded alpha on that route removes
all six, including the remaining image pixel (187,362). The production candidate
was not changed by these ablations. The basic/depth shaders use texture alpha
for discard and do not write a texture-dependent depth value. This supports
authored alpha sampling as the cause, not changed geometry or context ownership.
Exact alpha-cutout evidence equivalence is therefore not established.

At the full 4096 visibility budget, all 933 mask images and all culling/census
values match between decoded and unrestricted rendering. The unchanged
`renderequiv.py` comparator still reports different frames/reports/console,
with no waivers. The complete report leaf comparison contains only output
artifact paths and the changed scene-depth hash; no culling result changed.
Mask equality is limited to the retained rigid-opaque evidence scope and camera.
All four mask sets (original baseline, current decoded, unrestricted and repeat)
are byte-identical: six nonempty masks contain 2,247 hidden samples. Across the
ten recovered timing captures there is exactly one color and depth class per
policy. The decoded control matches the old baseline; unrestricted has the same
six explained depth changes on every run. All input configuration fingerprints
match. Full images, all masks and report leaves were retained and checked.

All 15 unrestricted NPC captures are pixel-identical. The baseline retains its
three previously observed pixel classes: fresh-to-resident three-pixel one-byte
changes and a 16-pixel one-byte resident variation. Its strict repeatability
gate remains failed; no threshold was relaxed. Candidate decoded fresh controls
are exact to the baseline in both hosts.

## Verification and identity

271 real GPU assertions pass, including full/partial/single-level mip sampling,
rectangular threshold boundaries, explicit LOD clamping and policy-separated
resident reuse. Existing channel, alpha, sRGB-label, tint, transparency, asset
variant, cubemap, unsupported-format, corruption, OOM and context checks pass.
62 focused SynthEBD tests pass. Concurrent unrelated host edits temporarily
failed compilation, so the host was validated from commit
`b1798eb860a81899adda0ebe98a1ae21586cd6bc`, with both host and test project references
pointing at the primary candidate renderer. No concurrent work was changed.

Candidate SHA256 loaded by the probe, SynthEBD host-reference test, NPC2 and viewprobe:

`74C563E6EF594C200A61792339C7F9F98A6C3A2DCFD12391247C121E8C788BFE`

Baseline renderer:
`36E1260A1A528C1FCAE0C87D28BBC9A2066A578DC9272BDD369401881DE81AF5`.
NPC2 and viewprobe use the retained matched host source snapshots from the first
evaluation. Actual runtime paths/hashes are in each result or diagnostic log.

## Evidence and reproduction

Primary plans, input inventory, original images and microbenchmark/NPC results:
`S:/Dev/SynthEBD/dds-evidence/mip-threshold` (ignored by Git).
`recovery-root.json` identifies the C: location for subsequent raw evidence.
Run `run_jobs_safe.py PLAN.json` with `DDS_EVIDENCE_ROOT` set to that location;
the exact argument vectors and environment are saved in each plan. It uses
houseCARL's existing Runner, records exit status immediately, checks free space
and stops on failure. Failed outputs never qualify as measurements.

The first scene batch exhausted S: during evidence writes: five jobs failed,
including unchanged baseline. The original runner continued without surfacing
each failure immediately. These runs are retained and excluded. Temporary
extraction and subsequent evidence moved to C:. A first overly long C: extraction
path hit the documented native mesh-reader path limit; its exit-3 output is
also rejected. The temporary extraction root is now a short task-specific
directory under the Windows temp folder. Controlled failure and space-refusal
tests verify that the corrected runner stops before its next job. Its process
error mode suppresses Windows crash dialogs without suppressing logged failures.

The first texture pair 4 overlapped an owned test build for two seconds; both
members were replaced under `bench-replacement`, and the original is preserved.
All remaining limitations from the first evaluation still apply: one GPU/driver,
local source references only, no package qualification or production rollout,
and no host persistent-thumbnail cache policy integration.

## Recommendation

Keep decoded rendering as the default and for exact evidence work. For previews
where authored mip behavior is accepted, **start with a maximum final dimension
of 4**. It admits 579/586 textures here, has the same captured pixels as
unrestricted mode in both fixtures, and avoids admitting large single-level
images. Unlimited saves only another 11.65 MiB in this scene while allowing more
severe minification aliasing on other views. A threshold is not a quality bound.

The memory reduction is useful. A loading-speed claim must remain workload
specific: the isolated texture path improves substantially, while the actual
NPC generator does not. Neither result alone justifies enabling it everywhere.
Consumer integration should put this policy into any persistent thumbnail cache
identity before allowing policy changes without regeneration. That adoption and
package publication remain outside this implementation.

## Delivery and resource record

Piranha accepted the implementation and authorized the source commit in the
primary SynthEBD checkout. Commit subject:
`Add DDS uploads with configurable final mip limits`.
The handoff is complete with the limitations above retained. No deployment.

OpenAI weekly checkpoint: 55% used, versus 50% at this follow-up's start and 39%
at the logical-session baseline. The rounded shared-account follow-up delta is
5 percentage points against a 4–8 point forecast; cumulative delta is 16 points,
below the standing 20-point cap. No Anthropic usage or cross-provider conversion.
