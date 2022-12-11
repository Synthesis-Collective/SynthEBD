Scriptname SynthEBDBodySlideLoaderPAScript extends ReferenceAlias

SynthEBDBodySlideLoaderQuestScript Property QuestScript Auto

Event OnPlayerLoadGame()
	;debug.MessageBox("Player Alias Calling LoadBodySlideDict") ; for debugging only
	QuestScript.LoadBodySlideDict("PlayerAlias")
EndEvent