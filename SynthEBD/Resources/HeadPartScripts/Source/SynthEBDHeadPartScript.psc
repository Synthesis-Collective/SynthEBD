Scriptname SynthEBDHeadPartScript extends ActiveMagicEffect

import PSM_SynthEBD
import EBDHeadPartFuncs
import EBDGlobalFuncs

import SynthEBDCommonFuncs

GlobalVariable Property VerboseMode Auto

Event OnEffectStart(Actor akTarget, Actor akCaster)
	RegisterForModEvent("SynthEBD_HeadPartsReloaded", "OnHeadPartReload")
	SetHeadParts(akCaster, false) ; loads headparts previously stored in the JFormDB
EndEvent

Event OnHeadPartReload()
	SetHeadParts(GetCasterActor(), true) ; JFormDB has been refreshed - set the headparts again to bring about any changes
EndEvent

function SetHeadParts(Actor akCaster, bool onReload)
	ActorBase akBase = getProperActorBase(akCaster)
	string actorName = akBase.GetName() ; for logging only
	if actorName == ""
		actorName = "Unnamed"
	endif
	
	bool updated = False
	
	if (SetHeadPart(akCaster, akBase, "Beard", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Brows", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Eyes", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Face", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Hair", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Misc", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, "Scars", actorName, onReload))
		updated = True;
	endif
EndFunction

bool Function SetHeadPart(Actor target, ActorBase akBase, string headPartType, string actorName, bool onReload)
	int headParts = JFormDB_solveObj(akBase, ".SynthEBD.HeadParts")
	if (headParts)
		string headPartStr = JMap_getStr(headParts, headPartType)
		if (headPartStr)
			form headPartForm = SynthEBDCommonFuncs.FormKeyToForm(headPartStr, false)
			if (headPartForm)
				HeadPart hp = headPartForm as HeadPart
				if (hp)
					;target.ChangeHeadPart(hp)
					target.ReplaceHeadPart(None, hp)
					
					if headPartType == "Hair"
						UpdateHead(target) ; only needed for hair
					endif
					VerboseLogger("Assigning new " + headPartType + " to NPC: " + actorName + " (" + akBase + "): " + hp, VerboseMode, false)
					return true
				else
					VerboseLogger("SynthEBD: Could not get head part from form " + headPartForm, VerboseMode, false)
				endif
			else
				VerboseLogger("SynthEBD: Could not convert string " + headPartStr + " to a Form", VerboseMode, false)
			endif
		elseif(onReload) ; only warn if assignment failed with the most recent database 
			VerboseLogger("SynthEBD: Head Part Database for NPC " + actorName + " has no entry for " + headPartType, VerboseMode, false)
		endif
	elseif(onReload) ; only warn if assignment failed with the most recent database 
		VerboseLogger("SynthEBD: Head Part Database has no entry for NPC " + actorName, VerboseMode, false)
	endif
						
	return false
EndFunction