Scriptname SynthEBDHeadPartScript extends ActiveMagicEffect

import PSM_SynthEBD
import EBDHeadPartFuncs
import EBDGlobalFuncs

GlobalVariable Property loadingCompleted Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	ActorBase akBase = getProperActorBase(akCaster)
	
	while (loadingCompleted.GetValue() == 0)
		Utility.Wait(2)
	endwhile
		
	bool updated = False
	
	if (SetHeadPart(akCaster, akBase, "Misc"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Face"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Eyes"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Beard"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Scars"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Brows"))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Hair"))
		updated = True;
	endif
	
	if (updated)
		ActorBase akTemplate = akBase.getTemplate()
		if (akTemplate)
			FixFaceTexture(akCaster, akTemplate)
		else
			FixFaceTexture(akCaster, akBase)
		EndIf
		akCaster.QueueNiNodeUpdate()
	endif
EndEvent

bool function SetHeadPart(Actor target, ActorBase akBase, string headPartType)
	string sourcePath = ".SynthEBD.HeadPart." + headPartType
	form headPartForm = JFormDB_getForm(akBase, sourcePath)
	string actorName = akBase.GetName() ; for logging only
	if (headPartForm)
		HeadPart headPartAsHP = headPartForm as HeadPart
		if (headPartAsHP)
			target.ChangeHeadPart(headPartAsHP)
			
			if headPartType == "Hair"
				UpdateHead(target) ; only needed for hair
			endif
			
			;debug.Notification("Assigned new " + headPartType + " to NPC: " + actorName)
			debug.Trace("Assigning new " + headPartType + " to NPC: " + actorName + " (" + akBase + "): " + headPartAsHP)
			
			return true
		endif
	endif
	
	;debug.Notification("No " + headPartType + " found for NPC: " + actorName)
	debug.Trace("No " + headPartType + " found for NPC: " + actorName + " (" + akBase + ")")
		
	return false
EndFunction