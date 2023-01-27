<img src="https://user-images.githubusercontent.com/63175798/169685064-9f7dd9e1-0e94-4772-aea3-7a97f9737c97.png" width="500" />

### NPC Appearance Customization and Randomization

SynthEBD is a standalone patcher that acts as a central hub for controlled randomization of certain aspects of NPC appearance: <u>Assets</u>, <u>Body Shape</u>, and <u>Height</u>. It is the Mutagen-based successor to the zEBD zEdit patcher. Assets are any files that are referenced within an NPC record or subrecord in a .esp file - most commonly <u>Textures</u> and sometimes <u>Meshes</u>. Body shape refers body mesh variations - e.g. <u>BodySlides</u> or <u>BodyGen morph</u>s. Height refers to the overall <u>size</u> scale of an NPC. SynthEBD enables you to both randomize and specifically control these parameters for any NPC in the game.

Read this readme and still need help? Join the Mutagen Discord channel at https://discord.gg/53KMEsW and post in the #spawn-support channel (ping @Piranha91 if I don't see you).

### Key Features

- ##### Texture Assignment
  
  SynthEBD allows you to install plugins that distribute NPC textures from some of the popular mods on the Nexus - Bijin Skins, Tempered Skins, etc. Unlike installation through your mod manager, which forces you to select one variant from the many created by the author, SynthEBD enables you to distribute any and all texture variants to NPCs throughout the game with both randomization and control.
  
- ##### Body Shape Randomization
  
  SynthEBD supports randomization of NPC body shapes using mesh deformations. You can select if you want to achieve this by distributing bodyslides through OBody/AutoBody AE, or directly through RaceMenu's BodyGen using collections of jBS2BG-converted bodyslides. Body shapes can be annotated to ensure that they are realistically paired with textures to produce the most visually consistent NPC experience.
  
- ##### Height Randomization
  
  SynthEBD can randomize NPC height (the uniform mesh scaling of an NPC) between defined ranges, and also provides a convenient interface to edit base racial heights.
  
- ##### Head Part Randomization

  SynthEBD can distribute head parts (hair, scars, etc) to NPC according to your chosen distribution rules.
  
- ##### Distribution Rules
  
  SynthEBD provides controls to fine-tune the distribution of assets, body shapes, and head parts. Common points of control include aspects such as NPC race, class, faction, and other "main" characteristics, but *any data within an NPC record or subrecord* can be used as a check to guide distribution.
  
- ##### Manual Assignment
  
  SynthEBD provides a <u>Specific NPC Assignments</u> menu so that if you want to assign any texture, body shape, or height to an NPC, you can do so from a centralized location.
  
- ##### Consistency
  
  NPC assignments are saved between patcher runs, so if you like the choices the patcher made you can keep them while selectively randomizing anything you want to re-shuffle.
  

### Acknowledgements

This belongs near the top, as I could never have built SynthEBD on my own. I am grateful to the following people for helping me drive this project to completion:

- MongoMonk (Nexus) for building Everybody's Different Redone. SynthEBD is at its core a wrapper to feed data into EBD, without which the main functionality of the patcher would not be possible.
  
- Noggog (Mutagen Discord) for providing an absurd amount of timely support for all of my Mutagen and C# questions, and for an incredible amount of patience in dealing with my barrage of questions over several months.
  
- Ashen and Nem (SkyrimMods Discord) for helping me learn Papyrus and JContainers to get OBody/Autobody integration into SynthEBD.

- Cecell (Nexus, Ethereal Tools Discord) for providing cLib for Papyrus, which allowed me to work around an apparent JContainers bug and allow the headparts feature to work in VR in addition to SE/AE.
  
- Mator (Modding Tools Discord) for also providing an absurd amount of support when I was building the original zEBD patcher on which SynthEBD is based.

- Focustense and Erri120 (Mutagen Discord) for additional help with C# and general coding.
  
- BigBlueWolf, TikiPrime, DisappointingHero, and Aria_Leonsbane (Nexus) for taking the time to write plugins for zEBD. There's no better endorsement than seeing someone take your mod and expand it.
  
- Timboman (Wabbajack Discord) for incorporating zEBD into the Ultimate VR Essentials Wabbajack List.

- HitBug83 (GitHub), Daikichi-Takahey (GitHub), and leonardovcr (GitHub) for rigorous issue reporting / quality testing.
  
- Everyone who has downloaded and/or endorsed zEBD and encouraged me to work on this Mutagen port. Without your push I would not have had the motivation to get this out.
  

### Requirements

The different SynthEBD modules have their own requirements, in addition to the core requirement of [.NET Framework 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0):

###### Asset Distribution:

- Everybody's Different Redone SSE
- JContainers

###### Body Shape Distribution:

- *Through BodyGen*:
  
  - RaceMenu SSE
- *Through OBody*:
  
  - RaceMenu SSE
    
  - OBody
    
  - Spell Perk Item Distributor
    
  - JContainers
    
- *Through AutoBody AE*:
  
  - RaceMenu SSE
    
  - AutoBody AE
    
  - Spell Perk Item Distributor
    
  - JContainers

###### Height Distribution:

- No dependencies

###### Head Part Distribution:

- Everybody's Different Redone SSE
- JContainers  
- Spell Perk Item Distributor

### Getting Started (For New Users)

1. Download SynthEBD from the Nexus or GitHub Releases page
  
2. Extract the SynthEBD folder to wherever you keep your mod utilities
  
3. Download any SynthEBD Asset Config Files you would like to use (e.g. Tempered Skins, Bjin, etc.)
  
4. Launch SynthEBD.exe (through your mod manager if you are using one).

5. Set the Output Data Folder (where SynthEBD-generated files will be written. As an MO2 user I prefer C:\MyMO2Path\mods\SynthEBD Output).

6. Select the distribution mode you'd like to use for body shapes (none, BodyGen, or OBody/AutoBody)

7. (if using a mod manger) Go to the *Mod Manager Integration Menu* and set up your mod manager paths.
  
8. In the *Textures and Meshes* tab, click *Install Config From Archive*. Select one of the config files that you downloaded in step 3, and follow the on-screen instructions to complete the installation.
  
9. Repeat step 5 for all of the config file(s) you downloaded
  
10. If you are using a mod manager, close SynthEBD and relaunch it (so that the virtual file system knows about the newly installed files). If you've configured your mod manager integration, you will also need to refresh your mod manager and activate the newly installed mods that will appear at the end of your mod list.

11. If you are distributing body shapes via OBody/AutoBody, go to the *O/AutoBody Settings* and make sure all of your custom BodySlides are annotated with the appropriate descriptors.
  
12. Click the green *Run* button.
  

### Important Differences from zEBD (For Previous Users)

Despite the new paint job, SynthEBD functions very similarly to zEBD so if you're familiar with the latter, you shouldn't have a problem jumping right in. That said, there are a few important additions and changes you should be aware of.

**Mutagen Back End**: No more 255 plugin limit, same as Synthesis, and significant speed benefits over zEdit.

**UI Upgrade**: I have tried to make the patcher UI more comprehensible where possible. Most notably, the structure of config file subgroups is now represented as a Tree View instead of a nested series of checkboxes, which should go a long way toward clarifying how the patcher works for new users.

**Patch-Don't-Replace**: In zEBD, if a config file edits a given record such as a Worn Armor, that record is completely replaced by one copied from a Record Template. SynthEBD handles this more delicately by copying from the template only if an NPC doesn't have the corresponding record. If an NPC already has a referenced record, it will be modified to point to the asset paths specified by the assigned asset combination but will otherwise remain unchanged, thereby ensuring compatibility with other mods such as those distributing hairs as wig armor addons. For those such as myself who were relying on the convenience of not having to use "BodySlide for X NPCs" mods, an explicit option now exists to "Force vanilla body mesh paths".

**O/AutoBody Integration**: Body shapes can now be distributed directly as BodySlides, without having to convert into BodyGen morphs via jBS2BG. This is achieved by feeding bodyslide assignments into either OBody or AutoBody AE (the latter of which is VR-compatible thanks to alandtse). While support for zEBD-style BodyGen configs is retained, I recommend migrating to BodySlides going forward as they are more readily digestible by end users, and can also be reassigned on the fly using the in-game O/AutoBody menus.

**Improved Attribute System**

- <u>Hardcoded Attributes</u>: For faster lookup and easier editing, some common types of allowed/disallowed attributes are now hardcoded and have their own assignment UI. The original "choose any aspect of an NPC" is retained and improved using the "Custom" type Attribute.
  
- <u>And/Or System</u>: Unlike in zEBD, which only allowed "this attribute OR that attribute", SynthEBD allows attributes to specify "this AND that". Both OR and AND operations are supported. OR is still by far the most common use case, but AND is available should you need it.
  
- <u>Attribute Groups</u>: One of the new design philosophies of SynthEBD is to make it easier for end users to control assignments by implementing Attribute Groups. Attribute Groups are simply groups of attributes which can be controlled from the General Settings menu. For example, in zEBD if a config plugin file had a muscular texture variant, it would typically be accompanied by a long list of Allowed Attributes (e.g. NPC is soldier / barbarian/ blacksmith / etc). In SynthEBD, that texture variant is simply given the "Must be Muscular" Force If attribute. This way, no matter how many config plugin files are installed, the user can control distribution of all muscular normal maps by going to the General Settings menu and editing which attributes belong to "Must Be Muscular". All other (e.g. non-group) attributes are of course allowed, and in some cases I make use of them in my own "official" config plugin files, but<u> in general I request that users who make their own config files try to adhere to this design philosphy and implement distribution via Attribute Groups</u> where it makes sense to do so.
  
- <u>Record Intellisense</u>: One of the problems with zEBD attributes was that you had to look at the record structure in SSEedit and try to recreate the subpath relative to the NPC, with no indication if the attribute wasn't matched because the NPC didn't satisfy it or because the subpath was typed incorrectly. SynthEBD Custom Attributes (as well as several other places in the patcher) implement an autocomplete system for record subpaths - by clicking on the dropdown arrow you can see a list of valid subpaths from wherever you are within the path.
  

**Whole-Config Distribution Rules**: In addition to Subgroups, entire Config Files now have their own distribution rules (dominant over the distribution rules contained within their Subgroups).

**Record Templates as Plugins**: Record Templates are now kept as normal SSE plugins instead of being read in from json files. This makes them significantly easier to modify, as you can simply pop them into SSEedit, make changes, and put them back into the Record Templates folder.

**Asset Destinations as Paths**: In zEBD, the destination for an asset such as a texture was specified simply by the file name (e.g. "malebody_0.dds"). In SynthEBD the destinations are specified by their direct record subpaths. While this may look more intimidating, it allows the patcher to function better and faster. If you are making your own config file, please take a look at the ones I uploaded see what the subpaths should look like. You can also use the aforementioned Record Intellisense to help figure out what the subpaths should be for new asset types/destinations that I don't cover in my own config files.

**Asset Replacers**: Config files can specify assets that should only be assigned if an NPC already has the corresponding assets. This is used by my "official" config files to distribute retextured facial scars to NPCs who have the vanilla textured scars. This generally works well for head parts. *Note that this does not work for face tints (e.g. makeup), which are stored in Race records and baked into NPC facegen, both of which make it challenging to distribute/replace them among NPCs*. I am exploring the concept of a separate face tint distribution system, which may come in a later update, but for the time being this is not possible, and it will always be somewhat tenuous due to the aforementioned issue of it being baked into facegen. In the meantime, for tint distribution I refer you to the wonderful [Distributed BodyPaints and Overlays](https://www.nexusmods.com/skyrimspecialedition/mods/55386) mod by Ryugenesis.

**Config Auto Installer**: For config plugin files packaged with a SynthEBD Manifest, such as the ones I upload, installation can be semi-automated using the "Install Config from Archive" button to ensure that users download the correct file(s) and that both the config file and all necessary asset files end up in their correct destinations, thereby countering some of the confusion derived from the nontraditional installation instructions that z/SynthEBD require. For config files shipped without a Manifest, a simple "Install Config JSON File" option remains available.

**Mod Manager Integration**: To support the config auto installer, SynthEBD comes with a simple mod manager integration menu to make sure that installed asset files are routed to the correct destination.

**Auto Upgrade**: All important zEBD-formatted json files (Asset config files, BodyGen config files, Block List) can be imported into SynthEBD using the corresponding menu buttons. However, do note that attribute groups are not auto-populated so please replace "loose" attributes with attribute groups manually where possible. Also note that only "official" Attributes for which options appear in the UI (e.g. Class, Faction, VoiceType, etc) will be automatically converted; any custom attributes will need to be re-implemented manually. The patcher will warn of any attributes it doesn't recognize when the config file is first loaded.

## License

SynthEBD bundles 7-zip's 7z.exe. In compliance with my [understanding of its license](https://sourceforge.net/p/sevenzip/discussion/45797/thread/d4ab546a/), I document that:
(1) I used parts of the 7-Zip program 
(2) 7-Zip is licensed under the GNU LGPL license 
(3) You can find the source code at [www.7-zip.org](www.7-zip.org).

## Detailed Documentation

#####

### General Settings

General Settings controls global patcher functionality.

**Output Name**: Name of the generated .esp file.

**Output Data Folder**: Path of the folder to which any files generated by SynthEBD (including the output .esp file) will be written. I recommend you set this to a folder in your mod manager (for example, mine is C:\Games\MO2\mods\SynthEBD Output). Click the green *Select* button to navigate to your desired folder via the File Explorer.

**Show ToolTips**: If checked, hovering the cursor over a menu item will show a tooltip describing what the menu item does.

**Apply Textures and Meshes**: If checked, the patcher will assign assets (e.g. Textures) to NPCs from any installed asset config files.

**Apply Body Shape Using**:

- None: SynthEBD will not control body shape.
  
- BodyGen: SynthEBD will distribute BodyGen morphs from any installed and selected SynthEBD BodyGen Config.
  
- BodySlide: SynthEBD will distribute installed BodySlides using one of the available BodySlide distribution mods:
  
  - OBody: SynthEBD will distribute BodySlides via OBody (OBody Standalone is currently not supported).
    
  - AutoBody AE: SynthEBD will distribute BodysSlides via AutoBody AE.
    

**Apply Height Changes**: If checked, SynthEBD will randomize NPC heights and distribute racial base heights in accordance with the installed and selected SynthEBD Height Config.

**Enable Consistency**: If checked, SynthEBD will remember patcher assignments (assets, body shape, and/or height) and re-assign the same ones during subsequent patcher runs as long as they remain compatible with the current assignment rules.

**Exclude Player Character**: If checked, SynthEBD won't patch the player actor. Recommended to leave on.

**Exclude Presets**: If checked, SynthEBD won't patch player appearance presets. Recommended to leave on.

**Load Settings from Game Data Folder**: If checked, instead of loading settings from the SynthEBD data folder, the patcher will instead try to load settings from Data\SynthEBD (or the equivalent virtualized directory if using a mod manager). Useful for having different SynthEBD settings for different mod manager profiles; e.g. one profile for CBBE and another for UNP. If the box is checked when no settings exist in Data\SynthEBD, they will be created there when the patcher exits. As an example, this is how this feature would be set up in Mod Organizer 2:
![2022-02-13-17-01-41-image](https://user-images.githubusercontent.com/63175798/154865440-fc5ec16b-ad5b-4ecf-8f2b-264f9b12d252.png)


**Link NPCs With Same Name**: If checked, the patcher assigns the same output to NPCs that it thinks are the same character (e.g. NPCs that are unique and have the same Name, where that name is not found in the *Linked NPC Name Exclusions* list). This is to make sure that when mods add another copy of a vanilla NPC, they get the same output.

**Linked NPC Name Exclusions**: NPC names that should not be linked even if the NPC bearing the name is flagged unique.

**Patchable Races**: List of Races that the patcher will try to patch. Any NPC that needs to be patched must have its race (or its *Race Alias*) included in this list.

**Race Aliases**: Race aliases make the patcher treat Race A as Race B. Useful for custom race followers; if you want MyFollowerRace to receive Nord textures, set the *Race* to MyFollowerRace and *AliasRace* to NordRace, and check "Apply to Textures and Meshes". If you want that follower to get a Body Shape meant for Nords, check "Apply to Body Shape". If you want that follower to get a height within the range for Nords, check "Apply to Height"

**Race Groupings**: Race Groupings are simply groups of races that can be referenced from distributed Assets and Body Shapes for convenience. You can define your own Race Groupings in the General Settings menu and reference them from elsewhere in the patcher.

**Attribute Groups**: This is the main menu for definining Attribute Groups. Asset Config Files, BodyGen Config Files, and BodySlide Settings Files can (and should) reference Attribute Groups to control which NPCs get which assets and body shapes. Attributes are the core controller of SynthEBD's distribution system - for more information, see **Attribtue assignment System** below.

- **Superced Plugin Group Definitions with Main**: All Asset Config Files, BodyGen Config files, and BodySlide Settings Files must come with their own Attribute Group definitions (to fall back on in case a specific user doesn't have the referenced Attribute Group in their General Settings). If this box is checked, any Attribute Groups defined in your General Settings menu will <u>override</u> the fallback Attribute Groups defined in the config file. This enables you to control distribution from multiple different config files from this single centralized menu.

**Verbose Mode for Conflict NPCs**: If checked, SynthEBD will write a detailed operation log to describe its decision making process for all NPCs that have an assignment conflict. Operation logs are written to a SynthEBD\Logs\TimeStamp folder. Assignment conflicts include:

- No mutually compatible assets & body shape could be found
  
- The consistency assets or body shape are no longer valid or no longer exist.
  
- The Specific NPC Assignment for the NPC no longer exists.
  

**Verbose Mode for All NPCs**: If checked, SynthEBD will write a detailed operation log for every NPC that it patches. Not recommended as this will consume a large amount of drive space.

**Verbose Mode for Specific NPCs**: If checked, SynthEBD will write a detailed operation log for every NPC selected in the NPC picker menu.

**Verbose Mode Detailed Attributes**: If checked, the detailed operation logs toggled in the settings above will show Names (if available) or EditorIDs for NPC Attributes, rather that their FormKeys. This makes the log more easily interpretable if you're trying to figure out why an NPC matched or didn't match an attribute, but significantly slows down patching speed, so it's recommended to enable this only for troubleshooting.

### Textures and Meshes

The Textures and Meshes menu controls all asset-related distribution options (most commonly Texture distribution).

**Allow config files to change NPC textures**: If checked, SynthEBD will assign textures (e.g. DDS files) specified in installed Asset Config Files.

**Allow config files to change NPC meshes**: If checked, SynthEBD will assign meshes (e.g. NIF files) specified in installed Asset Config Files.

**Patch NPCs with custom bodies**: If checked, SynthEBD will make changes to NPC body textures even if they already have a custom body (Worn Armor) record.

**Patch NPCs with custom faces**: If checked, SynthEBD will make changes to NPC face textures even if they already have a custom (non-vanilla) face texture.

**Force Vanilla Body Mesh Paths**: If checked, SynthEBD will set all WNAM body mesh paths to the vanilla path so that generated body slides apply to even those NPCs that have custom WNAM records. Note that this will affect all NPCs with custom WNAM records even if *Patch NPCs with custom bodies* (or *General Settings | Apply Textures and Meshes*) are unchecked. Note: the purpose of this is to make sure that all NPCs can receive a Body Shape, even if they come from a mod that defines a custom body mesh path, without needing to download "BodySlides for X NPCs" mods.

**Generate combination assignment log**: If checked, the patcher will generate a log (in SynthEBD\Logs) detailing which asset combinations were generated, which records were produced from those combinations, and which NPCs those combinations were assigned to.

**Asset Path Trimming**: Bethesda plugin records are formatted such that a top-level folder is implied based on what the path describes. For example, if a particular texture is found in Data\Textures\Some\Path\SomeTexture.dds, it will be referenced in a plugin simply as "Some\Path\SomeTexture.dds". The path trimming menu simply defines which root folders must be trimmed from the asset paths defined in Asset Config Files. Default behavior is to trim "Textures" from .dds paths and "Meshes" from .nif paths. Additional trimming for other file types can be defined as needed.

**Validate Configs**: Check all installed Asset Config Files for errors that would prevent successful patcher execution (mis-formatted config files) or that would cause in-game misbehavior (e.g. config files that reference assets that haven't been installed).

**Config File List**

All installed Asset Config Files will appear under the *Validate Configs* button. The Config Customization Menu is described at the end of this section. The checkbox to the left of the config name controls whether the assets defined in the config file will be distributed.

**Create New Config File**: Create a completely blank config file. For creating new config plugins for asset mods for which no config file already exists.

**Install Config File From Archive**: Opens the config installer window to guide you through installing a config file that's packaged with a SynthEBD Manifest File. Select the entire zipped file; do not extract it. <u>This is the recommended way to install SynthEBD Asset Config Files</u>.

**Install Config JSON File**: If no SynthEBD Config Archive is available for the asset mod you want to install, or if you want to upgrade a zEBD-formatted config file, you can directly install the .json file using this button. Note that if your config file relies on a BodyGen config, that config must be installed through the *BodyGen Integration* Menu <u>before</u> installing the Asset Config File.

##### Config Customization Menu

The Config Customization Menu defines both the assets the config file distributes, and the distribution rules for those assets.

**Name**: The name of the Asset Config File. Should be roughly similar to the mod whose assets it's distributing.

**Prefix**: A short name or acronym for this Asset Config File. Used by the Asset installer and *Direct Replacer* assignments. This must be the same as the second subfolder of the asset paths described by the config file (e.g. in the Tempered Skins for Males config files, the textures all have the path "textures\TSM\some\texture\path.dds", so the prefix is "TSM").

**Gender**: The NPC gender for which the assets described in the given config file are intended.

**Type**:

- Primary: This Config File describes "main" assets (such as body textures or meshes). Only one Primary config file will be assigned to an NPC.
  
- MixIn: Mix-In config files describe "auxiliary" assets. They are expected not to overlap with Primary config files - if they do, the asset defined by the MixIn will supercede the one described by the Primary. These are intended for small additional assets that are not included in Primary type config files. The patcher will attempt to assign assets from <u>all</u> mix-in config files to each NPC.

**Subgroups**: This is a TreeView defining the asset structure defined in the config file. **<u>NPCs will receive one subgroup from each of the top-level subgroups in the TreeView</u>**. In the example, below, each NPC will receive one Head Diffuse texture from the available options, one Head Normals texture from those available, etc. Click the green + sign to add a child subgroup, or the red x to delete the current subgroup.

![2022-02-13-17-59-21-image](https://user-images.githubusercontent.com/63175798/154865426-a920c2cd-2a6e-414a-9a18-ba19dfc908a5.png)

Clicking on a *Subgroup* will open the *Subgroup Customization Menu* (defined at the end of this section).

**Associated BodyGen Configuration**: If Body Shape is distributed from RaceMenu BodyGen, then the selected BodyGen Configuration's Morph Descriptors will be displayed in the *Allowed/Disallowed BodyGen Descriptors* Menu, and BodyGen Morphs from this BodyGen Configuration file will be distributed to the given NPC.

**Distribution Rules**: Defines the rules governing if an NPC may / may not / must receive assets from the config file. These rules are a subset of those found in the *Subgroup Customization Menu* and are therefore described in that section.

**Direct Replacers**: Defines asset replacers - these are assets that should only be assigned if its *Destination* path exists within a given NPC. For example, a replacer for a given type of scar will only be assigned to NPCs that have that type of scar already. Selecting an asset replacer will open the **Subgroup Customization Menu* (defined at the end of this section).

**Record Templates**: This menu describes which template NPCs will be referenced if the NPC being patched doesn't have a record at the expected path. SynthEBD ships with a template plugin stored in the Record Templates folder. Additional record templates may be defined simply by adding new plugins containing NPC records to this folder.

- **Default Template**: This is the record template that will be assigned to all NPCs besides those whose races are defined in the ***Additional Templates*** section below.
  
- **Additional Races Paths**: Paths at which the patcher will insert the list of patchable races. The default usage for this is the "Additional Races" property of Armature records, which must include the "owner" NPC's race or the armature will be invisible.
  
- **Additional Templates**: Additional record templates that will be assigned to NPCs of specific races (e.g. if those races have additional parts that "main" NPCs don't have, such as tails for Khajiit and Argonians).
  

**Attribute Groups**: This menu defines the *Attribute Groups* that are accessible to this config file. Note that these groups get synchronized to those in the General Settings menu upon SynthEBD startup (synchronized meaning that any Attribute Groups that are present in the General Settings menu get imported into the Config's Attribute Groups menu). If changes are made to the General Settings' Attribtue Group menu, they can be immediately imported by clicking the ***Import From General Settings*** button.

**Validate**: Check this config file for errors that would prevent successful patcher execution (mis-formatted config files) or that would cause in-game misbehavior (e.g. config files that reference assets that haven't been installed).

**Save**: Saves changes made to the current config file to the hard drive (note: changes are also saved when patcher is closed).

**Discard Changes**: Reload the most recent saved version of this config file from the hard drive.

##### Subgroup Customization Menu

Clicking on a subgroup pulls up its *Subgroup Customization Menu*. Here you can set the rules that determine which NPC can/cannot/must get a given subgroup and, by association, the assets associated with it. The options are as follows:

**ID**: The internal designation for the given subgroup. Must be unique for each subgroup within the config file.

****Name****: A short name that describes the assets associated with the subgroup. For display only.

**Distribute to non-forced NPCs**: If checked, the given subgroup is available to distribute randomly (that is, not just via *Specific NPC Assignments* or *ForceIf Attributes*).

**Allow Unique NPCs**: If checked, the given subgroup can be distributed randomly to NPCs flagged as Unique.

**Allow Non-Unique NPCs**: If checked, the given subgroup can be distributed randomly to NPCs not flagged as Unique.

**Distribution Probability Weighting**: Probability for an NPC to randomly receive the given subgroup (relative to other subgroups at this position).

**Allowed Races**: Races that can be assigned the given subgroup. If no Allowed Races (and no *Allwoed Race Groupings* are selected, NPCs of all races can receive the given subgroup).

**Allowed Race Groupings**: Race Groupings that can be assigned the given subgroup.

**Disallowed Races**: Races that cannot be assigned the given subgroup. Dominant over *Allowed Races* and *Allowed Race Groupings*.

**Disallowed Race Groupings**: Race Groupings that cannot be assigned the given subgroup. Dominant over *Allowed Races* and *Allowed Race Groupings*.

**Allowed NPC Attributes**: If any are present, the given subgroup can only be assigned to NPCs that match the given attributes. For more information, see the *Attribute Assignment System* below.

- **ForceIf**: If checked, an NPC <u>must</u> be assigned the given subgroup if it matches the give attribute (as long as another subgroup at the same position doesn't match more ForceIf attributes)
  
- **Weight**: The "score" assigned to each matched *Force If* Attribute for the given subgroup.
  

**Disallowed NPC Attributes**: If any are present, the given subgroup cannot be assigned to NPCs that match the given attributes. Dominant over *Allowed NPC Attributes*. For more information, see the *Attribute Assignment System* below.

**Allowed NPC Weight Range**: The minimum and maximum weight an NPC may have to receive the given subgroup.

**Required Subgroups**: If the given subgroup is assigned, these other subgroups from the same config file must also be assigned. Populate this list by dragging and dropping from the Subgroup TreeView.

**Excluded Subgroups**: If the given subgroup is assigned, these other subgroups from the same config file may not be assigned. Populate this list by dragging and dropping from the Subgroup TreeView.

**Add Keyword to NPC**: If the given subgroup is assigned to an NPC, the NPC will also receive the Keyword record(s) defined in this list. There are no uses for this feature at the time of writing, but it can be used to interface with other mods.

**Asset Paths**: Specifies the asset(s) that are controlled by the given config file.

- **Source File**: The file path of the given asset (relative to the Game Data folder). If the asset file exists, the border of the text box will be green. Otherwise it will be red. Missing asset files will result in undesired behavior (such as NPCs appearing blue in the case of textures).
  
- **Destination**: The subpath of where the given asset is supposed to be assigned relative to the NPC record. Supported by Record Intellisense; click the arrow button to show suggestions for valid paths. If the subpath exists, the border of the text box will be green. Otherwise it will be red. Paths that do not exist cannot be assigned.
  

**Allowed BodyGen Descriptors**: Displayed only if the Body Shape is set to be applied using BodyGen. If any descriptors are selected, then only BodyGen morphs annotated with the given descriptors (for a given category) can be assigned to NPCs to which the given subgroup is assigned. These descriptors are sourced from the *Associated BodyGen Configuration*'s Body Shape Descriptor menu.

**Disallowed BodyGen Descriptors**: Displayed only if the Body Shape is set to be applied using BodyGen. If any descriptors are selected, then BodyGen morphs annotated with the given descriptors (for a given category) may not be assigned to NPCs to which the given subgroup is assigned. These descriptors are sourced from the *Associated BodyGen Configuration*'s Body Shape Descriptor menu.

**Allowed BodySlide Descriptors**: Displayed only if the Body Shape is set to be applied using BodyGen. If any descriptors are selected, then only BodyGen morphs annotated with the given descriptors (for a given category) can be assigned to NPCs to which the given subgroup is assigned. These descriptors are sourced from the *(O/Auto)Body Integration* Menu's Body Shape Descriptor menu.

**Disallowed BodySlide Descriptors**: Displayed only if the Body Shape is set to be applied using BodyGen. If any descriptors are selected, then BodyGen morphs annotated with the given descriptors (for a given category) may not be assigned to NPCs to which the given subgroup is assigned. These descriptors are sourced from the *(O/Auto)Body Integration* Menu's Body Shape Descriptor menu.

### BodyGen Integration

This menu allows you to customize the distribution rules for BodyGen morphs defined by the installed BodyGen Config(s). Select the BodyGen Config to edit from the Female or Male dropdown list, and toggle the **Displayed** radio button between the two to select which one to display and edit.

**Config Name**: The name of the given BodyGen Config file.

**Config Gender**: The gender to which morphs from this BodyGen Config should be assigned.

**Morph List**: The list of BodyGen Morphs defined in the given BodyGen Config file and available for distribution to NPCs. Selecting one of these morphs pulls up the **BodyGen Morph Customization Menu** (see below).

**Morph Group Map**: List of Races and the BodyGen *Morph Groups* available to that race. Multiple *Morph Group*s can be assigned to a given race. For example, NordRace NPCs could get *Top* AND *Bottom* groups OR the *Whole Body* group. In this case, when assigning a BodyGen morph, the patcher will randomly decide which set of groups to assign from.

**Morph Descriptors**: Pulls up the ***Body Shape Descriptor Menu*** (see below) for the given BodyGen Config. Here you can edit the Body Shape Descriptors available to Asset Config Files that reference this BodyGen Config File.

**Morph Groups**: Pulls up the Morph Group Menu, which simply defines the Morph Groups available to the ***Morph Group Map***.

**Attribute Groups**: This menu defines the *Attribute Groups* that are accessible to this BodyGen config file. Note that these groups get synchronized to those in the General Settings menu upon SynthEBD startup (synchronized meaning that any Attribute Groups that are present in the General Settings menu get imported into the Config's Attribute Groups menu). If changes are made to the General Settings' Attribtue Group menu, they can be immediately imported by clicking the ***Import From General Settings*** button.

**Misc**: Miscellaneous BodyGen-related functions:

- **Set RaceMenu INI To Enable BodyGen**: Edits the RaceMenu .ini file to ensure BodyGen morphs can be correctly applied.

##### BodyGen Morph Customization Menu

This menu controls the given BodyGen Morph and dictates which NPCs it can/cannot be assigned to.

****Name****: The name of the given BodyGen morph

**Notes**: Longer description of what the morph looks like (optional; purely for user benefit).

****Morph****: The jBS2BG-converted string represenation of the given BodyGen Morph

**Belongs To Groups**: *Morph Groups* of which the given BodyGen Morph is a member.

**Descriptors**: BodyShape Descriptors (sourced from this BodyGen Config's *Morph Descriptors* Menu) that describe the given BodyGen Morph.

**Allowed Races**: Races that can be assigned the given Morph. If no Allowed Races (and no *Allwoed Race Groupings* are selected, NPCs of all races can receive the given Morph).

**Allowed Race Groupings**: Race Groupings that can be assigned the given Morph.

**Disallowed Races**: Races that cannot be assigned the given Morph. Dominant over *Allowed Races* and *Allowed Race Groupings*.

**Disallowed Race Groupings**: Race Groupings that cannot be assigned the given Morph. Dominant over *Allowed Races* and *Allowed Race Groupings*.

**Allowed NPC Attributes**: If any are present, the given Morph can only be assigned to NPCs that match the given attributes. For more information, see the *Attribute Assignment System* below.

- **ForceIf**: If checked, an NPC <u>must</u> be assigned the given Morph if it matches the give attribute (as long as another subgroup at the same position doesn't match more ForceIf attributes)
  
- **Weight**: The "score" assigned to each matched *Force If* Attribute for the given Morph.
  

**Disallowed NPC Attributes**: If any are present, the given Morph cannot be assigned to NPCs that match the given attributes. Dominant over *Allowed NPC Attributes*. For more information, see the *Attribute Assignment System* below.

**Allowed NPC Weight Range**: The minimum and maximum weight an NPC may have to receive the given Morph.

**Distribute to non-forced NPCs**: If checked, the given Morph is available to distribute randomly (that is, not just via *Specific NPC Assignments* or *ForceIf Attributes*).

**Allow Unique NPCs**: If checked, the given Morph can be distributed randomly to NPCs flagged as Unique.

**Allow Non-Unique NPCs**: If checked, the given Morph can be distributed randomly to NPCs not flagged as Unique.

**Distribution Probability Weighting**: Probability for an NPC to randomly receive the given Morph (relative to other subgroups within this *Morph Group*).

**Required Templates**: If the given Morph is assigned, these other Morphs must also be assigned along with it (this can be useful to bypass the character limit of the morphs.ini BodyGen file - if your jBS2BG-converted morph exceeds the character limit, you can glue them together using this option).

### (O/Auto)Body Integration

This menu controls the distribution of installed BodySlide presets.

**BodySlides**: Pulls up the list of currently or previously installed BodySlide presets. Bodyslides that are currently present in the Game Data folder and annotated with Body Shape descriptors are shown with a green border. BodySlides that are currently present but are not annotated are shown with a yellow border. BodySlides that are no longer present in the Data folder are shown with a red border and <u>will not be distributed when the patcher is run</u>. BodySlides that are set to hidden are shown with a grey border if the **Show Hidden** checkbox is selected. Selecting a BodySlide pulls up the ***BodySlide Customization Menu*** (see below). <u>Only the default BodySlides distributed with popular body mods are pre-annotated - YOU are responsible for annotating additional BodySlides that you install</u>.

**Body Descriptors**: Pulls up the ***Body Shape Descriptor Menu*** (see below) for BodySlides. Here you can edit the Body Shape Descriptors that are available to annotate BodySlide presets.

**Attribute Groups**: This menu defines the *Attribute Groups* that are accessible to the *BodySlide Customization Menu*. Note that these groups get synchronized to those in the General Settings menu upon SynthEBD startup (synchronized meaning that any Attribute Groups that are present in the General Settings menu get imported into the Config's Attribute Groups menu). If changes are made to the General Settings' Attribtue Group menu, they can be immediately imported by clicking the ***Import From General Settings*** button.

**Misc Settings**: Miscellaneous BodySlide-related functions:

- **Set RaceMenu INI To Enable OBody/AutoBody**: Edits the RaceMenu .ini file to ensure BodySlides can be correctly applied.
  
- **Male BodySlide Groups**: BodySlides belonging to the listed Groups (defined in the BodySlide's xml file) will be imported into the Male BodySlide list.
  
- **Female BodySlide Groups**: BodySlides belonging to the listed Groups (defined in the BodySlide's xml file) will be imported into the Female BodySlide list.
  
- **Use Verbose Scripts**: If selected, a version of the BodySlide distribution script will be installed that displays notifications in the top-left corner in-game when a BodySlide is assigned to an NPC. This is intended as a diagnostic only to verify that SynthEBD BodySlide distribution is working in-game.
  

##### BodySlide Customization Menu

This menu controls the given BodySlide Template and dictates which NPCs it can/cannot be assigned to.

****Name****: The name of the given BodySlide template

**Notes**: Longer description of what the BodySlide looks like (optional; purely for user benefit).

**Descriptors**: BodyShape Descriptors (sourced from the (O/Auto)Body Integration *Body Descriptors* Menu) that describe the given BodySlide.

**Allowed Races**: Races that can be assigned to the given BodySlide. If no Allowed Races (and no *Allwoed Race Groupings* are selected, NPCs of all races can receive the given BodySlide).

**Allowed Race Groupings**: Race Groupings that can be assigned the given BodySlide.

**Disallowed Races**: Races that cannot be assigned the given BodySlide. Dominant over *Allowed Races* and *Allowed Race Groupings*.

**Disallowed Race Groupings**: Race Groupings that cannot be assigned the given BodySlide. Dominant over *Allowed Races* and *Allowed Race Groupings*.

**Allowed NPC Attributes**: If any are present, the given BodySlide can only be assigned to NPCs that match the given attributes. For more information, see the *Attribute Assignment System* below.

- **ForceIf**: If checked, an NPC <u>must</u> be assigned the given BodySlide if it matches the give attribute (as long as another subgroup at the same position doesn't match more ForceIf attributes)
  
- **Weight**: The "score" assigned to each matched *Force If* Attribute for the given BodySlide.
  

**Disallowed NPC Attributes**: If any are present, the given BodySlide cannot be assigned to NPCs that match the given attributes. Dominant over *Allowed NPC Attributes*. For more information, see the *Attribute Assignment System* below.

**Allowed NPC Weight Range**: The minimum and maximum weight an NPC may have to receive the given BodySlide.

**Distribute to non-forced NPCs**: If checked, the given BodySlide is available to distribute randomly (that is, not just via *Specific NPC Assignments* or *ForceIf Attributes*).

**Allow Unique NPCs**: If checked, the given BodySlide can be distributed randomly to NPCs flagged as Unique.

**Allow Non-Unique NPCs**: If checked, the given BodySlide can be distributed randomly to NPCs not flagged as Unique.

**Distribution Probability Weighting**: Probability for an NPC to randomly receive the given BodySlide (relative to other BodySlides).

**Hide Preset**: If checked, the given BodySlide preset will be hidden from the BodySlide list unless the ***Show Hidden*** checkbox is checked.

**Clone Preset**: Creates a duplicate entry of the current preset which manages the same core BodySlide preset. The idea for this is if you have a BodySlide preset with very different characteristics depending ont the NPC's weight, you can clone the preset and set the Weight Range and Descriptors separately for each clone to make sure the bodyslide at any given NPC weight is annotated correctly.

**Delete Preset**: Delets the current preset from your list of preset settings. Note that this doesn't delete the actual BodySlide, and if you relaunch SynthEBD without moving that preset from your mod list it'll get re-imported (sans any custom distribution rules you might have assigned). To make SynthEBD permanently ignore a preset, uncheck "Distribute to Non-forced NPCs" and check "Hide Preset".

### Height Assignment

This menu enables customization of NPC and racial heights.

**Change Individual NPC Heights**: Allows the patcher to randomize the height of each NPC according to the parameters defined in the selected ***Height Configuration***.

**Change Base Race Heights**: Allows the patcher to set the base Racial Heights for each Race defined in the selected ***Height Configuration***.

**Overwrite Non-Default NPC Heights**: If checked, the patcher may randomize the heights of NPCs who already have a non-default (e.g. height is not 1) heights.

**Create New Height Configuration**: Create a ***Height Configuration*** file from scratch.

***Delete Current Configuration***: Deletes the entire current ***Height Configuration*** file.

**Configuration Name**: Name of the currently selected ***Height Configuration*** file.

***Add Height Group***: Adds a new ***Height Group*** to the curently selected ***Height Configuration*** file.

***Set All Distribution Modes To***: Sets the distribution mode for each ***Height Group*** within the current ***Height Configuration File*** to:

- **Uniform**: Each height within the specified height range has the same probability of being assigned.
  
- **Bell Curve**: Heights are distributed along a bell curve (Mean = 1; +/- = 3 sigma (capped at the range boundaries)).
  

**Height Group Menu**:

- **Height Group**: The name of the given ***Height Group*** (for display only).
  
- **Races**: The Races to which the given ***Height Group*** should be applied.
  
- **Distribution Mode**: *Uniform* or *Bell Curve* (see above for details).
  
- **Base Male Height**: The racial height assigned to males of the selected *Races*. Total height = Base Height x NPC height.
  
- **Male Height +/-**: The range within which male heights for the given ***Height Group*** can be randomized (e.g. an NPC with a range of 0.02 can be assigned a height between 0.98 and 1.02).
  
- **Base Female Height**: The racial height assigned to females of the selected *Races*. Total height = Base Height x NPC height.
  
- **Female Height +/-**: The range within which female heights for the given ***Height Group*** can be randomized (e.g. an NPC with a range of 0.02 can be assigned a height between 0.98 and 1.02).
  

### Head Part Distribution
To use the head part distribution feature, you must first import the head parts that you wish to distribute. 
- After clicking "Head Parts" in the left navigation pane, it should default to the *Import* tab. 
- Click in the "Import from: " box and select the mod from which you would like to import head parts
-- Note: there may be some lag if you select a large plugin such as Skyrim.esm
- Use the provided checkboxes to filter which headparts to import
- You also manually filter by removing individual headparts from the list boxes (you can also delete them later if necessary)
- When finished, click "Imported Selected Headparts"
- You can now go to the tab for each head part type to set its distribution rules

#### Common Distribution Rules
These rules govern if ANY headpart within this category can be distributed to a given NPC. Most should be familiar from other sections of the patcher. The only unique rules are:
- **Lock to NPCs with this Head Part type**: Only distributes the selected headpart to an NPC that already had the same type of head part. E.g. only distribute scars to NPCs that already have them.
- **Distribution Probability**: Should be 0 - 100. The probability that an NPC will receive a random head part of this type, provided they don't match a Specific NPC assignment or ForceIf attribute (in which case the probability is 100).

#### Head Part Rules
These rules govern the distribution of each individual head part. All should be familiar from other sections of the patcher. 

### Specific NPC Assignments

This menu allows you to specify exactly which Assets, Body Shape, Height, or Head Part you wish to assign to a given NPC.

**NPC**: The NPC to which the selected assignments apply.

**Forced Asset Pack**: The <u>Primary</u> Asset Config File from which the selected NPC must receive assets.

**Forced Subgroups**: Subgroup(s) within the ***Forced Asset Pack*** that must be assigned to the given NPC. Select by dragging and dropping from Available to Selected.

**Forced Asset Replacers**: Subgroup(s) within ***Asset Replacers*** within ***Forced Asset Pack*** which must be assigned to the given NPC. Note that this is for choosing texture variants within a given replacer; this does <u>not</u> allow you to assign a replacer to an NPC that doesn't already have the source asset.

**Forced MixIns**: <u>Mix-In</u> Asset Config File(s) that must be assigned to the given NPC.

- Dropdown menu selects the Mix-In config file name to be forced
  
- Once a config file is selected in the dropdown menu, assign forced subgroups within it by dragging and dropping from Available to Selected.
  
- Once a config file is selected, you can add **MixIn Asset Replacers** to specify replacer subgroups that should be forced from the given Mix-In config file.
  

**Forced Height**: Height that must be assigned to the given NPC.

**Forced Morphs**: BodyGen Morph(s) that must be assigned to a given NPC. Displayed only if the Body Shape is set to be applied using BodyGen.

**Forced BodySlide**: BodySlide Preset that must be assigned to a given NPC. Displayed only if the Body Shape is set to be applied using BodySlide.

### Consistency

The Consistency Menu displays the contents of the consistency file; i.e. which assignments were made during the previous patcher run. Select an NPC in the **Search NPC** box to view its consistency entry. If you wish to re-randomize any of the assignments, click the red X next to the assignment to clear it.

**Name**: The name of the NPC | EditorID | FormKey of the given NPC.

**Asset Pack**: The Primary Asset Config File assigned to the given NPC.

**Subgroups**: The Primary subgroups assigned to the given NPC.

**Mix-In Assignments**: The Name(s) and Subgroup(s) of Mix-In Asset Config File(s) assigned to the given NPC.

**Asset Replacers**: The Name(s) and Subgroup(s) of Asset Replacers from the Primary config file that were assigned to the given NPC.

**BodyGen Morphs**: BodyGen Morph(s) that were assigned to the NPC.

**BodySlide**: BodySlide Preset that was assigned to the given NPC.

**Height**: Height that was assigned to the given NPC.

**Clear Asset Assignments**: Clears all consistency information pretaining to assets:

- Primary Asset Pack and Subgroup(s)
  
- Mix-In Asset Pack(s) and Subgroup(s)
  
- Asset Replacers
  

**Clear Body Shape Assignments**: Clears all BodyGen Morph and BodySlide assignments.

**Clear Height Assignments**: Clears all assigned NPC heights.

**Clear Current NPC**: Clears all consistency assingments for the selected NPC

**Clear Consistency**: Clears all consistency assignmetns for all NPCs.

### Block List

The Block List Menu allows you to prevent NPCs from being patched either by their specific record, or by any plugin that touches them.

**NPCs**: NPCs that are blocked from being patched by their record (FormID)

- **NPC**: Select the NPC to be blocked.
  
- **Block Assets**: Prevent the patcher from modifying the NPC's assets (e.g. textures and meshes).
  
- **Block Height**: Prevent the patcher from modifying the NPC's height.
  
- **Block Body Shape**: Prevent the patcher from assigning a BodyGen Morph or BodySlide to the NPC.
  

**Plugins**: Plugins that are blocked from being patched. NPCs will not be patched if a plugin contains their record, *even if the NPC's master record is not that plugin*.

- **Plugin**: Select the plugin from which NPCs are to be blocked.
  
- **Block Assets**: Prevent the patcher from modifying the NPC's assets (e.g. textures and meshes).
  
- **Block Height**: Prevent the patcher from modifying the NPC's height.
  
- **Block Body Shape**: Prevent the patcher from assigning a BodyGen Morph or BodySlide to the NPC.
  

### Mod Manager Integration

The Mod Manager Integration Menu specifies which mod manager is being used and tells the patcher where to extract files when installing an Asset Config Plugin File.

**Mod Manager Type**: The mod manager being used.

- **None**: Files will be extracted directly to subfolders of the game data folder (strongly not recommended).
  
- **Mod Organizer 2**: Files will be installed as Mod Organizer 2 mods.
  
  - **MO2 Executable Path**: The path of your ModOrganizer.exe file.
    
  - **Mod Folder Path**: The path of your MO2 mods folder.
    
- **Vortex**: Files will be installed as Vortex mods. *Note: Vortex support is currently very crude - you'll need to restart Vortex after installing a Config File, and Vortex will complain that you are managing files externally. I am open to suggestions for how to improve this. I don't use Vortex myself and when I asked on multiple different forums I got either lazy answers ("Install mods using the install button") or no answers at all. I'm happy to code, but if you want better Vortex support you'll need to tell me how to do it.*
  
  - **Mod Staging Folder**: The path of your Vortex Staging folder

### Status Log

Updates regarding the patcher's progress, or an errors encountered, will appear here.

### Assignment Priority

How does the patcher decide which assets to assign? The priorities are as follows:

1. If a ***Specific NPC Assignment*** exists, it has priority over everything. The only case in which it won't be assigned is if the referenced config file or subgroup has been deleted.
  
2. If the NPC is a member of a ***Linked NPC Group*** and is not the primary member, it will receive the same assignments as the primary member.
  
3. If the NPC is unique and has the same name as another unique NPC which has already been assigned, it will receive the same assignments as that NPC.
  
4. Config Files are filtered according to their ***Distribution Rules***
  
  - If a config's Distribution Rules prohibit a given NPC, it is eliminated.
    
  - If a config's ***Allowed Attributes*** include ***Force If Attributes***, the number (and weighting) of matched ***Force If Attributes*** is tallied. The config file(s) with the most matched ***Force If Attributes*** "survive" for subsequent assignment.
    
5. Within the remaining Config Files, subgroups are filtered according to their ***Distribution Rules***.
  
  - If a subgroup's Distribution Rules prohibit a given NPC, it is eliminated.
    
  - If a subgroup's ***Allowed Attributes*** include ***Force If Attributes***, the number (and weighting) of matched ***Force If Attributes*** is tallied. The subgroup(s) with the most matched ***Force If Attributes*** "survive" for subsequent assignment.
    
6. If a consistency assignment exists, and any "surviving" Config Files and/or subgroups match the consistency assignment, it will be re-assigned to that NPC.
  

### Asset and Body Shape Matching

If ***Apply Body Shapes Using*** is set to any option other than **None**, the patcher will attempt to assign compatible assets and body shapes. The intention of this functionality is to ensure that textures and body shapes are visually consistent - if an NPC gets a highly muscular normal map, they should not receive a rotund body shape, and vice-versa. The patcher attempts to find mutually compatible body shapes according to the following algorithm:

1. Assets are assigned as described above in ***Assignment Priority***.
  
2. BodyGen Morphs or BodySlides are selected according to the following priorities:
  
  A) If the given NPC has a Specific NPC Assignment, that Body Shape will be selected no matter what.
  
  B) If the NPC is a member of a ***Linked NPC Group*** and is not the primary member, it will receive the same assignments as the primary member.
  
  C) If the NPC is unique and has the same name as another unique NPC which has already been assigned, it will receive the same assignments as that NPC.
  
  D) Available BodyGen Morphs or BodySlides are filtered according to their ***Distribution Rules***.
  
  - These Distribution Rules include all ***Allowed/Disallowed Body Shape Descriptors*** from the assigned subgroups.
    
  - If an assigned subgroup has an ***Allowed Body Shape*** of a given category, only morphs/bodyslides that match the given descriptor can be assigned.
    
  - If an assigned subgroup has a ***Disallowed Body Shape*** of a given category, morphs/bodyslides that match the given descripter cannot be assigned.
    
  
  E) If a consistency assignment exists, and "surviving" BodyGen morphs or BodySlides match the consistency assignment, they will be reassigned.
  
3. If a Body Shape is successfully assigned in Step 2, the assignments are kept.
  
4. If a Body Shape is <u>not</u> successfully assigned in Step 2, the patcher attempts the following:
  
  A) Try to assign again without filtering Body Shapes by consistency
  
  B) Try to assign again without regarding the ***Allowed/Disallowed Body Shape Descriptors*** from the assigned assets.
  
  If step B is successful, the patcher chooses a different subgroup combination and re-evaluates.
  
  If step B is unsuccessful, then the patcher keeps the assigned combination and chooses a random body shape without regarding any distribution rules.
  
5. If the patcher tries all possible subgroup combinations and still cannot assign a Body Shape that complies with the ***Allowed/Disallowed Body Shape Descriptors***, it will keep the first assigned subgroup combination and the first assigned body shape, and notify the user that no mutually compatible subgroup combinations and body shapes exist.
  

### Attribute Assignment System

The Attribute Assignment System is at the core of how SynthEBD decides which Assets and Body Shapes can go to which NPCs. Attributes can be Allowed, Disallowed, or Forced, with Disallowed attributes having priority over Allowed. Each Attribute within an Attribute List is treated with OR logic - if any of the attributes are matched, the NPC is considered to be matched. Additional attributes can be added with the **OR** button. Within each Attribute are Sub-Attributes. If only one Sub-Attribute exists then it is by definition the entirety of the parent Attribute. However, if multiple Sub-Attributes exist, they are treated with AND logic - all of the Sub-Attributes must be matched for the NPC to match the parent attribute. Sub-Attributes can be added via the **AND** button.

##### Attribute Types

Attributes can belong to one of several types, most of which should be fairly obvious from their name:

**Class** Attributes specify NPCs whose class matches the specified Class records.

**Faction** Attributes specify NPCs belonging to Factions, with a Rank between the specified minimum and maximum (for the base record). This does not take in-game faction rank progression into account if you are patching an existing save.

**Face Texture** Attributes specify NPCs that have a specific face texture record. This is typically used for matching age-related face textures (Age40/50 variants, Rough variants, Freckles, etc).

**NPC** Attributes specify NPCs - i.e. if the given NPC is found within the selected list of NPCs.

**Race** Attributes specify NPC races. Note that these are subservient to Allowed/Disallowed Race Groupings. The difference here is that for Allowed Attributes, the ***Force If*** checkbox becomes available.

**Voice Type** Attributes speciy the voice type of an NPC.

In addition to the above, two special types of attributes exist:

**Custom** Attributes allow you to specify any data element belong to the NPC record or its subrecord. To use it, first specify a reference NPC that you know matches the given attribute (this is only for the UI to tell you if your Custom Attribute works - the reference NPC has no bearing on the actual patching process). Then specify the type of the attribute: **Text**, **Integer**, **Decimal**, **Boolean** (True/False), or **Record**. Finally, set the condition you wish to evaluate with your custom attribute. The animation below depicts the usage of Custom attributes:

https://user-images.githubusercontent.com/63175798/154865378-730d3eb2-0c45-4fba-b15d-fd1a4f43c561.mp4

**Group** Attributes present a checkbox corresponding to the list of available ***Attribute Groups***. These ***Attribute Groups*** are sourced from the parent Asset Config Plugin File, BodyGen Config File, or O/AutoBody Settings. If an Attribute Group is checked, it gets replaced with its constitutent Attributes at the start of patcher execution.

**Force If Attributes**: Attributes within an ***Allowed Attributes*** List or an ***Attribute Groups*** Menu have a **Force If** checkbox. If this box is checked, the parent Config File, Subgroup, BodyGen Morph, or BodySlide is considered to be Forced - if an NPC matches the attribute then the owner MUST be assigned, unless another owner within the same list has more matched **Force If Attributes** with a higher cumulative **Weight**.

##### Attribute Groups

***Attribute Groups*** are, as the name implies, groups of Attributes that can be referenced by an Allowed/Disallowed Attribute List. These groups are configurable in the General Settings Menu, and allow the user a centralized way to manage asset and body shape distribution. The desired use case of **Attribute Groups** is for managing similar types of assets and body shapes. For example, all very muscular body normal maps should have the *Must Be Muscular* **Attribute Group** as a **Force If Attribute**. That way the user can easily manage which individual Attributes *Must Be Muscular* from the General Settings Menu, and these settings will apply to all installed config files as long as their muscular body normals are correctly assigned with the appropriate Group.

### Body Shape Descriptor Menu

Body Shape Descriptors are Category: Value pairs that describe a given body shape. They are used to ensure that assets are correctly paired with body shapes. The Descriptor Menus are located within BodyGen Configs and the patcher's (O/Auto)Body Integration menu. The descriptors are synced with the currently installed Asset Config Plugin Files, which can reference them as Allowed or Disallowed descriptors for any particular asset. For example, an Asset Subgroup containing highly muscular body normal maps might want to list "Build: Chubby" as a *Disallowed Body Shape Descriptor* so that less fit NPCs don't get assigned highly muscular textures and experience a visual mismatch. In addition to controlling Asset / Shape pairing, these descriptors can also come with their own Distribution Rules. If a BodyGen morph or BodySlide is tagged with a descriptor, it combines the descriptor's Distribution Rules with its own sub-rules. This provides a centralized way to control the distribution of large groups of Body Shapes.

### Known Issues

- Slow startup when BodyGen configs are installed. This is likely to be fixed in a future update.
  
### F.A.Q.

Q: Why is called SynthEBD when it doesn't use Synthesis?

A: It uses Mutagen, which Synthesis is built on, and "MutEBD" doesn't have the same ring to it.

Q: I'm a new user, what do I do?

A: Please read the Getting Started section and come back if you have specific questions. This isn't to be rude or terse, but simply because the patcher has too many options to be walked through in a short post or comment.

Q: What happened to the "Director's Cut" Config Files?

A: In zEBD, I distributed config files in two versions: blank (no distribution rules, where only the texture paths were defined) and "Director's Cut" (my take on how the textures should be distributed by class/faction/etc). In SynthEBD, all of the config files are "Director's Cut" to begin with. The reasons for this are A) having to maintain two versions of each config file is obnoxious, B) most new users would probably prefer having my distribution rules, even if they don't quite perfectly align with their preferences, to no distribution rules at all, and C) if a user is comfortable enough with SynthEBD to fill out the blank config file, they should be equally comfortable modifying my existing rule set.

Q: I want to distribute textures from Mod X, but there's no config file for it. What do I do?

A: If the zEBD Nexus page has a config file for Mod X, I haven't gotten around to uploaded the SynthEBD-formatted version but I do plan to do it. If you need it ASAP you can install the zEBD-formatted config file through SynthEBD and it will be auto-upgraded (with the caveats described above). If I have not made a config file for the mod you want, you can ask but I'm getting ever busier IRL and can't guarantee I'll have time to do it. Please check out how the existing configs work and take a stab at making one yourself! You can even upload it to the Nexus to get some sweet mod author cred ;)
