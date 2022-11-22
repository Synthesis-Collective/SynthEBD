Scriptname SynthEBDHeadPartScript extends ActiveMagicEffect

import PSM_SynthEBD
import EBDHeadPartFuncs
import EBDGlobalFuncs

import SynthEBDCommonFuncs

GlobalVariable Property SynthEBDDataBaseLoaded Auto
MagicEffect Property SynthEBDHeadPartMGEF Auto
Spell Property SynthEBDHeadPartSpell Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	SetHeadParts(akCaster)
EndEvent

function SetHeadParts(Actor akCaster)
	ClearActorEffect(akCaster, SynthEBDHeadPartMGEF, SynthEBDHeadPartSpell)

	ActorBase akBase = getProperActorBase(akCaster)
	
	while (SynthEBDDataBaseLoaded.GetValue() == 0)
		Utility.Wait(2)
	endwhile
		
	bool updated = False
	
	if (SetHeadPart(akCaster, akBase, "Beard"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Brows"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Eyes"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Face"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Hair"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Misc"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Scars"))
		updated = True;
	endif
EndFunction

bool Function SetHeadPart(Actor target, ActorBase akBase, string headPartType)
	string sourcePath = ".SynthEBD.HeadPart." + headPartType
	form headPartForm = JFormDB_getForm(akBase, sourcePath)
	string actorName = akBase.GetName() ; for logging only
	if (headPartForm)
		HeadPart headPartAsHP = headPartForm as HeadPart
		if (headPartAsHP)
			;target.ChangeHeadPart(headPartAsHP)
			target.ReplaceHeadPart(None, headPartAsHP)
			
			if headPartType == "Hair"
				UpdateHead(target) ; only needed for hair
			endif
			;debug.Trace("Assigning new " + headPartType + " to NPC: " + actorName + " (" + akBase + "): " + headPartAsHP)
			
			return true
		endif
	endif
	
	;debug.Trace("No " + headPartType + " found for NPC: " + actorName + " (" + akBase + ")")		
	return false
EndFunction