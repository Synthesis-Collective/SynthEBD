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
	
	string formKey = FormKeyFromForm(akBase,  true)
	
	bool updated = False
	
	if (SetHeadPart(akCaster, akBase, formKey, "Beard", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, formKey, "Brows", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, formKey, "Eyes", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, formKey, "Face", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, formKey, "Hair", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, formKey, "Misc", actorName, onReload))
		updated = True;
	endif
	if (SetHeadPart(akCaster, akBase, formKey, "Scars", actorName, onReload))
		updated = True;
	endif
	
	if (updated)
		ActorBase akTemplate = akBase.getTemplate()
		if (akTemplate)
			FixFaceTexture(akCaster, akTemplate)
		else
			FixFaceTexture(akCaster, akBase)
		EndIf
	endif
EndFunction

bool Function SetHeadPart(Actor target, ActorBase akBase, string formKey, string headPartType, string actorName, bool onReload)
	string headPartStr = JDB_solveStr(".SynthEBD.HeadParts." + formKey + "." + headPartType)
	if (headPartStr)
		form headPartForm = SynthEBDCommonFuncs.FormKeyToForm(headPartStr, false, false) ; HeadPart formkeys are container values, not paths, so leave bJCPathCompatibility false
		if (headPartForm)
			HeadPart hp = headPartForm as HeadPart
			if (hp)
				;target.ChangeHeadPart(hp)
				target.ReplaceHeadPart(None, hp)
				
				if headPartType == "Hair"
					UpdateHead(target) ; only needed for hair
				endif
				VerboseLogger("Assigning new " + headPartType + " to NPC: " + actorName + " (" + akBase + "): " + hp, VerboseMode.GetValue(), true)
				return true
			else
				VerboseLogger("SynthEBD: Could not get head part from form " + headPartForm, VerboseMode.GetValue(), false)
			endif
		else
			VerboseLogger("SynthEBD: Could not convert string " + headPartStr + " to a Form", VerboseMode.GetValue(), false)
		endif
	elseif(onReload) ; only warn if assignment failed with the most recent database 
		VerboseLogger("SynthEBD: Head Part Database for NPC " + actorName + " has no entry for " + headPartType, VerboseMode.GetValue(), false)
	endif
				
	return false
EndFunction