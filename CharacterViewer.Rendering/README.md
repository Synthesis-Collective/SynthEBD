# CharacterViewer.Rendering

An offscreen OpenGL 3D renderer for Skyrim NPC head/body previews ("mugshots"),
extracted from [SynthEBD](https://github.com/Synthesis-Collective/SynthEBD). It
loads NIF meshes, applies textures/tints/BodySlide morphs, and renders a framed
PNG (or raw BGRA32 pixels) entirely offscreen.

It is **host-agnostic**: the library takes no dependency on Mutagen.Bethesda or
any game-specific types. Consumers implement a few small abstraction interfaces
so the renderer can pull the data it needs:

- `IOffscreenRenderer` (created via `OffscreenRendererFactory`) — the entry point.
- `INpcMeshDataSource` — resolves an NPC to its mesh/texture paths.
- `IBsaArchiveProvider` — enumerates/extracts files from BSA archives.
- `IDataFolderProvider` — supplies the game Data folder + a cache token.
- `ICharacterViewerLogger`, `ICharacterViewerSettings` — logging and settings.

Requests are described with Mutagen-free POCOs (`OffscreenRenderRequest`,
`ResolvedNpcMeshPaths`, `TextureOverride`, `MeshOverride`, `MorphSet`, …).

## Platform

- `net8.0-windows` (WPF). Windows + an OpenGL-capable GPU are required.
- GLSL shaders ship embedded in the assembly (and are also copied beside it for
  local builds), so the package works as a `PackageReference` with no extra setup.

## License

GPL-3.0-only. See the SynthEBD repository for the full license text.
