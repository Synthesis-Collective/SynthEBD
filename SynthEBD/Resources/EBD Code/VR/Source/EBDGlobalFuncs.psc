;/ Decompiled by Champollion V1.0.1
Source   : EBDGlobalFuncs.psc
Modified : 2020-05-02 14:48:28
Compiled : 2020-05-02 14:48:32
User     : Don
Computer : LEMACHINE
/;
scriptName EBDGlobalFuncs
{contains all Global Funcions}

;-- Properties --------------------------------------

;-- Variables ---------------------------------------

;-- Functions ---------------------------------------

Int function getLightModIndex(form fForm) global

	String formIDRaw = fForm as String
	Int bracketIndex = stringutil.Find(formIDRaw, "(", 0)
	String indexHex = stringutil.Substring(formIDRaw, bracketIndex + 3, 3)
	Int modIndex = EBDGlobalFuncs.HexToTen(indexHex)
	return modIndex
endFunction

Bool function isTemplateActorBase(Actor akActor) global

	if akActor.GetLeveledActorBase() != akActor.GetBaseObject() as ActorBase
		ActorBase baseTemplate = akActor.GetLeveledActorBase().GetTemplate()
		if baseTemplate == akActor.GetActorBase()
			return true
		endIf
	endIf
	return false
endFunction

function DebugOutput(String output, Bool bNotification) global

	output = "EBD: " + output
	debug.Trace(output, 0)
	if EBDGlobalFuncs.isPapyrusUtilInstalled()
		miscutil.PrintConsole(output)
	endIf
	if bNotification
		debug.Notification(output)
	endIf
endFunction

Bool function isUIExtUtilInstalled(Bool bSilent) global

	if game.GetFormFromFile(3589, "UIExtensions.esp")
		return true
	elseIf !bSilent
		String Msg = "Error: You don't have UIExtensions installed. EBDCustomizer will not work."
		EBDGlobalFuncs.DebugOutput(Msg, true)
	endIf
	return false
endFunction

Bool function isSKSEinstalled(Bool bSilent) global

	if skse.GetVersionRelease() >= 48
		return true
	endIf
	if !bSilent
		if skse.GetVersionRelease() == 0
			String Msg = "Error: You don't have SKSE installed. HelperScript will not work."
			EBDGlobalFuncs.DebugOutput(Msg, true)
		elseIf skse.GetVersionRelease() < 48
			String Msg1 = "Error: Your SKSE version is out of date. Install the newest version."
			String Msg2 = "Your version: " + skse.GetVersion() as String + ".0" + skse.GetVersionMinor() as String + "." + skse.GetVersionBeta() as String + "; Required: 1.7.1"
			EBDGlobalFuncs.DebugOutput(Msg1, true)
			EBDGlobalFuncs.DebugOutput(Msg2, true)
		endIf
	endIf
	return false
endFunction

ActorBase function getProperActorBase(Actor akActor) global

	if akActor.GetLeveledActorBase() != akActor.GetBaseObject() as ActorBase
		return akActor.GetLeveledActorBase().GetTemplate()
	else
		return akActor.GetActorBase()
	endIf
endFunction

Bool function isJContainersInstalled(Bool bSilent) global

	if jcontainers.APIVersion() >= 3 && jcontainers.featureVersion() >= 1
		return true
	endIf
	if !bSilent
		if jcontainers.APIVersion() == 0
			String Msg = "Error: You don't have JContainers installed. Headpart feature will not work."
			EBDGlobalFuncs.DebugOutput(Msg, true)
		elseIf jcontainers.APIVersion() < 3 || jcontainers.featureVersion() < 3
			String Msg1 = "Error: Your JContainers version is out of date. Headpart feature will not work."
			EBDGlobalFuncs.DebugOutput(Msg1, true)
		endIf
	endIf
	return false
endFunction

String function getEDID(form fForm) global

	if fForm
		String sForm = fForm as String
		Int iStart = stringutil.Find(sForm, "<", 0) + 1
		Int iLength = stringutil.Find(sForm, " (", 0) - iStart
		return stringutil.Substring(sForm, iStart, iLength)
	endIf
	return "None"
endFunction

function FixFaceTexture(Actor akActor, ActorBase akActorBase) global

	Int index = akActorBase.GetIndexOfHeadPartByType(1)
	headpart facePart = akActorBase.GetNthHeadPart(index)
	String modName = EBDGlobalFuncs.getModName(akActorBase as form)
	String formIDString = EBDGlobalFuncs.getFormIDString(akActorBase as form)
	textureset akActorTexSet = akActorBase.GetFaceTextureSet()
	if akActorTexSet
		akActorTexSet.SetNthTexturePath(6, "Actors\\Character\\FaceGenData\\FaceTint\\" + modName + "\\" + formIDString + ".dds")
		netimmerse.SetNodeTextureSet(akActor as objectreference, facePart.GetName(), akActorTexSet, false)
	endIf
endFunction

Bool function isLightMod(Int formID) global

	if math.RightShift(formID, 24) == 254
		return true
	endIf
	return false
endFunction

Float function roundFloat(Float flt) global

	Int ceil = math.Ceiling(flt)
	Int floor = math.floor(flt)
	if ceil as Float - flt < flt - floor as Float
		return ceil as Float
	else
		return floor as Float
	endIf
endFunction

String function getModName(form fForm) global

	Int formID = fForm.GetFormID()
	if EBDGlobalFuncs.isLightMod(formID)
		return game.GetLightModName(EBDGlobalFuncs.getLightModIndex(fForm))
	else
		return game.getModName(math.RightShift(formID, 24))
	endIf
endFunction

Bool function isRaceMenuInstalled(Bool bVerbose) global

	if racemenubase.GetScriptVersionRelease() >= 7 && nioverride.GetScriptVersion() >= 6
		return true
	else
		String Msg = "Note: You don't have RaceMenu and NiOverride installed. Cannot open CosmeticMenu."
		EBDGlobalFuncs.DebugOutput(Msg, bVerbose)
	endIf
	return false
endFunction

Float function getWaitTimeByDistance(Float Dist) global

	Float waitTime = 0.500000 + Dist / 1000.00
	if waitTime > 5.00000
		waitTime *= 0.650000
	endIf
	if waitTime > 5.00000
		return 5.00000
	endIf
	return waitTime
endFunction

Int function HexToTen(String InputHTT) global

	Int Length2 = stringutil.GetLength(InputHTT)
	Int LengthDone2 = 0
	Int SoFar2 = 0
	while LengthDone2 < Length2
		Int CurrentTen
		String CurrentDigit = stringutil.GetNthChar(InputHTT, LengthDone2)
		if CurrentDigit == "1"
			CurrentTen = 1
		elseIf CurrentDigit == "2"
			CurrentTen = 2
		elseIf CurrentDigit == "3"
			CurrentTen = 3
		elseIf CurrentDigit == "4"
			CurrentTen = 4
		elseIf CurrentDigit == "5"
			CurrentTen = 5
		elseIf CurrentDigit == "6"
			CurrentTen = 6
		elseIf CurrentDigit == "7"
			CurrentTen = 7
		elseIf CurrentDigit == "8"
			CurrentTen = 8
		elseIf CurrentDigit == "9"
			CurrentTen = 9
		elseIf CurrentDigit == "A"
			CurrentTen = 10
		elseIf CurrentDigit == "B"
			CurrentTen = 11
		elseIf CurrentDigit == "C"
			CurrentTen = 12
		elseIf CurrentDigit == "D"
			CurrentTen = 13
		elseIf CurrentDigit == "E"
			CurrentTen = 14
		elseIf CurrentDigit == "F"
			CurrentTen = 15
		else
			CurrentTen = 0
		endIf
		CurrentTen *= math.pow(16 as Float, (Length2 - LengthDone2 + 1) as Float) as Int
		SoFar2 += CurrentTen
		LengthDone2 += 1
	endWhile
	return SoFar2
endFunction

String function getFormIDString(form fForm) global

	Int formID = fForm.GetFormID()
	String formIDRaw = fForm as String
	Int bracketIndex = stringutil.Find(formIDRaw, "(", 0)
	if EBDGlobalFuncs.isLightMod(formID)
		return "00000" + stringutil.Substring(formIDRaw, bracketIndex + 6, 3)
	else
		return "00" + stringutil.Substring(formIDRaw, bracketIndex + 3, 6)
	endIf
endFunction

Bool function isPapyrusUtilInstalled() global

	if papyrusutil.GetVersion() >= 32
		return true
	endIf
	return false
endFunction

function onEndState()
{Event received when this state is switched away from}

	; Empty function
endFunction

; Skipped compiler generated GotoState

; Skipped compiler generated GetState

function onBeginState()
{Event received when this state is switched to}

	; Empty function
endFunction
