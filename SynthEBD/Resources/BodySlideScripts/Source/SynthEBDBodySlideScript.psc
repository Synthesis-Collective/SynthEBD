Scriptname SynthEBDBodySlideScript extends ActiveMagicEffect

import PSM_SynthEBD
import EBDGlobalFuncs

import SynthEBDCommonFuncs

string Property TargetMod Auto
GlobalVariable Property VerboseMode Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	RegisterForModEvent("SynthEBD_BodySlidesReloaded", "OnBodySlideReload")
	ApplyBodySlide(akCaster, false) ; loads bodyslides previously stored in the JFormDB
EndEvent

Event OnBodySlideReload()
	ApplyBodySlide(GetCasterActor(), true) ; JFormDB has been refreshed - set the bodyslides again to bring about any changes
EndEvent

function ApplyBodySlide(Actor akCaster, bool onReload)
	ActorBase akBase = getProperActorBase(akCaster)
	string actorName = akBase.GetName()
	if actorName == ""
		actorName = "Unnamed"
	endif
	
	string formKey = FormKeyFromForm(akBase,  true)
	string assignment = JDB_solveStr(".SynthEBD.BodySlides." + formKey)
	if assignment != ""
		ApplyPresetByName(akCaster, assignment)
		VerboseLogger("SynthEBD: Assigned bodyslide preset: " + assignment + " to NPC: " + actorName + " (" + akBase + ")", VerboseMode, true)
	ElseIf(onReload) ; only warn if assignment failed with the most recent database 
		VerboseLogger("No bodyslide assignment recorded for NPC: " + actorName + " (" + akBase + ")", VerboseMode, true)
	endif	
EndFunction

function ApplyPresetByName(Actor akCaster, string assignment)
	if (TargetMod == "OBody")
		OBodyNative.ApplyPresetByName(akCaster, assignment)
	ElseIf (TargetMod == "AutoBody")
		autoBodyUtils.ApplyPresetByName(akCaster, assignment)
	endif
endFunction