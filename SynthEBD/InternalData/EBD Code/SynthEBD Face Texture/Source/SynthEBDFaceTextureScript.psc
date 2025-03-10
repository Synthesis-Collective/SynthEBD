scriptName SynthEBDFaceTextureScript extends ActiveMagicEffect
{applies the face texture which is set up in the npc record, changes headparts for NPCs}

import EBDGlobalFuncs ;direct access to the global funcs
import SynthEBDCommonFuncs
import PSM_SynthEBD

Keyword property SynthEBDFaceTextureKeyword Auto
GlobalVariable Property FaceTextureScriptActive Auto
GlobalVariable Property VerboseMode Auto
Actor property PlayerREF Auto
string[] property TriggerEventNames auto
Spell property SynthEBDFaceTextureSpell Auto
string property ScriptEditorIdMode Auto

State Busy

EndState

EVENT OnEffectStart(Actor akTarget, Actor akActor)
	RegisterForNiNodeUpdate() ; Registers for the NiNodeUpdate event
	RegisterForCustomEvents() ; Registers for additional events via the SynthEBD UI
	RegisterForModEvent("SynthEBD_ReloadFaces", "OnGameHasLoaded")
	RegisterForModEvent("SynthEBD_SkinTexturesReloaded", "OnDatabaseReload")
	SetTextures(akActor, "OnEffectStart") ; Calling function for fixing face texture
EndEvent

Event OnNiNodeUpdate(ObjectReference akActorRef) ; Other scripts calling QueNiNodeUpdate overrides the SynthEBD-applied face texture, so the texture needs to be re-applied on this event.
	Actor akActor = akActorRef as Actor
	if (akActor)
		SetTextures(akActor, "OnNiNodeUpdate")
	else
		VerboseLogger("SynthEBD: " + akActorRef  + "received an NiNodeUpdate but is not an actor", VerboseMode.GetValue(), true)	
	EndIf
EndEvent

Event OnSynthEBDSubscribedEvent(string eventName, string strArg, float numArg, Form sender) ; Re-apply face texture on custom events registered via SynthEBD UI (must match these params)
	if !self
		return
	endif
	actor akActor = GetTargetActor()
	SetTextures(akActor, eventName)
EndEvent

Event OnGameHasLoaded()
	if !self
		return
	endif
	actor akActor = GetTargetActor()
	SetTextures(akActor, "Game Reload")
EndEvent

Event OnDatabaseReload()
	if !self
		return
	endif
	actor akActor = GetTargetActor()
	SetTextures(akActor, "Database Reload")
EndEvent

Event OnPlayerLoadGame() ; Fix actors in current cell when player reloads game.
	int handle = ModEvent.Create("SynthEBD_ReloadFaces")
	if (handle)
		VerboseLogger("SynthEBD: Sending SynthEBD_ReloadFaces", VerboseMode.GetValue(), false)
		ModEvent.Send(handle)
	endif
EndEvent

function SetTextures(Actor akActor, string eventName)
	;ClearActorEffect(akActor, GetBaseObject(), SynthEBDFaceTextureSpell) ; EBD SSE used spell dispelling to fix texture application on game load, but dispelling prevents this script from detecting events after OnEffectStart. This functionality has been replaced by repurposing FixDeadActors() as FixDeadActors(). 
	if (akActor && isSKSEinstalled() && FaceTextureScriptActive.getValue() == 1)
		If (akActor == PlayerREF)
			FixDeadActors()	; Still necessary here, as well as OnPlayerLoadGame, to fix dead actor faces when the player changes cells
		EndIf
		
		ActorBase akActorBase = getProperActorBase(akActor)
		
		string[] textureAssignmentInfo = GetTextureAssignmentInfo(akActorBase)
		string faceDbAssignment = textureAssignmentInfo[1]
		string skinDbAssignment = textureAssignmentInfo[2]
		
		if (IsTextureEligible(textureAssignmentInfo))
			VerboseLogger("SynthEBD: " + akActorBase.GetName() + ": " + eventName  + " called  FaceTexture script", VerboseMode.GetValue(), true)	
		
			GoToState("Busy")
			Utility.Wait(getWaitTimeByDistance(PlayerREF.GetDistance(akActor)))	; Apply textures to other NPCs first if they're closer to the player	
			VerboseLogger("SynthEBD: Check Actor: " + akActorBase.getName() + "; Race: " + akActorBase.getRace().getName() + "; Distance: " + PlayerREF.GetDistance(akActor) as string, VerboseMode.GetValue(), false)																															

			ActorBase akTemplate = akActorBase.getTemplate()
			
			;; NOTE - Skin patching needs a QueueNiNodeUpdate call to show over equipped clothes, but this script also needs to listen to this event, which causes infinite texture loop
			;; I'm sure this is solvable in Papyrus, but the easiest solution is to just use SkyPatcher to set the WNAM. Opting for this approach and commenting out the following.
			
			;/ 
			;; WNAM only needs to be set by script if:
			;; a) SynthEBD is in Script-Only mode(WNAM is not set in the .esp file)
			;; b) The NPC is a TemplateActorBase (according to the original EBD code; I don't understand why but if it ain't broke...)
		    
			bool isTemplateAB = isTemplateActorBase(akActor)
			string skinEDID = "UNDEFINED"
			Armor arSkin
			if (skinDbAssignment != "")
				form skinTextureForm = SynthEBDCommonFuncs.FormKeyToForm(skinDbAssignment, false, false)
				if (skinTextureForm)
					VerboseLogger("Form: " + getEDID(skinTextureForm), true, true)
					Armor parsedArmor = skinTextureForm as Armor
					if (parsedArmor)
						VerboseLogger("Success: " + getEDID(skinTextureForm), true, true)
						arSkin = parsedArmor
					else
						VerboseLogger("Fail: " + getEDID(skinTextureForm), true, true)
					EndIf
				EndIf
			else
				arSkin = akActorBase.getSkin()
			EndIf
			
			if (arSkin && skinDbAssignment != "" || isTemplateAB)
				skinEDID = getEDID(arSkin)
				
				if (isTemplateAB)
					akActor.getLeveledActorBase().setSkin(arSkin)
					VerboseLogger("SynthEBD: Found template actor to fix body skin for. Name: " + akActorBase.GetName()  + ", Base:" + akActorBase as String + ", NameLvl: " + akActor.getLeveledActorBase().getName() + ", LvlBase: " + akActor.getLeveledActorBase() as string, VerboseMode.GetValue(), false)	
				else
					akActorBase.setSkin(arSkin)
				EndIf	
			EndIf	 /;
			;; end WNAM patching
			
			;; All NPCs need Face patching via script because it doesn't update from the .esp file
			TextureSet akActorFaceTexSet
			
			string faceEDID = "UNDEFINED"
			if (faceDbAssignment != "")
				form faceTextureForm = SynthEBDCommonFuncs.FormKeyToForm(faceDbAssignment, false, false)
				if (faceTextureForm)
					akActorFaceTexSet = faceTextureForm as TextureSet
				EndIf
			EndIf
			
			if (akTemplate)
				if (!akActorFaceTexSet)
					akActorFaceTexSet = akTemplate.GetFaceTextureSet()
				EndIf
			
				FixFaceTextureNew(akActor, akTemplate, akActorFaceTexSet, ScriptEditorIdMode)
				VerboseLogger("SynthEBD: Found template for: Name: " + akActorBase.GetName()  + ", Base:" + akActorBase as String + ", NameLvl: " + akActor.getLeveledActorBase().getName() + ", LvlBase: " + akActor.getLeveledActorBase() as String + "; Template" + akTemplate.GetName() + "; " + akTemplate as string, VerboseMode.GetValue(), false)	
			else
				if (!akActorFaceTexSet)
					akActorFaceTexSet = akActorBase.GetFaceTextureSet()
				EndIf
				
				FixFaceTextureNew(akActor, akActorBase, akActorFaceTexSet, ScriptEditorIdMode)
			EndIf
			
			;; End Face Patching
			
			if (akActorFaceTexSet)
				faceEDID = getEDID(akActorFaceTexSet)
			EndIf
			
			GoToState("")
			;VerboseLogger("SynthEBD: " + akActorBase.GetName()  + " got the following FaceTextureSet: " + faceEDID + "; Skin: " + skinEDID, VerboseMode.GetValue(), true)
			VerboseLogger("SynthEBD: " + akActorBase.GetName()  + " got the following FaceTextureSet: " + faceEDID, VerboseMode.GetValue(), true)			
		EndIf					
	EndIf
EndFunction

bool Function IsTextureEligible(string[] textureAssignmentInfo)
	return textureAssignmentInfo[0] == "True"
EndFunction

string[] Function GetTextureAssignmentInfo(ActorBase akActorBase)
	string[] outputs = new string[3] ;; [0] = eligibility, [1] = face texture Form, [2] = skin texture form
	
	outputs[1] = ""
	outputs[2] = ""
	
	if(!akActorBase)
		outputs[0] = "False"
	else
		string formKey = FormKeyFromForm(akActorBase,  true)
		outputs[1] = JDB_solveStr(".SynthEBD.FaceTextures." + formKey)
		outputs[2] = JDB_solveStr(".SynthEBD.SkinTextures." + formKey)
		
		bool hasDatabaseAssignment = outputs[1] != "" || outputs[2] != ""
		
		if(akActorBase.hasKeyword(SynthEBDFaceTextureKeyword) || hasDatabaseAssignment)
			outputs[0] = "True"
		else
			outputs[0] = "False"
		EndIf
	EndIf
	
	;VerboseLogger(akActorBase.getName() + " " + outputs[0] + " "  + outputs[1] + " "  + outputs[2], true, true)
	
	return outputs
EndFunction

Function FixDeadActors(); applies the magic effect to currently loaded actors; not too accurate, especially outdoors some actors are missed
	if (isPapyrusUtilinstalled()) ;faster than SKSE function
		ObjectReference[] aActors = MiscUtil.ScanCellObjects(43, PlayerREF, 0.0) ; Actors = 0x3E; kNPC = 43; character = 62
		Int iIndex = aActors.Length 
		While iIndex
			iIndex -= 1
			Actor akActor = aActors[iIndex] as Actor
			if (akActor)
				ActorBase akActorBase = getProperActorBase(akActor)
				if (akActorBase)
					String actorName = akActorBase.getName()
					string[] textureAssignmentInfo = GetTextureAssignmentInfo(akActorBase)
					if (IsTextureEligible(textureAssignmentInfo) && akActor.IsDead())
						SetTextures(akActor, "Fix Dead " + actorName)	
					Endif
				EndIf
			EndIf
		EndWhile			
	Else	
		Cell kCell = PlayerREF.GetParentCell()	
		if (kCell != None)	
			Int iIndex = kCell.GetNumRefs(43) ; Actors = 0x3E; kNPC = 43; character = 62
			While iIndex
				iIndex -= 1
				Actor akActor = kCell.GetNthRef(iIndex, 43) as Actor
				if (akActor)
					ActorBase akActorBase = getProperActorBase(akActor)
					if (akActorBase)
						String actorName = akActorBase.getName()
						string[] textureAssignmentInfo = GetTextureAssignmentInfo(akActorBase)
						if (IsTextureEligible(textureAssignmentInfo) && akActor.IsDead())
							SetTextures(akActor, "Fix Dead " + actorName)
						EndIf	
					EndIf
				EndIf
			EndWhile
		EndIf	
	EndIf
EndFunction

Function RegisterForCustomEvents()
	int index = 0
	while index < TriggerEventNames.Length
	  RegisterForModEvent(TriggerEventNames[index], "OnSynthEBDSubscribedEvent")
	  index += 1
	endWhile
EndFunction

;; Adapted from original EBD Scripts
Function FixFaceTextureNew(Actor akActor, ActorBase akActorBase, TextureSet akActorTexSet, string ScriptEditorIdMode)										
	int index = akActorBase.GetIndexOfHeadPartByType(1) ; 1 is Face type, 3 is Hair
	HeadPart facePart = akActorBase.GetNthHeadPart(index)		
	string modName = getModName(akActorBase)
	if modName == "None"
		return
	endif
	string formIDString = getFormIDString(akActorBase)
	if (akActorTexSet)
		akActorTexSet.SetNthTexturePath(6, "Actors\\Character\\FaceGenData\\FaceTint\\" + modName + "\\" + formIDString + ".dds")
		if (ScriptEditorIdMode == "ModernSKSE")
			NetImmerse.SetNodeTextureSet(akActor, facePart.GetPartName(), akActorTexSet, false) ; GetPartName() only exists in SKSE >= 2.0.17; for older versions we need to use the somewhat buggy GetName()				
		elseIf (ScriptEditorIdMode == "PO3")
			NetImmerse.SetNodeTextureSet(akActor, PO3_SKSEFunctions.GetFormEditorID(facePart), akActorTexSet, false)
		else
			NetImmerse.SetNodeTextureSet(akActor, facePart.GetName(), akActorTexSet, false)
		EndIf
	EndIf	
EndFunction

