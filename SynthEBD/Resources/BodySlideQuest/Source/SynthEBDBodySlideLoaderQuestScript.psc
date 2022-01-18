Scriptname SynthEBDBodySlideLoaderQuestScript extends Quest

import PSM_SynthEBD

GlobalVariable Property loadingCompleted Auto

Event OnInit()
	;debug.MessageBox("Quest Script")
	LoadBodySlideDict("OnInit")
EndEvent

Function LoadBodySlideDict(string caller) ;caller is for debugging only
	;debug.MessageBox("Loading BodySlides")
	loadingCompleted.SetValue(0)
	int assignmentDict = JValue_readFromFile("Data/SynthEBD/BodySlideDict.json")
	int count = 0
	int maxCount = JFormMap_count(assignmentDict)
	while (count < maxCount)
		form currentNPC = JFormMap_getNthKey(assignmentDict, count)
		string assignment = JFormMap_getStr(assignmentDict, currentNPC)
		;debug.Trace("SynthEBD: Loaded bodyslide " + assignment + " for NPC " + currentNPC)
		JFormDB_setStr(currentNPC, ".SynthEBD.BodySlide", assignment)
		currentNPC = JFormMap_nextKey(assignmentDict)
		count += 1
	endwhile
	
	;debug.MessageBox("Finished loading from " + caller)
	;debug.Trace("assignmentDict has " + maxCount + " values")
	debug.Trace("SynthEBD: Loaded " + count as string + " Bodyslide assignments")
	loadingCompleted.SetValue(1)
	;debug.MessageBox("SynthEBD: BodySlides Loaded")
EndFunction
