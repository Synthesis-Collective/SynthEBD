Scriptname SynthEBDBodySlideLoaderQuestScript extends Quest

import PSM_SynthEBD

GlobalVariable Property SynthEBDDataBaseLoaded Auto

Event OnInit()
	;debug.Notification("SynthEBD Loading BodySlides")
	LoadBodySlideDict("OnInit")
EndEvent

Function LoadBodySlideDict(string caller) ;caller is for debugging only
	SynthEBDDataBaseLoaded.SetValue(0)
	
	int fileCount = 1
	bool fileFound = true
	int fileReadCount = 0
	int keyReadCount = 0
	while (fileFound)
		string inputFile = "Data/SynthEBD/BodySlideDict" + fileCount as string + ".json"
		int assignmentDict = JValue_readFromFile(inputFile)
		if (assignmentDict)
			fileReadCount += 1
			int keyIndex = 0
			int maxCount = JMap_count(assignmentDict)
			;debug.Notification("SynthEBD: Reading BS dict " + fileReadCount as string)
			;debug.Trace("SynthEBD: Read BodySlide input file " + fileCount as string + ": Contains " + maxCount as string + " entries.")
			while (keyIndex < maxCount)
				string currentNPCstr = JMap_getNthKey(assignmentDict, keyIndex)
				form currentNPC = SynthEBDCommonFuncs.FormKeyToForm(currentNPCstr, false)
				string assignment = JMap_getStr(assignmentDict, currentNPCstr)
				if (assignment)
					;debug.Trace("JSON has entry for Key " + keyIndex as string + ": " + currentNPCstr + " (" + currentNPC + ")")
					keyReadCount += 1
					AddBodySlideToDB(assignment, currentNPC)
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
		;debug.Notification("Loaded BodySlide Assignments")
		debug.Trace("SynthEBD: Loaded " + keyReadCount as string + " BodySlide assignments from " + fileReadCount as string + " input files")
	else
		;debug.Notification("Failed to loaded BodySlide Assignments")
		debug.Trace("SynthEBD: No BodySlide json files found")
	endif
	SynthEBDDataBaseLoaded.SetValue(1)
EndFunction

Function AddBodySlideToDB(string bodyslide, form currentNPC)
	string destinationPath = ".SynthEBD.BodySlide"
	JFormDB_setStr(currentNPC, destinationPath, bodyslide)
EndFunction
