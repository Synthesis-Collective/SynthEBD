ScriptName SynthEBDCommonFuncs

import SynthEBDcLib

form Function FormKeyToForm(string formKeyStr, bool bVerbose) global
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

Function ClearActorEffect(Actor akAktor, MagicEffect effectToClear, Spell parentSpell) global
	If (akAktor.HasMagicEffect(effectToClear))
		akAktor.DispelSpell(parentSpell)
	EndIf
EndFunction