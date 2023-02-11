scriptName SynthEBDFaceTextureScript extends ActiveMagicEffect
{applies the face texture which is set up in the npc record, changes headparts for NPCs}

import EBDHelperScript; still needed for FixDeadActors
import EBDGlobalFuncs ;direct access to the global funcs
import EBDHeadPartFuncs ;direct access to the headpart funcs
import SynthEBDCommonFuncs

Keyword property SynthEBDFaceTextureKeyword Auto
GlobalVariable Property FaceTextureScriptActive Auto
GlobalVariable Property VerboseMode Auto
Actor property PlayerREF Auto
string[] property TriggerEventNames auto

State Busy

EndState

EVENT OnEffectStart(Actor akTarget, Actor akActor)
	RegisterForNiNodeUpdate() ; Registers for the NiNodeUpdate event
	RegisterForCustomEvents()
	SetTextures(akActor, "OnEffectStart")
EndEvent

Event OnNiNodeUpdate(ObjectReference akActorRef)
	Actor akActor = akActorRef as Actor
	if (akActor)
		SetTextures(akActor, "OnNiNodeUpdate")
	else
		VerboseLogger("SynthEBD: " + akActorRef  + "received an NiNodeUpdate but is not an actor", VerboseMode.GetValue(), true)	
	EndIf
EndEvent

Event OnSynthEBDSubscribedEvent(string eventName, string strArg, float numArg, Form sender)
	actor akActor = GetTargetActor()
	SetTextures(akActor, eventName)
EndEvent

function SetTextures(Actor akActor, string eventName)
	if (akActor && isSKSEinstalled() && FaceTextureScriptActive.getValue() == 1)
		If (akActor == PlayerREF)
			FixDeadActors()		
		EndIf
		ActorBase akActorBase = getProperActorBase(akActor)
		if (akActorBase && akActorBase.hasKeyword(SynthEBDFaceTextureKeyword))
			VerboseLogger("SynthEBD: " + akActorBase.GetName() + ": " + eventName  + " called  FaceTexture script", VerboseMode.GetValue(), true)	
		
			GoToState("Busy")
			Utility.Wait(getWaitTimeByDistance(PlayerREF.GetDistance(akActor)))		
			VerboseLogger("SynthEBD: Check Actor: " + akActorBase.getName() + "; Race: " + akActorBase.getRace().getName() + "; Distance: " + PlayerREF.GetDistance(akActor) as string, VerboseMode.GetValue(), false)																															

			ActorBase akTemplate = akActorBase.getTemplate()
			if (isTemplateActorBase(akActor))
				Armor arSkin = akActorBase.getSkin()
				if (arSkin)
					akActor.getLeveledActorBase().setSkin(arSkin)
					VerboseLogger("SynthEBD: Found actor to fix body skin for. Name: " + akActorBase.GetName()  + ", Base:" + akActorBase as String + ", NameLvl: " + akActor.getLeveledActorBase().getName() + ", LvlBase: " + akActor.getLeveledActorBase() as string, VerboseMode.GetValue(), false)	
				EndIf	
			EndIf
			
			if (akTemplate)
				FixFaceTexture(akActor, akTemplate)
				VerboseLogger("SynthEBD: Found template for: Name: " + akActorBase.GetName()  + ", Base:" + akActorBase as String + ", NameLvl: " + akActor.getLeveledActorBase().getName() + ", LvlBase: " + akActor.getLeveledActorBase() as String + "; Template" + akTemplate.GetName() + "; " + akTemplate as string, VerboseMode.GetValue(), false)	
			else
				FixFaceTexture(akActor, akActorBase)
			EndIf
			
			GoToState("")
			VerboseLogger("SynthEBD: " + akActorBase.GetName()  + " got the following FaceTextureSet: " + akActorBase.getFaceTextureSet() as String + "; Skin: " + akActorBase.getSkin() as String, VerboseMode.GetValue(), true)																																				
		EndIf					
	EndIf
EndFunction

Function FixDeadActors(); applies the magic effect to currently loaded dead actors; not too accurate, especially outdoors some actors are missed
	if (isPapyrusUtilinstalled()) ;faster than SKSE function
		ObjectReference[] aActors = MiscUtil.ScanCellObjects(43, PlayerREF, 0.0) ; Actors = 0x3E; kNPC = 43; character = 62
		Int iIndex = aActors.Length 
		While iIndex
			iIndex -= 1
			Actor akActor = aActors[iIndex] as Actor
			String actorName = getProperActorBase(akActor).getName()
			if (akActor.hasKeyword(SynthEBDFaceTextureKeyword))
				if (akActor.isDead())
					SetTextures(akActor, "FixDeadActors via dead actor " + actorName)
				EndIf	
			Endif	
		EndWhile			
	Else	
		Cell kCell = PlayerREF.GetParentCell()	
		if (kCell != None)	
			Int iIndex = kCell.GetNumRefs(43) ; Actors = 0x3E; kNPC = 43; character = 62
			While iIndex
				iIndex -= 1
				Actor akActor = kCell.GetNthRef(iIndex, 43) as Actor
				String actorName = getProperActorBase(akActor).getName()
				if (akActor.hasKeyword(SynthEBDFaceTextureKeyword))
					if (akActor.isDead())
						SetTextures(akActor, "FixDeadActors via dead actor " + actorName)
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