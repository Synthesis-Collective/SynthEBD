Scriptname SynthEBDBodySlideScriptAutoBody extends ActiveMagicEffect

import PSM_SynthEBD
import EBDGlobalFuncs
import SynthEBDCommonFuncs

GlobalVariable Property SynthEBDDataBaseLoaded Auto
MagicEffect Property SynthEBDBodySlideMGEF Auto
Spell Property SynthEBDBodySlideSpell Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	ApplyBodySlide(akCaster)
EndEvent

function ApplyBodySlide(Actor akCaster)
	ClearActorEffect(akCaster, SynthEBDBodySlideMGEF, SynthEBDBodySlideSpell)
	
	ActorBase akBase = getProperActorBase(akCaster)
	string actorName = akBase.GetName()
	if actorName == ""
		actorName = "Unnamed"
	endif
	
	while (SynthEBDDataBaseLoaded.GetValue() == 0)
		Utility.Wait(2)
	endwhile

	string assignment = JFormDB_getStr(akBase, ".SynthEBD.BodySlide")
	
	if assignment != ""
		autoBodyUtils.ApplyPresetByName(akCaster, assignment)
		;debug.Notification("Assigned bodyslide preset: " + assignment + " to NPC: " + actorName)
		;debug.Trace("Assigned bodyslide preset: " + assignment + " to NPC: " + actorName + " (" + akBase + ")")
	else 
		;debug.Notification("No assignment recorded for NPC: " + actorName)
		;debug.Trace("No assignment recorded for NPC: " + actorName + " (" + akBase + ")")
	endif
EndFunction