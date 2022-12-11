Scriptname SynthEBDHeadPartLoaderPAScript extends ReferenceAlias

SynthEBDHeadPartLoaderQuestScript Property QuestScript Auto

Event OnPlayerLoadGame()
	;debug.MessageBox("Player Alias Calling LoadHeadPartDict") ; for debugging only
	QuestScript.LoadHeadPartDict("PlayerAlias")
EndEvent