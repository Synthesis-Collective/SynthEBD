Scriptname SynthEBDBodySlideLoaderPAScript extends ReferenceAlias

SynthEBDBodySlideLoaderQuestScript Property QuestScript Auto

Event OnPlayerLoadGame()
	;debug.MessageBox("Calling LoadBodySlideDict")
	QuestScript.LoadBodySlideDict("PlayerAlias")
EndEvent