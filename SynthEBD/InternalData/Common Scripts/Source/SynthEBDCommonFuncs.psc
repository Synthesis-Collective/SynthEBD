ScriptName SynthEBDCommonFuncs

import SynthEBDcLib
import EBDGlobalFuncs
import PSM_SynthEBD

;FAQ
;Q: Why are you writing a JMAP with FormKey strings that you have to parse yourself rather than using a JFormMap?
;A: JFormMap appears not to work in VR (yes, with JContainers VR). The JString.decodeFormStringToForm() function also fails in VR in my testing. The only way I could figure out to make this script VR-compatible was to parse the strings on my end.

Function ReloadSynthEBDDataBase(string jsonPath, string dbSubPath, bool verbose, string entryType) global
	if (jContainers.fileExistsAtPath(jsonPath))
		int containerHandle = JValue_readFromFile(jsonPath)
		if (containerHandle)
			JValue_retain(containerHandle)
			JDB_solveObjSetter(dbSubPath, containerHandle, true)
			VerboseLogger("SynthEBD: Loaded " + JMap_count(containerHandle) as string + " " + entryType + " assignments", verbose, true)
			JValue_release(containerHandle)
		endif

		string eventName = "SynthEBD_" + entryType + "sReloaded"
		int handle = ModEvent.Create(eventName)
		if (handle)
			VerboseLogger("SynthEBD: Sending " + eventName, verbose, false)
			ModEvent.Send(handle)
		endif
	else
		VerboseLogger("SynthEBD: Can't find expected json file at " + jsonPath, verbose, true)
	endif	
EndFunction

string Function FormKeyFromForm(form fForm, bool bJCPathCompatibility) global
	if fForm
		string formKey = StringUtil.subString(getFormIDString(fForm), 2) + ":" + getModName(fForm)
		if (bJCPathCompatibility)
			formKey = strReplace(formKey, ".",  "*")
		endif
		return formKey
	else
		return "None"
	endif
EndFunction

form Function FormKeyToForm(string formKeyStr, bool bJCPathCompatibility, bool bVerbose) global
	if (bJCPathCompatibility)
		formKeyStr = StrReplace(formKeyStr, ".", "*")
	endIf
	
	string[] split = StringUtil.Split(formKeyStr, ":")
	if (split.Length != 2 || StringUtil.GetLength(split[0]) != 6)
		if (bVerbose)
			debug.Trace("FormKeyToForm: " + formKeyStr + " is not a FormKey string")
		endif
		return None
	endif
	
	string modStr = split[1]
	string subID = split[0]
	form output = cGetForm(0, subID, modStr)
	
	if (bVerbose)
		if (output)
			debug.Trace("FormKeyToForm( " + formKeyStr + "): Found " + subID + " in " + modStr)
		else
			debug.Trace("FormKeyToForm( " + formKeyStr + "): Did not find " + subID + " in " + modStr)
		endif
	endif
	return output
EndFunction

Function VerboseLogger(string logStr, bool verbose, bool bNotifyInGame) global
	if (verbose)
		debug.Trace(logStr)
		if (bNotifyInGame)
			debug.Notification(logStr)
		endIf
	endif
EndFunction

string Function StrReplace(string target, string toReplace, string replaceWith) global
	string[] parts = StringUtil.split(target, toReplace)
	string output = ""
	int i = 0
	while (i < parts.Length)
		output += parts[i]
		if (i < parts.Length - 1)
			output += replaceWith
		endIf
		i += 1
	endWhile
	return output
endFunction

;DEPRECATED
;FAQ
;Q: Why are you writing a JMAP with FormKey strings that you have to parse yourself rather than using a JFormMap?
;A: JFormMap appears not to work in VR (yes, with JContainers VR). The JString.decodeFormStringToForm() function also fails in VR in my testing. The only way I could figure out to make this script VR-compatible was to parse the strings on my end.

;Q: Why are you writing a bunch of dictionary files to a directory rather than writing a single .json file?
;A: In my testing with headparts, JMaps containing >176 headpart entries will return NONE when JMap.GetNthKey is called on entry #177 and higher. Not sure if the 176 limit is hard, or if it depends on the size of each object in memory. 
;   I therefore had to split the headpart assignments into chunks of 176, and am doing the same for other JSON files to err on the side of caution.

Function LoadJFormKeyMapsToJFormDB(string jsonDirectory, string dbSubPath, bool verbose, string entryType, string dataType) global
	int fileCount = 0
	int validFileCount = 0
	int keyReadCount = 0
	
	int dictionariesContainer = JValue_readFromDirectory(jsonDirectory, ".json")
	JValue_retain(dictionariesContainer)
	
	int dictCount = JMap_count(dictionariesContainer)
	VerboseLogger("SynthEBD: Found " + dictCount as string + " Dictionaries in " + jsonDirectory, verbose, false)
	
	while (fileCount < dictCount)
		string currentDictName = JMap_getNthKey(dictionariesContainer, fileCount)
		VerboseLogger("SynthEBD: Reading input file " + (fileCount + 1) as string + " (" + currentDictName + ")", verbose, true)
		
		int assignmentDict = JMap_getObj(dictionariesContainer, currentDictName)
		if (assignmentDict)
			validFileCount += 1
			int keyIndex = 0
			int maxCount = JMap_count(assignmentDict)
			VerboseLogger("SynthEBD: " + currentDictName + ": Contains " + maxCount as string + " entries.", verbose, false)
			
			while (keyIndex < maxCount)
				string currentNPCstr = JMap_getNthKey(assignmentDict, keyIndex)
				form currentNPC = SynthEBDCommonFuncs.FormKeyToForm(currentNPCstr, false, false)
				if (currentNPC)
					if (dataType == "obj")
						keyReadCount += SetFormDBObj(assignmentDict, currentNPCstr, currentNPC, entryType, dbSubPath, keyIndex, verbose)
					elseif (dataType == "str")
						keyReadCount += SetFormDBStr(assignmentDict, currentNPCstr, currentNPC, entryType, dbSubPath, keyIndex, verbose)
					elseif (dataType == "int")
						keyReadCount += SetFormDBInt(assignmentDict, currentNPCstr, currentNPC, entryType, dbSubPath, keyIndex, verbose)
					elseif (dataType == "flt")
						keyReadCount += SetFormDBFlt(assignmentDict, currentNPCstr, currentNPC, entryType, dbSubPath, keyIndex, verbose)
					elseif (dataType == "form")
						keyReadCount += SetFormDBForm(assignmentDict, currentNPCstr, currentNPC, entryType, dbSubPath, keyIndex, verbose)
					else
						VerboseLogger("SynthEBD: LoadJFormKeyMapsToJFormDB was called with unrecognized data type: " + dataType, verbose, false)
					endif
				else
					VerboseLogger("SynthEBD: NPC " + currentNPCstr + " was not found in the current load order", verbose, false)
				endIf				
				currentNPCstr = JMap_nextKey(assignmentDict)
				keyIndex += 1
			endwhile
		else
			VerboseLogger("SynthEBD: " + currentDictName + " is not a valid " + entryType + " dictionary.", verbose, false)
		endIf
		
		fileCount += 1
	endwhile

	if (fileCount > 0)
		string eventName = "SynthEBD_" + entryType + "sReloaded"
		int handle = ModEvent.Create(eventName)
		if (handle)
			ModEvent.Send(handle)
		endif
		VerboseLogger("SynthEBD: Loaded " + keyReadCount as string + " " + entryType + " assignments from " + validFileCount as string + " input files", verbose, true)
	else
		VerboseLogger("SynthEBD: No readable BodySlideDict json files were found", verbose, true)
	endif
		
	JValue_release(dictionariesContainer)
EndFunction

int Function SetFormDBObj(int assignmentDict, string currentNPCstr, form currentNPC, string entryType, string dbSubPath, int keyIndex, bool verbose) global
	int entryContainer = JMap_getObj(assignmentDict, currentNPCstr)
	if (entryContainer)
		VerboseLogger("SynthEBD: JSON has " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")", verbose, false)
		if (JFormDB_solveObjSetter(currentNPC, dbSubPath, entryContainer, true))
			;VerboseLogger("SynthEBD: Set object for " + currentNPC as string + " at path " + dbSubPath + " to "+ entryContainer as string, verbose, false)
		else
			VerboseLogger("SynthEBD: Could not set object for " + currentNPC as string + " at path " + dbSubPath + " to " + entryContainer as string, verbose, false)
		endif
		return 1
	else
		VerboseLogger("SynthEBD: JSON does not have " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr, verbose, false)
		return 0
	endIf
EndFunction

int Function SetFormDBStr(int assignmentDict, string currentNPCstr, form currentNPC, string entryType, string dbSubPath, int keyIndex, bool verbose) global
	string entry = JMap_getStr(assignmentDict, currentNPCstr)
	if (entry)
		VerboseLogger("SynthEBD: JSON has " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")", verbose, false)
		if (JFormDB_solveStrSetter(currentNPC, dbSubPath, entry, true))
			;VerboseLogger("SynthEBD: Set object for " + currentNPC as string + " at path " + dbSubPath + " to "+ entry, verbose, false)
		else
			VerboseLogger("SynthEBD: Could not set string for " + currentNPC as string + " at path " + dbSubPath + " to " + entry, verbose, false)
		endif
		return 1
	else
		VerboseLogger("SynthEBD: JSON does not have " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr, verbose, false)
		return 0
	endIf
EndFunction

int Function SetFormDBInt(int assignmentDict, string currentNPCstr, form currentNPC, string entryType, string dbSubPath, int keyIndex, bool verbose) global
	int entry = JMap_getInt(assignmentDict, currentNPCstr)
	if (entry)
		VerboseLogger("SynthEBD: JSON has " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")", verbose, false)
		if (JFormDB_solveIntSetter(currentNPC, dbSubPath, entry, true))
			;VerboseLogger("SynthEBD: Set object for " + currentNPC as string + " at path " + dbSubPath + " to "+ entry as string, verbose, false)
		else
			VerboseLogger("SynthEBD: Could not set Int for " + currentNPC as string + " at path " + dbSubPath + " to " + entry as string, verbose, false)
		endif
		return 1
	else
		VerboseLogger("SynthEBD: JSON does not have " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr, verbose, false)
		return 0
	endIf
EndFunction

int Function SetFormDBFlt(int assignmentDict, string currentNPCstr, form currentNPC, string entryType, string dbSubPath, int keyIndex, bool verbose) global
	float entry = JMap_getFlt(assignmentDict, currentNPCstr)
	if (entry)
		VerboseLogger("SynthEBD: JSON has " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")", verbose, false)
		if (JFormDB_solveFltSetter(currentNPC, dbSubPath, entry, true))
			;VerboseLogger("SynthEBD: Set object for " + currentNPC as string + " at path " + dbSubPath + " to "+ entry as string, verbose, false)
		else
			VerboseLogger("SynthEBD: Could not set Float for " + currentNPC as string + " at path " + dbSubPath + " to " + entry as string, verbose, false)
		endif
		return 1
	else
		VerboseLogger("SynthEBD: JSON does not have " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr, verbose, false)
		return 0
	endIf
EndFunction

int Function SetFormDBForm(int assignmentDict, string currentNPCstr, form currentNPC, string entryType, string dbSubPath, int keyIndex, bool verbose) global
	form entry = JMap_getForm(assignmentDict, currentNPCstr)
	if (entry)
		VerboseLogger("SynthEBD: JSON has " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")", verbose, false)
		if (JFormDB_solveFormSetter(currentNPC, dbSubPath, entry, true))
			;VerboseLogger("SynthEBD: Set object for " + currentNPC as string + " at path " + dbSubPath + " to "+ entry as string, verbose, false)
		else
			VerboseLogger("SynthEBD: Could not set Form for " + currentNPC as string + " at path " + dbSubPath + " to " + entry as string, verbose, false)
		endif
		return 1
	else
		VerboseLogger("SynthEBD: JSON does not have " + entryType + " entry for Key " + keyIndex as string + ": " + currentNPCstr, verbose, false)
		return 0
	endIf	
EndFunction

Function ClearActorEffect(Actor akAktor, MagicEffect effectToClear, Spell parentSpell) global
	If (akAktor.HasMagicEffect(effectToClear))
		akAktor.DispelSpell(parentSpell)
	EndIf
EndFunction

string function GetActorBaseName(ActorBase akBase) global
	string actorName = akBase.GetName()
	if actorName == ""
		actorName = "Unnamed"
	endif
	return actorName
endFunction