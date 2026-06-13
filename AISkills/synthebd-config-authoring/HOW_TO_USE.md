# Using an AI assistant to create or update SynthEBD config files

SynthEBD ships with two things that let an AI assistant do most of the config-making work for you:

- **`SynthEBD.CLI.exe`** (in your SynthEBD folder) — a command-line version of the Config Drafter,
  validator, Distribution Simulator, and packager that the AI runs to do and check its work.
- **`AISkills\synthebd-config-authoring\SynthEBD_SKILL.md`** — an instruction file that teaches the
  AI the whole workflow: how config files are structured, what questions to ask you, and how to
  verify the result.

You don't need to understand either file. You just point your AI assistant at the instruction file
and tell it what you want.

## What you need

1. **SynthEBD installed** (the folder containing `SynthEBD.exe` also contains `SynthEBD.CLI.exe`
   and the `AISkills` folder), on a PC with Skyrim SE/AE installed.
2. **An AI assistant that runs on your PC** — one with direct access to your files and the ability
   to run programs on your Windows machine. Good choices: **Claude Code** (in a terminal, or its
   VS Code / JetBrains extension), Codex CLI, Gemini CLI, or any similar local "agent" tool.

   **What will _not_ work:** the **Claude desktop app's chat**, **claude.ai** in a browser, or any
   assistant whose "code"/"analysis" tool runs in the cloud. Even though these look like they can run
   code, that code runs in an isolated online sandbox that **cannot see your `C:\` drive and cannot
   run `SynthEBD.CLI.exe`** — so they can't inventory your archives, draft against real textures, or
   validate the result. If you ask one of these, it will (correctly) tell you it can't reach your
   files; that's your cue to switch to a local agent like Claude Code. The single best choice for
   Claude users is **Claude Code**, which is the same Claude, just running locally on your machine.
3. **The texture mod's download files** from Nexus — the .7z/.zip/.rar archives, or the folders if
   you already extracted them. You do not need to install the mod first.

## Scenario 1: A new texture mod released and you want a config for it

1. Create an empty working folder somewhere with a short path, e.g. `C:\temp\MyModWork`.
2. Start your AI assistant and send it a message like this (fill in your paths):

   > Read `C:\Games\SynthEBD\AISkills\synthebd-config-authoring\SynthEBD_SKILL.md` and follow it.
   > I want to draft a new SynthEBD config file for the texture mod "Bits and Pieces Female Skin".
   > The downloaded archives are in `C:\Users\Me\Downloads\BnP`. Use `C:\temp\MyModWork` as the
   > working folder. SynthEBD is installed at `C:\Games\SynthEBD`.

3. **Answer its questions.** A good assistant following the instructions will ask you things like:
   - What does the mod page description say? (Paste it, or say there isn't a useful one.)
   - Which downloads go together — base files, updates, hotfixes, resolution options?
   - Should identical duplicate textures be merged to save video memory? (Usually yes.)
   - Is the mod for a CBBE-family (3BA) or UNP-family (BHUNP) body? (Check the mod page.)
   - Should some unrecognized textures be kept or ignored?
   - Any preferences about who gets what — e.g. "the fantasy skins should only go to mages"?

   If you're not sure about something, say so and ask it to explain or recommend.
4. **Wait while it works.** It will extract the files, draft the config, fix names and linkages,
   validate it, and run the Distribution Simulator on test NPCs to prove the textures actually get
   assigned. Expect a few more questions along the way.
5. **Check the result.** Open SynthEBD — the new config appears in the Textures and Meshes menu.
   Look over the subgroups, run "Simulate Distribution" on an NPC or two yourself if you like, then
   run the patcher and spot-check in game (look for neck seams — that's the classic sign of face
   and body textures that shouldn't be paired).

## Scenario 2: A texture mod updated and your config needs to catch up

Same idea — point the assistant at the instruction file and describe the situation:

> Read `C:\Games\SynthEBD\AISkills\synthebd-config-authoring\SynthEBD_SKILL.md` and follow it.
> The mod "Bits and Pieces Female Skin" updated from 2.0 to 2.1 and my config "SynthEBD - BnP 4K
> CBBE" needs to be updated. The new downloads are in `C:\Users\Me\Downloads\BnP21`. SynthEBD is
> installed at `C:\Games\SynthEBD`.

The assistant will compare the old and new files, swap updated texture paths, flag textures the
author removed, offer to add subgroups for new options, and re-validate and re-simulate the config
when done. Paste the mod's changelog if it asks — but know that changelogs are sometimes
incomplete, which is exactly why it checks the actual files too.

## Scenario 3: Packaging a config to share on Nexus

> Read `C:\Games\SynthEBD\AISkills\synthebd-config-authoring\SynthEBD_SKILL.md` and follow it.
> Package my configs "SynthEBD - MyMod 4K" and "SynthEBD - MyMod 2K" for distribution. The original
> mod archives are in `C:\Users\Me\Downloads\MyMod`.

It will write the installer manifest (including the 4K/2K choice), build the archive, and then
verify that *every* possible installer selection installs correctly against the real mod archives
before telling you it's done.

## Good to know

- The assistant only writes config JSON files (into SynthEBD's `Asset Packs` folder or folders you
  give it) — it does not touch your game install or your mod manager.
- Everything it produces is checked by SynthEBD's own validation and simulation code, but you are
  the final reviewer: look at the config in the SynthEBD UI and in game before publishing anything.
- If the assistant says it can't find Skyrim or your SynthEBD settings, tell it your install paths —
  the CLI accepts them as options and the instruction file tells it how.
- If the assistant says it can't see your files **at all**, or can't find `SynthEBD.CLI.exe` (as
  opposed to just not knowing your paths), you're almost certainly in a cloud/sandboxed chat — switch
  to a locally-running agent like Claude Code (see "What you need" above). Uploading the archives into
  a cloud chat is not a substitute: the assistant still can't validate or simulate the config against
  your Skyrim install, which is the part that proves it actually works.
- Claude Code users: you can also install this as a personal skill by copying the
  `synthebd-config-authoring` folder into `%USERPROFILE%\.claude\skills\` and renaming
  `SynthEBD_SKILL.md` to `SKILL.md` inside it — then it activates automatically when you ask for
  SynthEBD config work. The explicit "Read the file and follow it" message works in every tool.
