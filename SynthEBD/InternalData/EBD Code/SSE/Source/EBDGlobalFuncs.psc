scriptName EBDGlobalFuncs
{contains all Global Funcions}


;=====================================================================
; * General purpose
; 
;=====================================================================

ActorBase Function getProperActorBase(Actor akActor) Global;return proper actor base for leveled and non-leveled actors	
	if (akActor.GetLeveledActorBase() != (akActor.GetBaseObject() as ActorBase)) ;should be true for all leveled actors
		return akActor.GetLeveledActorBase().GetTemplate()				
	else
		return akActor.GetActorBase()						
	EndIf
EndFunction

;if actor is levelled and its template is the same as the non-levelled actorBase, then the body skin has to re-applied or it won't show (Skyrim bug)
bool Function isTemplateActorBase(Actor akActor) Global
	if (akActor.GetLeveledActorBase() != (akActor.GetBaseObject() as ActorBase)) ;should be true for all leveled actors
		ActorBase baseTemplate = akActor.GetLeveledActorBase().GetTemplate()	
		If (baseTemplate == akActor.getActorBase())
			return true
		EndIf
	EndIf
	Return false
EndFunction

String Function getEDID(Form fForm) Global ;returns a form's EDID
	if (fForm)
		String sForm = fForm as String
		;Form as String example: "[ScriptName <EditorID (FormID)>]"
		int iStart = StringUtil.Find(sForm, "<") + 1
		int iLength = StringUtil.Find(sForm, " (") - iStart
		return StringUtil.Substring(sForm, iStart, iLength)
	EndIf
	return "None"	
EndFunction

;returns the plugin name the form belongs to
String Function getModName(Form fForm) Global
	int formID = fForm.GetFormID() ; Get the formid to extract modindex
	if isLightMod(formID) ; and check whether the for formid comes from a light mod (i.e. FE plugin space)
		return Game.GetLightModName(getLightModIndex(fForm))
	else
		return Game.GetModName(Math.RightShift(formID, 24)) ; Shift right 24 bits to leave only modindex
	endIf
EndFunction

; checks if formid comes from a light mod by looking the index which is FE=254 for light plugins
bool Function isLightMod(int formID) Global
	if Math.RightShift(formID, 24) == 254
		Return True
	endif
	return False
EndFunction

;returns a float (used as a wait time) based on the distance between two objects. I.E. NPCs further from the player are processed later
float Function getWaitTimeByDistance(float Dist) Global
	float waitTime = 0.5 + (Dist/1000.0)
	if (waitTime > 5.0)
		waitTime = waitTime*0.65
	endIf
	if (waitTime > 5.0)
		return 5.0
	endIf
	return waitTime
EndFunction


float Function roundFloat(float flt) Global ;properly rounds a float
	int ceil = Math.Ceiling(flt)
	int floor = Math.Floor(flt)
	if ((ceil - flt) < (flt - floor))
		return ceil as float
	else
		return floor as float
	EndIf
EndFunction

Function DebugOutput(String output, bool bNotification = false) Global
	output = "EBD: " + output
	Debug.Trace(output)
	if (isPapyrusUtilInstalled())
		MiscUtil.PrintConsole(output)
	endif
	if (bNotification)
		Debug.Notification(output)
	EndIf
EndFunction

; function to return a decimal for given hex string
; taken from here: https://forums.nexusmods.com/index.php?/topic/1210601-papyrus-get-name-of-esp-a-base-form-belongs-to/#entry10079661
; could probably be done much better in a manner like this: https://forums.nexusmods.com/index.php?/topic/1210601-papyrus-get-name-of-esp-a-base-form-belongs-to/#entry40904755
; or: https://forums.nexusmods.com/index.php?/topic/8441118-convert-decimal-formid-to-hexadecimal/page-2#entry78086848
int Function HexToTen(String InputHTT) global
	Int Length2 = StringUtil.GetLength(InputHTT)
	Int LengthDone2 = 0
	Int SoFar2 = 0
	
	While ( LengthDone2 < Length2 )
		String CurrentDigit = StringUtil.GetNthChar(InputHTT, LengthDone2)
		Int CurrentTen
		
		If (CurrentDigit == "1")
			CurrentTen = 1
		ElseIf (CurrentDigit == "2")
			CurrentTen = 2
		ElseIf (CurrentDigit == "3")
			CurrentTen = 3
		ElseIf (CurrentDigit == "4")
			CurrentTen = 4
		ElseIf (CurrentDigit == "5")
			CurrentTen = 5
		ElseIf (CurrentDigit == "6")
			CurrentTen = 6
		ElseIf (CurrentDigit == "7")
			CurrentTen = 7
		ElseIf (CurrentDigit == "8")
			CurrentTen = 8
		ElseIf (CurrentDigit == "9")
			CurrentTen = 9
		ElseIf (CurrentDigit == "A")
			CurrentTen = 10
		ElseIf (CurrentDigit == "B")
			CurrentTen = 11
		ElseIf (CurrentDigit == "C")
			CurrentTen = 12
		ElseIf (CurrentDigit == "D")
			CurrentTen = 13
		ElseIf (CurrentDigit == "E")
			CurrentTen = 14
		ElseIf (CurrentDigit == "F")
			CurrentTen = 15
		Else
			CurrentTen= 0
		EndIf

		CurrentTen = CurrentTen*Math.pow(16, Length2-(LengthDone2+1)) as int
		SoFar2 += CurrentTen
		LengthDone2 += 1
	EndWhile
	
	return SoFar2
EndFunction

; returns the int index for a given form assuming that it is coming from light plugin
int function getLightModIndex(form fForm) global
	string formIDRaw = fForm as string
	int bracketIndex = StringUtil.Find(formIDRaw, "(", 0) ; a light form looks like this: FE087800
	string indexHex =  StringUtil.Substring(formIDRaw, bracketIndex + 3, 3) ; the index is between FE and the last three digits; in the above case it would be 087
	int modIndex = HexToTen(indexHex) ; convert the extracted hex index to int
	Return modIndex
endfunction

; returns the formid string needed for facegen textures
; the load index is overwritten with zeroes
; 5 zeroes for light plugins and 2 for regular ones
string function getFormIDString(form fForm) Global
	int formID = fForm.GetFormID()
	string formIDRaw = fForm as string
	int bracketIndex = StringUtil.Find(formIDRaw, "(", 0)
	if isLightMod(formID)
		return "00000" + StringUtil.Substring(formIDRaw, bracketIndex + 6, 3)
	else
		return "00" + StringUtil.Substring(formIDRaw, bracketIndex + 3, 6)
	endIf
endfunction

;=====================================================================
; * FACE TEXTURES
; 
;=====================================================================
;applies proper tintmask from npc record	
Function FixFaceTexture(Actor akActor, ActorBase akActorBase) Global										
	int index = akActorBase.GetIndexOfHeadPartByType(1) ; 1 is Face type, 3 is Hair
	HeadPart facePart = akActorBase.GetNthHeadPart(index)		
	string modName = getModName(akActorBase)
	string formIDString = getFormIDString(akActorBase)
	TextureSet akActorTexSet = akActorBase.GetFaceTextureSet()
	if (akActorTexSet)
		akActorTexSet.SetNthTexturePath(6, "Actors\\Character\\FaceGenData\\FaceTint\\" + modName + "\\" + formIDString + ".dds")
		NetImmerse.SetNodeTextureSet(akActor, facePart.GetPartName(), akActorTexSet, false) ; GetPartName() only exists in SKSE >= 2.0.17; for older versions we need to use the somewhat buggy GetName()				
		;DebugOutput("Found face texture for: " + akActorBase.getName() + ", " + formIDRaw + ": " + akActorBase.GetFaceTextureSet().GetNthTexturePath(0) + ", " +akActorBase.GetFaceTextureSet().GetNthTexturePath(6))
	EndIf	
	
EndFunction


;=====================================================================
; * INSTALLATION CHECKS
; 
;=====================================================================

bool Function isSKSEinstalled(bool bSilent = false) Global ;check SKSE install
	if (SKSE.GetVersionRelease() >= 48)
		return true
	EndIf	
	if (!bSilent)
		if (SKSE.GetVersionRelease() == 0)
			String Msg = "Error: You don't have SKSE installed. HelperScript will not work."
			DebugOutput(Msg, true)		
		elseif (SKSE.GetVersionRelease() < 48)
			String Msg1 = "Error: Your SKSE version is out of date. Install the newest version."
			String Msg2 = "Your version: " + SKSE.GetVersion() + ".0" + SKSE.GetVersionMinor() + "." + SKSE.GetVersionBeta() + "; Required: 1.7.1"
			DebugOutput(Msg1, true)
			DebugOutput(Msg2, true)		
		EndIf	
	EndIf	
	return false
EndFunction

bool Function isPapyrusUtilInstalled() Global ;check PapyrusUtil install
	if (PapyrusUtil.GetVersion() >= 32)
		return true
	EndIf
	return false
EndFunction

bool Function isJContainersInstalled(bool bSilent = false) Global ;check JC install
	if (JContainers.APIVersion() >= 3 && JContainers.featureVersion() >= 1)
		return true
	EndIf			
	if (!bSilent)
		if (JContainers.APIVersion() == 0)
			String Msg = "Error: You don't have JContainers installed. Headpart feature will not work."
			DebugOutput(Msg, true)
		elseif (JContainers.APIVersion() < 3 || JContainers.featureVersion() < 3)
			String Msg1 = "Error: Your JContainers version is out of date. Headpart feature will not work."
			DebugOutput(Msg1, true)
		EndIf	
	EndIf	
	return false
EndFunction

bool Function isUIExtUtilInstalled(bool bSilent = false) Global ;check UIExtensions install
	; enderal brings its own uiextensions
	If Game.GetModByName("Enderal - Forgotten Stories.esm") < 255
		return True
	ElseIf Game.GetFormFromFile(0xE05, "UIExtensions.esp")
		return True
	Else
		if (!bSilent)
			String Msg = "Error: You don't have UIExtensions installed. EBDCustomizer will not work."
			DebugOutput(Msg, true)
		EndIf
	Endif
	Return False
EndFunction

bool Function isRaceMenuInstalled(bool bVerbose = false) Global
	if (RaceMenuBase.GetScriptVersionRelease() >= 7 && NiOverride.GetScriptVersion() >= 6)
		return true
	Else
		String Msg = "Note: You don't have RaceMenu and NiOverride installed. Cannot open CosmeticMenu."
		DebugOutput(Msg, bVerbose)			
	EndIf
	Return False
EndFunction

