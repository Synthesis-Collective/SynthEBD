Scriptname SynthEBDHeadPartLoaderQuestScript extends Quest

import PSM_SynthEBD

GlobalVariable Property SynthEBDDataBaseLoaded Auto

Event OnInit()
	debug.Notification("SynthEBD Loading Headparts")
	LoadHeadPartDict("OnInit")
EndEvent

Function LoadHeadPartDict(string caller) ;caller is for debugging only
	SynthEBDDataBaseLoaded.SetValue(0)
	
	int fileCount = 1
	bool fileFound = true
	int fileReadCount = 0
	int keyReadCount = 0
	while (fileFound)
		string inputFile = "Data/SynthEBD/HeadPartDict" + fileCount as string + ".json"
		int assignmentDict = JValue_readFromFile(inputFile)
		if (assignmentDict)
			fileReadCount += 1
			
			int keyIndex = 0
			int maxCount = JMap_count(assignmentDict)
			debug.Notification("SynthEBD: Reading headpart input file " + fileReadCount as string)
			debug.Trace("SynthEBD: Read input file " + fileCount as string + ": Contains " + maxCount as string + " entries.")
			while (keyIndex < maxCount)
				string currentNPCstr = JMap_getNthKey(assignmentDict, keyIndex)
				form currentNPC = SynthEBDCommonFuncs.FormKeyToForm(currentNPCstr, false)
				int headPartAssignments = JMap_getObj(assignmentDict, currentNPCstr)
				
				if (headPartAssignments)
					debug.Trace("JSON has entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")")
					keyReadCount += 1
					AddHeadPartToDB(headPartAssignments, "Beard", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Brows", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Eyes", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Face", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Hair", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Misc", currentNPC)
					AddHeadPartToDB(headPartAssignments, "Scars", currentNPC)
				else
					debug.Trace("JSON does not have entry for Key " + keyIndex as string + ": " + currentNPCstr)
				endif

				currentNPCstr = JMap_nextKey(assignmentDict)
				keyIndex += 1
			endwhile
		else
			fileFound = false
		endIf
		
		fileCount += 1
	endwhile

	if (fileCount > 0)
		debug.Notification("Loaded HeadPart Assignments")
		debug.Trace("SynthEBD: Loaded " + keyReadCount as string + " HeadPart assignments from " + fileReadCount as string + " input files")
	else
		debug.Notification("Failed to loaded HeadPart Assignments")
		debug.Trace("SynthEBD: No readable HeadPartDict json files were found")
	endif
	SynthEBDDataBaseLoaded.SetValue(1)
EndFunction

function AddHeadPartToDB(int headPartAssignments, string headPartType, form currentNPC)
	string headPartStr = JMap_getStr(headPartAssignments, headPartType)
	if (headPartStr)
		form headPartForm = SynthEBDCommonFuncs.FormKeyToForm(headPartStr, false)
		if (headPartForm)
			string destinationPath = ".SynthEBD.HeadPart." + headPartType
			JFormDB_setForm(currentNPC, destinationPath, headPartForm)
		endif
	endif
endfunction

