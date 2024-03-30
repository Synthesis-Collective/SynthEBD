Scriptname SynthEBDBodySlideScript extends ActiveMagicEffect

import PSM_SynthEBD
import EBDGlobalFuncs

import SynthEBDCommonFuncs

string Property TargetMod Auto
GlobalVariable Property VerboseMode Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	RegisterForModEvent("SynthEBD_BodySlidesReloaded", "OnBodySlideReload")	
	if (TargetMod == "OBody")
		RegisterForModEvent("obody_manualchange", "OnManualChangeOBody")
	endif	
	ApplyBodySlide(akCaster, false) ; loads bodyslides previously stored in the JFormDB
EndEvent

Event OnBodySlideReload()
	if !self
		return
	endif
	ApplyBodySlide(GetCasterActor(), true) ; JFormDB has been refreshed - set the bodyslides again to bring about any changes
EndEvent

Event OnManualChangeOBody(Form Act)
	IgnoreActor (Act as Actor)
EndEvent

function IgnoreActor(Actor akActor)
    ActorBase akBase = getProperActorBase(akActor)
	string actorName = GetActorBaseName(akBase)
	string formKey = FormKeyFromForm(akBase,  true)
	JDB_solveIntSetter(".SynthEBD.IgnoredBodySlides." + formKey, 1, true)
	VerboseLogger("SynthEBD: Detected in-game bodyslide assignment for " + actorName + " (" + akBase + ")", VerboseMode.GetValue(), true)
endFunction

function ApplyBodySlide(Actor akCaster, bool onReload)
	ActorBase akBase = getProperActorBase(akCaster)
	string actorName = GetActorBaseName(akBase)
	string formKey = FormKeyFromForm(akBase,  true)
	string assignment = JDB_solveStr(".SynthEBD.BodySlides." + formKey)
	int ignoreThisNPC = JDB_solveInt(".SynthEBD.IgnoredBodySlides." + formKey)
	
	if (assignment != "" && ignoreThisNPC != 1)
		ApplyPresetByName(akCaster, assignment)
		VerboseLogger("SynthEBD: Assigned bodyslide preset: " + assignment + " to NPC: " + actorName + " (" + akBase + ")", VerboseMode.GetValue(), true)
	ElseIf(ignoreThisNPC == 1)
		VerboseLogger("SynthEBD: Ignoring " + actorName + " (" + akBase + ") for BodySlide due to in-game assignment", VerboseMode.GetValue(), true)
	ElseIf(onReload) ; only warn if assignment failed with the most recent database 
		VerboseLogger("SynthEBD: No bodyslide assignment recorded for NPC: " + actorName + " (" + akBase + ")", VerboseMode.GetValue(), true)
	endif	
EndFunction

function ApplyPresetByName(Actor akCaster, string assignment)
	if (TargetMod == "OBody")
		OBodyNative.ApplyPresetByName(akCaster, assignment)
	ElseIf (TargetMod == "AutoBody")
		autoBodyUtils.ApplyPresetByName(akCaster, assignment)
	endif
endFunction