Scriptname SynthEBDHeadPartLoaderPAScript extends ReferenceAlias

SynthEBDHeadPartLoaderQuestScript Property QuestScript Auto

Event OnPlayerLoadGame()
	debug.Notification("Calling LoadHeadPartDict")
	QuestScript.LoadHeadPartDict("PlayerAlias")
EndEvent