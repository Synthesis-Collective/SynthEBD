Scriptname SynthEBDHeadPartLoaderQuestScript extends Quest

;FAQ
;Q: Why are you writing a JMAP with FormKey strings that you have to parse yourself rather than using a JFormMap?
;A: JFormMap appears not to work in VR (yes, with JContainers VR). The JString.decodeFormStringToForm() function also fails in VR in my testing. The only way I could figure out to make this script VR-compatible was to parse the strings on my end.

;Q: Why are you writing a bunch of dictionary files to a directory rather than writing a single .json file?
;A: In my testing with headparts, JMaps containing >176 headpart entries will return NONE when JMap.GetNthKey is called on entry #177 and higher. Not sure if the 176 limit is hard, or if it depends on the size of each object in memory. 
;   I therefore had to split the headpart assignments into chunks of 176, and am doing the same for other JSON files to err on the side of caution.

import PSM_SynthEBD

GlobalVariable Property SynthEBDDataBaseLoaded Auto

Event OnInit()
	debug.Notification("SynthEBD Loading Headparts")
	LoadHeadPartDict("OnInit")
EndEvent

Function LoadHeadPartDict(string caller) ;caller is for debugging only
	SynthEBDDataBaseLoaded.SetValue(0)
	
	int fileCount = 0
	int validFileCount = 0
	int keyReadCount = 0
	
	string headPartDirectory = "Data/SynthEBD/HeadPartAssignments"
	int dictionariesContainer = JValue_readFromDirectory(headPartDirectory, ".json")
	JValue_retain(dictionariesContainer)
	int dictCount = JMap_count(dictionariesContainer)
	debug.Trace("SynthEBD: Found " + dictCount as string + " Dictionaries in " + headPartDirectory)
	
	while (fileCount < dictCount)
		string currentDictName = JMap_getNthKey(dictionariesContainer, fileCount)
		debug.Notification("SynthEBD: Reading headpart input file " + (fileCount + 1) as string)
		debug.Trace("SynthEBD: Reading input file " + (fileCount + 1) as string + " (" + currentDictName + ")")
		
		int assignmentDict = JMap_getObj(dictionariesContainer, currentDictName)
		if (assignmentDict)
			validFileCount += 1
			int keyIndex = 0
			int maxCount = JMap_count(assignmentDict)
			debug.Trace("SynthEBD: " + currentDictName + ": Contains " + maxCount as string + " entries.")
			
			while (keyIndex < maxCount)
				string currentNPCstr = JMap_getNthKey(assignmentDict, keyIndex)
				form currentNPC = SynthEBDCommonFuncs.FormKeyToForm(currentNPCstr, false)
				int headPartAssignments = JMap_getObj(assignmentDict, currentNPCstr)
				
				if (headPartAssignments)
					debug.Trace("JSON has HeadPart entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")")
					keyReadCount += 1
					AddHeadPartToDB(headPartAssignments, "Beard", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Brows", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Eyes", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Face", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Hair", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Misc", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Scars", currentNPC)
				else
					debug.Trace("JSON does not have HeadPart entry for Key " + keyIndex as string + ": " + currentNPCstr)
				endif

				currentNPCstr = JMap_nextKey(assignmentDict)
				keyIndex += 1
			endwhile
		else
			debug.Trace("SynthEBD: " + currentDictName + " is not a valid headpart dictionary.")
		endIf
		
		fileCount += 1
	endwhile

	if (fileCount > 0)
		int handle = ModEvent.Create("SynthEBD_HeadPartsReloaded")
		if (handle)
			ModEvent.Send(handle)
		endif
		debug.Notification("Loaded HeadPart Assignments")
		debug.Trace("SynthEBD: Loaded " + keyReadCount as string + " HeadPart assignments from " + validFileCount as string + " input files")
	else
		debug.Notification("Failed to loaded HeadPart Assignments")
		debug.Trace("SynthEBD: No readable HeadPartDict json files were found")
	endif
	SynthEBDDataBaseLoaded.SetValue(1)
	JValue_release(dictionariesContainer)
EndFunction

function AddHeadPartToDB(int headPartAssignments, string headPartType, form currentNPC)
	if (JMap_hasKey(headPartAssignments, headPartType))
		string headPartStr = JMap_getStr(headPartAssignments, headPartType)
		if (headPartStr)
			form headPartForm = SynthEBDCommonFuncs.FormKeyToForm(headPartStr, false)
			if (headPartForm)
				string destinationPath = ".SynthEBD.HeadPart." + headPartType
				JFormDB_setForm(currentNPC, destinationPath, headPartForm)
			endif
		endif
	endif
endfunction

