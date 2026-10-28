# SkyRender

OpenGL rendering for Skyrim NIF scenes, in two packages:

- **SkyRender** (`net8.0`): NIF parsing, Bethesda materials and shaders, DDS textures (decoded or
  authored-mip compressed uploads), CPU skinning, the GL renderer with overlays, and an offscreen
  pipeline that renders a scene to PNG or raw BGRA32 pixels. No WPF and no game-library dependency.
- **SkyRender.Character** (`net8.0-windows10.0.19041.0`, WPF): character assembly on top of the
  core: NPC mesh-path inputs, texture and mesh overrides, FaceGen heads, slot occupancy, an NPC
  offscreen scene builder for portraits, and an interactive viewer view model with WPF panels.

The libraries take no dependency on Mutagen.Bethesda or other game-record libraries. A host
implements a few small abstraction interfaces so the renderer can pull the data it needs:

- `IBsaArchiveProvider` — enumerates/extracts files from BSA archives.
- `IDataFolderProvider` — supplies the game Data folder + a cache token.
- `IRenderLogger`, `IRenderSettings` — logging and cache sizing.
- Character tier: `INpcMeshDataSource` (resolves an NPC to its mesh/texture paths) and
  `ICharacterViewerSettings` (extends `IRenderSettings` with the viewer's persisted settings).

Entry points: `OffscreenRendererFactory.Create(IOffscreenSceneBuilder, ...)` for any scene, and
`NpcOffscreenRendererFactory.Create(...)` for NPC portraits described by an `NpcRenderRequest`.
Body shapes can be deformed on the CPU through a host-supplied `IShapeDeformer`.

## Platform

- Windows + an OpenGL 4.3-capable GPU. The core is plain `net8.0`; the character tier needs WPF.
- GLSL shaders ship embedded in the assembly (and are also copied beside it for
  local builds), so the package works as a `PackageReference` with no extra setup.

See `docs/RENDERING_PIPELINE.md` for how NIF data flows through parsing, materials, lighting and
post-processing.

## License

GPL-3.0-only.
