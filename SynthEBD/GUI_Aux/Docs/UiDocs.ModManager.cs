namespace SynthEBD;

// 3-part documentation for the Mod Manager Integration menu (shared settings plus the MO2 and Vortex panels).
public static partial class UiDocs
{
    private static void RegisterModManager()
    {
        Add("ModManager.Type",
            layperson: "Tells SynthEBD which mod manager you use. When you install config files, their textures and meshes are then sent to the right place: a Mod Organizer 2 mod folder, the Vortex staging folder, or the game's Data folder if you use neither.",
            technical: "Sets Settings_ModManager.ModManagerType (None / ModOrganizer2 / Vortex). It switches the integration panel shown below, resolves CurrentInstallationFolder (game Data folder, MO2 mod folder, or Vortex staging folder) as the config installer's destination root, and selects which FilePath Length Limit applies.",
            motivation: "Config files carry large asset payloads that should live inside the mod manager like any other mod rather than as loose files, so the installer must know which manager owns your setup.");

        Add("ModManager.TempFolder",
            layperson: "The scratch folder where downloaded archives are unpacked while installing config files. Keep it on a short, simple path; the default is a Temp folder next to SynthEBD.exe.",
            technical: "Mirror of Settings_ModManager.TempExtractionFolder. ConfigInstaller extracts config and dependency archives into a timestamped subfolder here using the bundled 7-Zip before moving referenced files to the installation folder, and the FaceGen patcher stages temporary BSA extractions here. The UI warns when the chosen path exceeds 100 characters.",
            motivation: "Archive extraction temporarily stacks the archive's folder tree on top of this path, so a deeply buried temp folder triggers path-too-long failures during installation; surfacing the location lets users keep it shallow.");

        Add("ModManager.FilePathLimit",
            layperson: "The longest file path allowed when installing a config file's assets. Files that would exceed it are automatically renamed to shorter paths (and the config is updated to match), so nothing breaks - this just sets the threshold.",
            technical: "Settings_ModManager.FilePathLimit (no mod manager, default 260) or the MO2/Vortex-specific value (default 220). ConfigInstaller.HandleLongFilePaths computes the longest fully resolved destination path per asset pack; if it exceeds the limit, directory and file names are remapped to shorter ones and the asset pack's internal paths are rewritten to match, aborting only if paths remain too long afterward.",
            motivation: "Windows traditionally fails past 260 characters and mod managers consume extra headroom of their own (hence their stricter 220 default); without automatic shortening, texture-heavy configs with deep folder trees would fail to install.");

        Add("ModManager.MO2ExecutablePath",
            layperson: "The full path to your ModOrganizer.exe. Setting it lets SynthEBD find your MO2 mods folder automatically.",
            technical: "Sets VM_MO2Integration.ExecutablePath (persisted as Settings_ModManager.MO2.ExecutablePath). On change, UpdateModFolderPath reads the mod_directory entry from the ModOrganizer.ini beside the executable to auto-fill the Mod Folder Path, falling back to the default 'mods' subfolder.",
            motivation: "Users reliably know where MO2 itself lives, while the mods folder may have been relocated in MO2's settings; deriving it from MO2's own ini avoids pointing the installer at the wrong directory.");

        Add("ModManager.MO2ModFolder",
            layperson: "MO2's mods folder, where every installed mod has its own subfolder. Config file assets are installed here as a new mod that appears in MO2's left pane.",
            technical: "Sets VM_MO2Integration.ModFolderPath (Settings_ModManager.MO2.ModFolderPath). While the mod manager type is ModOrganizer2 it becomes CurrentInstallationFolder, the root under which the config installer creates the destination mod folder. Auto-derived from ModOrganizer.ini when the executable path is set.",
            motivation: "Installing assets as a proper MO2 mod keeps them manageable - enable, disable, or delete them like any other mod - instead of contaminating the real Data folder.");

        Add("ModManager.VortexStagingFolder",
            layperson: "Vortex's mod staging folder (shown in Vortex under Settings on the Mods tab). Config file assets are installed here so Vortex can deploy them like a normal mod.",
            technical: "Sets VM_VortexIntergation.StagingFolderPath (Settings_ModManager.Vortex.StagingFolderPath). While the mod manager type is Vortex it becomes CurrentInstallationFolder, the config installer's destination root.",
            motivation: "Vortex deploys mods from its staging area into the game folder; installing there keeps SynthEBD's assets under Vortex's management instead of bypassing it with loose files.");
    }
}
