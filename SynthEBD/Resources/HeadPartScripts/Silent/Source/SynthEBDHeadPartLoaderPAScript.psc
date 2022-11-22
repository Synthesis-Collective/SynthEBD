Scriptname SynthEBDHeadPartLoaderPAScript extends ReferenceAlias

SynthEBDHeadPartLoaderQuestScript Property QuestScript Auto

Event OnPlayerLoadGame()
	QuestScript.LoadHeadPartDict("PlayerAlias")
EndEvent