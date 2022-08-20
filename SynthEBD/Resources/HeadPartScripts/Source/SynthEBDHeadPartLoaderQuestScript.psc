Scriptname SynthEBDHeadPartLoaderQuestScript extends Quest

import PSM_SynthEBD
import PapyrusUtil

GlobalVariable Property loadingCompleted Auto

Event OnInit()
	debug.Notification("HeadParts OnInit")
	LoadHeadPartDict("OnInit")
EndEvent

Function LoadHeadPartDict(string caller) ;caller is for debugging only
	;debug.MessageBox("Loading HeadParts")
	loadingCompleted.SetValue(0)
	int assignmentDict = JValue_readFromFile("Data/SynthEBD/HeadPartDict.json")
	if (assignmentDict)
		debug.Trace("SynthEBD: HeadPart Dict Read")
	else
		debug.Trace("SynthEBD: Failed to read HeadPart Dict")
	endIf
	int count = 0
	int maxCount = JMap_count(assignmentDict)
	debug.Trace("SynthEBD: NPCs with head parts Count = " + maxCount as string)
	while (count < maxCount)
		string currentNPCstr = JMap_getNthKey(assignmentDict, count)
		;debug.Trace("Key: " + currentNPCstr)
		;form currentNPC = JString.decodeFormStringToForm(currentNPCstr) doesn't seem to work in VR
		form currentNPC = FormKeyToForm(currentNPCstr)
		;debug.Trace("Form from key: " + currentNPC)
		
		int headPartAssignments = JMap_getObj(assignmentDict, currentNPCstr)
		
		;int headPartCount = JMap_count(headPartAssignments)
		;debug.Trace("head part count for " + currentNPC + ": " + headPartCount as string)
		
		;string hairStr = JMap_getStr(headPartAssignments, "Hair")
		;form hair = FormKeyToForm(HairStr)
		;JFormDB_setForm(currentNPC, ".SynthEBD.HeadPart.Hair", hair)
		
		AddHeadPartToDB(headPartAssignments, "Hair", currentNPC)
		
		;debug.Trace("SynthEBD: hair for " + currentNPCstr + ": " + hairStr)
		;debug.Notification("SynthEBD HP: " + currentNPC + ": " + hair)
		;debug.Trace("SynthEBD: Loaded hair for: " + currentNPC + ": " + hair)
		currentNPCstr = JMap_nextKey(assignmentDict)
		count += 1
	endwhile
	
	;debug.MessageBox("Finished loading from " + caller)
	;debug.Trace("assignmentDict has " + maxCount + " values")
	debug.Notification("Loaded HeadPart Assignments")
	debug.Trace("SynthEBD: Loaded " + count as string + " HeadPart assignments")
	loadingCompleted.SetValue(1)
	;debug.MessageBox("SynthEBD: HeadParts Loaded")
EndFunction

function AddHeadPartToDB(int headPartAssignments, string headPartType, form currentNPC)
	string headPartStr = JMap_getStr(headPartAssignments, headPartType)
	if (headPartStr)
		form headPartForm = FormKeyToForm(headPartStr)
		if (headPartForm)
			string destinationPath = ".SynthEBD.HeadPart." + headPartType
			JFormDB_setForm(currentNPC, destinationPath, headPartForm)
		endif
	endif
endfunction

form Function FormKeyToForm(string formKeyStr)
	string[] split = StringSplit(formKeyStr, ":") ; PapyrusUtil function
	if (split.Length != 2)
		return None
	endif
	
	string modStr = split[1]
	string subID = split[0]
	form output = cGetForm(0, subID, modStr)
	if (output)
		debug.Trace("SynthEBD: Found " + subID + " in " + modStr)
	else
		debug.Trace("SynthEBD: Did not find " + subID + " in " + modStr)
	endif
	
	return output
EndFunction

;string Function GetFormSignature(string formString)
;	string[] split1 = StringSplit(formString, "(")
;	if split1.Length != 2
;		return ""
;	endif
;	string[] split2 = StringSplit(split1[1], ")")
;	if split2.Length != 2
;		return ""
;	endif
;	string HexID = split2[0]
;	return cGetHexSubID(0, HexID, None) ;Clib function
;EndFunction

;====== Query/Analysis
  ;>>> resolve form
Form    function cGetForm(Int decForm, String hexForm = "", String modName = "") global
  {Requirements: None}
  Form returnForm
  if !decForm && !hexForm
    cErrInvalidArg("cGetForm", "!decForm && !hexForm")
  else
    if hexForm
      decForm = cH2D(hexForm)
    endif
    if !modName
      returnForm = Game.GetForm(decForm)
    else
      returnForm = Game.GetFormFromFile(decForm, modName)
    endif
  endif
  return returnForm
endfunction

String function cStringSubString(String aString, Int startChar, Int numChars = 0, Bool useSKSE = TRUE) global
  {Requirements None, SKSE:Soft}
  String returnString
  if !aString
    cErrInvalidArg("cStringSubString", "!aString", "\"\"")
  elseif numChars < 0
    cErrInvalidArg("cStringSubString", "numChars < 0", "\"\"")
  elseif useSKSE && SKSE.GetVersion()
    if StringUtil.GetLength(aString) > numChars
      numChars = 0 ; 0 == rest of string
    endif
    returnString = StringUtil.SubString(aString, startChar, numChars)
  else
    String[] stringBuild = cStringToArray(aString, -1)
    returnString = cArrayJoinString(stringBuild, "", startChar, startChar + (numChars - 1))
  endif
  return returnString
endfunction

function cErrInvalidArg(String functionName, String argName = "", String returnValue = "", \
    Int errorLevel = 2, Bool condition = TRUE, Bool useSKSE = TRUE) global
  {Requirements: None, ConsoleUtil:Soft}
  if useSKSE && SKSE.GetVersion() && StringUtil.Find(functionName, "array") != -1
    returnValue = "arrayNone"
  endif
  clibTrace(functionName, "Argument(s)" + cTernaryString(argName != "", ": " + argName, "") + " invalid!" + \
    cTernaryString(returnValue != "", " Returning " + returnValue, ""), errorLevel, condition)
endfunction

function cErrArrInitFail(String functionName, String arrayName = "newArray", String returnValue = "ArrayNone", \
    Int errorLevel = 2, Bool condition = TRUE) global
  {Requirements: None, ConsoleUtil:Soft}
  clibTrace(functionName, "Variable: " + arrayName + " failed to initialize! Returning " + returnValue, errorLevel, \
    condition)
endfunction

String   function cTernaryString(Bool ifThis, String returnThis, String elseThis = "") global
  {Requirements: None}
  if ifThis
    return returnThis
  endif
  return elseThis
endfunction

String   function cGetScriptName() global
  {Requirements: None}
  return "clib"
endfunction  

function clibTrace(String functionName, String msg, Int errorLevel, Bool condition = TRUE) global
  {Requirements: None, ConsoleUtil:Soft}
  condition = TRUE ; change this to false to disable all trace messages
  if condition
    Debug.Trace(cGetScriptName() + "::" + functionName + "():: " + msg, errorLevel)
  endif
endfunction

Int    function cH2D(String aString) global
  {Requirements: None, SKSE:Soft}
  Int returnInt
  if !aString
    cErrInvalidArg("cH2D", "!aString")
  else
    String[] digits = cArrayHexDigits()
    String[] aArray
    String curDigit
    Int hCalcVal
    Int remaining
    Int power
    aArray = cStringHexToArray(aString)
    remaining = aArray.length
    while remaining >= 0
      curDigit = aArray[remaining - 1]
      hCalcVal = digits.Find(curDigit)
      power = aArray.length - remaining
      returnInt = hCalcVal * (Math.Pow(16, power) as int) + returnInt
      remaining -= 1
    endwhile
  endif
  return returnInt
endfunction

String function cD2H(Int aInt, Bool useSKSE = TRUE) global
  {Requirements: None, SKSE:Soft}
  String returnString
  if useSKSE && SKSE.GetVersion()
    String digits = "0123456789ABCDEF"
    Int shifted = 0
    while shifted < 32
      returnString = StringUtil.GetNthChar(digits, Math.LogicalAnd(0xF, aInt)) + returnString
      aInt = Math.RightShift(aInt, 4)
      shifted += 4
    endwhile
  else
    ; This code from dylbill, thanks!!
    ; https://forums.nexusmods.com/index.php?/topic/8441118-convert-decimal-formid-to-hexadecimal/
    String[] HexDigits = New String[16]
    HexDigits[0] = "0"
    HexDigits[1] = "1"
    HexDigits[2] = "2"
    HexDigits[3] = "3"
    HexDigits[4] = "4"
    HexDigits[5] = "5"
    HexDigits[6] = "6"
    HexDigits[7] = "7"
    HexDigits[8] = "8"
    HexDigits[9] = "9"
    HexDigits[10] = "a"
    HexDigits[11] = "b"
    HexDigits[12] = "c"
    HexDigits[13] = "d"
    HexDigits[14] = "e"
    HexDigits[15] = "f"
    
    Int[] NegativeDecDigits = New Int[16]
    NegativeDecDigits[0] = -15
    NegativeDecDigits[1] = -14
    NegativeDecDigits[2] = -13
    NegativeDecDigits[3] = -12
    NegativeDecDigits[4] = -11
    NegativeDecDigits[5] = -10
    NegativeDecDigits[6] = -9
    NegativeDecDigits[7] = -8
    NegativeDecDigits[8] = -7
    NegativeDecDigits[9] = -6
    NegativeDecDigits[10] = -5
    NegativeDecDigits[11] = -4
    NegativeDecDigits[12] = -3
    NegativeDecDigits[13] = -2
    NegativeDecDigits[14] = -1
    NegativeDecDigits[15] = 0
    Int scratchInt
    if aInt >= 0
      String s
      Int v = 0x10000000  ; init divisor
      while (v > 0)
        Int j = aInt / v
        s += HexDigits[j]
        aInt = aInt % v         ; new remainder as result of modulo
        if (v > 1)
          v = v / 16      ; new divisor
        else
          v = 0           ; 1 / 16 = ?    safety first
        endif
      endwhile
      return s
    else
      aInt += 1
      string s
      Int v = 0x10000000  ; init divisor
      while (v > 0)
        Int j = aInt / v
        s += HexDigits[NegativeDecDigits.Find(j)]
        aInt = aInt % v         ; new remainder as result of modulo
        if (v > 1)
          v = v / 16      ; new divisor
        else
          v = 0           ; 1 / 16 = ?    safety first
        endif
      endwhile
      returnString = s
    endif
  endif
  return returnString
endfunction

String[] function cArrayHexDigits() global
  {Requirements: None}
  String[] digits = New String[17]
  digits[0] = "0"
  digits[1] = "1"
  digits[2] = "2"
  digits[3] = "3"
  digits[4] = "4"
  digits[5] = "5"
  digits[6] = "6"
  digits[7] = "7"
  digits[8] = "8"
  digits[9] = "9"
  digits[10] = "A"
  digits[11] = "B"
  digits[12] = "C"
  digits[13] = "D"
  digits[14] = "E"
  digits[15] = "F"
  digits[16] = "G" ; makes nabbing F more accurate ('else' will also allow invalid values to pass through)
  return digits
endfunction

String function cStringHexCheck(String aString, String builtString, String[] hexDigits) global
  {Requirements: None}
  ; Returns next hex digit in string without SKSE
  String returnString
  if aString < (builtstring + hexDigits[8])
    if aString < (builtstring + hexDigits[4])
      if aString < (builtstring + hexDigits[1])
        returnString = hexDigits[0]
      elseif aString < (builtstring + hexDigits[2])
        returnString = hexDigits[1]
      elseif aString < (builtstring + hexDigits[3])
        returnString = hexDigits[2]
      else
        returnString = hexDigits[3]
      endif
    elseif aString < (builtstring + hexDigits[5])
        returnString = hexDigits[4]
    elseif aString < (builtstring + hexDigits[6])
      returnString = hexDigits[5]
    elseif aString < (builtstring + hexDigits[7])
      returnString = hexDigits[6]
    else
      returnString = hexDigits[7]
    endif
  elseif aString < (builtstring + hexDigits[16])
    if aString < (builtstring + hexDigits[12])
      if aString < (builtstring + hexDigits[9])
        returnString = hexDigits[8]
      elseif aString < (builtstring + hexDigits[10])
        returnString = hexDigits[9]
      elseif aString < (builtstring + hexDigits[11])
        returnString = hexDigits[10]
      else
        returnString = hexDigits[11]
      endif
    elseif aString < (builtstring + hexDigits[13])
      returnString = hexDigits[12]
    elseif aString < (builtstring + hexDigits[14])
      returnString = hexDigits[13]
    elseif aString < (builtstring + hexDigits[15])
      returnString = hexDigits[14]
    elseif aString < (builtstring + hexDigits[16])
      returnString = hexDigits[15]
    endif
  endif
  return returnString
endfunction

String[] function cStringHexToArray(String aString, Bool useSKSE = TRUE) global
  {Requirements: None, SKSE:Soft}
  ; Non-SKSE version only has to look through the *16* hex digits as opposed to all 69 ASCII chars
  String[] stringBuild
  if !aString
    cErrInvalidArg("cStringHexToArray", "!aString")
  elseif useSKSE && SKSE.GetVersion()
    Int stringLength = StringUtil.GetLength(aString)
    if stringLength == 1
      stringBuild = New String[1] ; returns single index array containing aString
      stringBuild[0] = aString
    else
      stringBuild = Utility.CreateStringArray(stringLength)
      Int i = 0
      while i < stringLength
        stringBuild[i] = StringUtil.SubString(aString, i, 1)
        i += 1
      endwhile
    endif
  else
    String[] hexDigits = cArrayHexDigits()
    String builtString = ""
    stringBuild = New String[12] ; the individual letters, no idea how many are being passed
    Int iterations = 0
    while builtString != aString && iterations < stringBuild.length
      stringBuild[iterations] = cStringHexCheck(aString, builtString, hexDigits)
      builtString += stringBuild[iterations]
      iterations += 1
    endwhile
    stringBuild = cArrayResizeString(stringBuild, iterations)
  endif
  return stringBuild
endfunction

String[] function cArrayResizeString(String[] aArray, Int newSize, String filler = "", Int clampMinLength = -1, \
  Int clampMaxLength = -1, Bool usePapUtil = TRUE) global
  {Requirements: None, PapyrusUtil:Soft}
  String[] newArray
  if !aArray
    cErrInvalidArg("cArrayResizeString", "!aArray", "")
  elseif newSize < 1
    cErrInvalidArg("cArrayResizeString", "newSize < 1", "")
  elseif clampMinLength != -1 && clampMinLength < 0
    cErrInvalidArg("cArrayResizeString", "newSize < 1", "")
  else
    if (clampMinLength != -1) && (newSize < clampMinLength)
      newSize = clampMinLength
    endif
    if (clampMaxLength != -1) && (newSize > clampMaxLength)
      newSize = clampMaxLength
    endif
    if usePapUtil && PapyrusUtil.GetVersion()
      newArray = PapyrusUtil.ResizeStringArray(aArray, newSize, filler)
    else
      newArray = cArrayCreateString(newSize, filler)
      if newArray.length
        Int i = 0
        while i < newArray.length
          if i < aArray.length
            newArray[i] = aArray[i]
          else
            newArray[i] = filler
          endif
          i += 1
        endwhile
      else
        cErrArrInitFail("cArrayResizeString")
      endif
    endif
  endif
  return newArray
endfunction
	
;From clib
;====== FormID handling 
  ;>>> number base conversion
Int      function cGetIntSubID(Int decForm, String hexForm = "", Form aForm = None) global
  {Requirements: None}
  ; used in GetFormFromFile
  Int returnInt = 0 ; chose zero because it isn't a valid ID and can be used as a bool as well
  if !decForm && !hexForm && !aForm
    cErrInvalidArg("cGetIntSubID", "!decForm && !hexForm && !aForm", "0") ; no valid args
  else
    returnInt = cH2D(cGetHexSubID(decForm, hexForm, aForm))
  endif
  return returnInt
endfunction
String   function cGetHexSubID(Int decForm, String hexForm = "", Form aForm = None) global
  {Requirements: None}
  ; Returns last 3 hex digits for light or 6 in regular. Input for this function assumes some prior 
  ;   validation. FormIDs must be 'fully loaded' (e.g. hexForm must be 8 digits). Using aForm argument
  ;     requires that it be currently loaded but decForm || hexForm arguments does not
  String returnString = hexForm
  if !hexForm && !decForm && !aForm
    cErrInvalidArg("cGetHexSubID", "!hexForm && !decForm && !aForm", "\"\"")
  else
    if !hexForm
      if aForm
        returnString = cGetHexIDFromForm(aForm)
      elseif decForm
        returnString = cD2H(decForm) ; get the hex string first
      endif
    endif
    if cIsLight(returnString)
      returnString = cStringSubString(hexForm, 5) ; 3rd arg default == 'rest of string'
    else
      returnString = cStringSubString(hexForm, 2) ; 3rd arg default == 'rest of string'
    endif
  endif
  return returnString
endfunction
String   function cGetHexIDFromForm(Form aForm) global
  {Requirements: None}
  String returnString
  if !aForm
    cErrInvalidArg("cGetHexIDFromForm", "!aForm", "\"\"")
  else
    returnString = cD2H(aForm.GetFormID())
  endif
  return returnString
endfunction
Int[]    function cArrayHexStringsToDecimal(String[] aArray) global
  {Requirements: None}
  ; without SKSE array creation is limited to 128!
  Int[] newArray
  if !aArray
    cErrInvalidArg("cArrayHexStringsToDecimal", "!aArray")
  else
    newArray = cArrayCreateInt(aArray.length)
    if newArray.length
      Int i = 0
      while i < aArray.length
        newArray[i] = cH2D(aArray[i])
        i += 1
      endwhile
    else
      cErrArrInitFail("cArrayHexStringsToDecimal")
    endif
  endif
  return newArray
endfunction
String[] function cArrayDecimalsToHexStrings(Int[] aArray) global
  {Requirements: None}
  String[] newArray
  if !aArray
    cErrInvalidArg("cArrayDecimalsToHexStrings", "!aArray")
  else
    newArray = cArrayCreateString(aArray.length, "")
    if newArray.length
      Int i = 0
      while i < aArray.length
        newArray[i] = cD2H(aArray[i])
        i += 1
      endwhile
    else
      cErrArrInitFail("cArrayDecimalsToHexStrings")
    endif
  endif
  return newArray
endfunction
  ;>>> fairly accurate method of determining if full (loaded) FormID is vanilla or not
Bool     function cIDInVanillaRange(Int decForm, String hexForm = "", Form aForm = None) global
  {Requirements: None}
  ; Requires full formID of a loaded plugin
  ; Checks if the dec FormID value is between 0 and SSEEdit value for next form in Dragonborn.esm
  ; NOTE: Injected records cannot be differentiated. This does not mean the form is valid, only that it's in range
  ;   however, apart from this caveat it does confirm that it is *not* from a mod use cGetForm to test validity
  Bool returnBool
  if !hexForm && !decForm && !aForm
    cErrInvalidArg("cIDInVanillaRange", "!hexForm && !decForm && !aForm", "\"\"")
  else
    if aForm
      decForm = aForm.GetFormID()
    elseif hexForm
      decForm = cH2D(hexForm)
    endif
    returnBool = cIsBetweenInt(decForm, 1, 67232578)
  endif
  return returnBool
endfunction

String[] function cArrayCreateString(Int indices, String filler = "", Bool useSKSE = TRUE, Bool outputTrace = TRUE) global
  {Requirements: None, SKSE:Soft}
  String[] aArray
  if useSKSE && indices > 0
    aArray = Utility.CreateStringArray(indices, filler)
  elseif indices > 128 || indices < 1
    ; outputTrace = False    ; uncomment to stop trace messages
    ; useConsoleUtil = TRUE ; uncomment to stop ConsoleUtil use
    if outputTrace
      String msg = "cArrayCreateString()::Arg 'indices' (" + indices + ") out of bounds! (>128)"
      Debug.Trace("cArrayString::" + msg + " Returning ArrayNone", 2)
    endif
  else
    if indices < 65
      if indices < 33
        if indices < 17
          if indices < 9
            if indices < 5
              if indices < 2
                aArray = new String[1]
              elseif indices < 3
                aArray = new String[2]
              elseif indices < 4
                aArray = new String[3]
              else
                aArray = new String[4]
              endif
            else
              if indices < 6
                aArray = new String[5]
              elseif indices < 7
                aArray = new String[6]
              elseif indices < 8
                aArray = new String[7]
              else
                aArray = new String[8]
              endif
            endif
          else
            if indices < 13
              if indices < 10
                aArray = new String[9]
              elseif indices < 11
                aArray = new String[10]
              elseif indices < 12
                aArray = new String[11]
              else
                aArray = new String[12]
              endif
            else
              if indices < 14
                aArray = new String[13]
              elseif indices < 15
                aArray = new String[14]
              elseif indices < 16
                aArray = new String[15]
              else
                aArray = new String[16]
              endif
            endif
          endif
        else
          if indices < 25
            if indices < 21
              if indices < 18
                aArray = new String[17]
              elseif indices < 19
                aArray = new String[18]
              elseif indices < 20
                aArray = new String[19]
              else
                aArray = new String[20]
              endif
            else
              if indices < 22
                aArray = new String[21]
              elseif indices < 23
                aArray = new String[22]
              elseif indices < 24
                aArray = new String[23]
              else
                aArray = new String[24]
              endif
            endif
          else
            if indices < 29
              if indices < 26
                aArray = new String[25]
              elseif indices < 27
                aArray = new String[26]
              elseif indices < 28
                aArray = new String[27]
              else
                aArray = new String[28]
              endif
            else
              if indices < 30
                aArray = new String[29]
              elseif indices < 31
                aArray = new String[30]
              elseif indices < 32
                aArray = new String[31]
              else
                aArray = new String[32]
              endif
            endif
          endif
        endif
      else
        if indices < 49
          if indices < 41
            if indices < 37
              if indices < 34
                aArray = new String[33]
              elseif indices < 35
                aArray = new String[34]
              elseif indices < 36
                aArray = new String[35]
              else
                aArray = new String[36]
              endif
            else
              if indices < 38
                aArray = new String[37]
              elseif indices < 39
                aArray = new String[38]
              elseif indices < 40
                aArray = new String[39]
              else
                aArray = new String[40]
              endif
            endif
          else
            if indices < 45
              if indices < 42
                aArray = new String[41]
              elseif indices < 43
                aArray = new String[42]
              elseif indices < 44
                aArray = new String[43]
              else
                aArray = new String[44]
              endif
            else
              if indices < 46
                aArray = new String[45]
              elseif indices < 47
                aArray = new String[46]
              elseif indices < 48
                aArray = new String[47]
              else
                aArray = new String[48]
              endif
            endif
          endif
        else
          if indices < 57
            if indices < 53
              if indices < 50
                aArray = new String[49]
              elseif indices < 51
                aArray = new String[50]
              elseif indices < 52
                aArray = new String[51]
              else
                aArray = new String[52]
              endif
            else
              if indices < 54
                aArray = new String[53]
              elseif indices < 55
                aArray = new String[54]
              elseif indices < 56
                aArray = new String[55]
              else
                aArray = new String[56]
              endif
            endif
          else
            if indices < 61
              if indices < 58
                aArray = new String[57]
              elseif indices < 59
                aArray = new String[58]
              elseif indices < 60
                aArray = new String[59]
              else
                aArray = new String[60]
              endif
            else
              if indices < 62
                aArray = new String[61]
              elseif indices < 63
                aArray = new String[62]
              elseif indices < 64
                aArray = new String[63]
              else
                aArray = new String[64]
              endif
            endif
          endif
        endif
      endif
    else
      if indices < 97
        if indices < 81
          if indices < 73
            if indices < 69
              if indices < 66
                aArray = new String[65]
              elseif indices < 67
                aArray = new String[66]
              elseif indices < 68
                aArray = new String[67]
              else
                aArray = new String[68]
              endif
            else
              if indices < 70
                aArray = new String[69]
              elseif indices < 71
                aArray = new String[70]
              elseif indices < 72
                aArray = new String[71]
              else
                aArray = new String[72]
              endif
            endif
          else
            if indices < 77
              if indices < 74
                aArray = new String[73]
              elseif indices < 75
                aArray = new String[74]
              elseif indices < 76
                aArray = new String[75]
              else
                aArray = new String[76]
              endif
            else
              if indices < 78
                aArray = new String[77]
              elseif indices < 79
                aArray = new String[78]
              elseif indices < 80
                aArray = new String[79]
              else
                aArray = new String[80]
              endif
            endif
          endif
        else
          if indices < 89
            if indices < 85
              if indices < 82
                aArray = new String[81]
              elseif indices < 83
                aArray = new String[82]
              elseif indices < 84
                aArray = new String[83]
              else
                aArray = new String[84]
              endif
            else
              if indices < 86
                aArray = new String[85]
              elseif indices < 87
                aArray = new String[86]
              elseif indices < 88
                aArray = new String[87]
              else
                aArray = new String[88]
              endif
            endif
          else
            if indices < 93
              if indices < 90
                aArray = new String[89]
              elseif indices < 91
                aArray = new String[90]
              elseif indices < 92
                aArray = new String[91]
              else
                aArray = new String[92]
              endif
            else
              if indices < 94
                aArray = new String[93]
              elseif indices < 95
                aArray = new String[94]
              elseif indices < 96
                aArray = new String[95]
              else
                aArray = new String[96]
              endif
            endif
          endif
        endif
      else
        if indices < 113
          if indices < 105
            if indices < 101
              if indices < 98
                aArray = new String[97]
              elseif indices < 99
                aArray = new String[98]
              elseif indices < 100
                aArray = new String[99]
              else
                aArray = new String[100]
              endif
            else
              if indices < 102
                aArray = new String[101]
              elseif indices < 103
                aArray = new String[102]
              elseif indices < 104
                aArray = new String[103]
              else
                aArray = new String[104]
              endif
            endif
          else
            if indices < 109
              if indices < 106
                aArray = new String[105]
              elseif indices < 107
                aArray = new String[106]
              elseif indices < 108
                aArray = new String[107]
              else
                aArray = new String[108]
              endif
            else
              if indices < 110
                aArray = new String[109]
              elseif indices < 111
                aArray = new String[110]
              elseif indices < 112
                aArray = new String[111]
              else
                aArray = new String[112]
              endif
            endif
          endif
        else
          if indices < 121
            if indices < 117
              if indices < 114
                aArray = new String[113]
              elseif indices < 115
                aArray = new String[114]
              elseif indices < 116
                aArray = new String[115]
              else
                aArray = new String[116]
              endif
            else
              if indices < 118
                aArray = new String[117]
              elseif indices < 119
                aArray = new String[118]
              elseif indices < 120
                aArray = new String[119]
              else
                aArray = new String[120]
              endif
            endif
          else
            if indices < 125
              if indices < 122
                aArray = new String[121]
              elseif indices < 123
                aArray = new String[122]
              elseif indices < 124
                aArray = new String[123]
              else
                aArray = new String[124]
              endif
            else
              if indices < 126
                aArray = new String[125]
              elseif indices < 127
                aArray = new String[126]
              elseif indices < 128
                aArray = new String[127]
              else
                aArray = new String[128]
              endif
            endif
          endif
        endif
      endif
    endif
  endif
  if filler
    Int i = 0
    while i < aArray.length
      aArray[i] = filler
      i += 1
    endwhile
  endif
  return aArray
endfunction

Bool   function cIsLight(String hexForm = "", Int decForm = 0,Form formVar = None, Bool useSKSE = TRUE) global
  {Requirements: None, SKSE:Soft}
  if !hexForm && !decForm && !formVar
    cErrInvalidArg("cIsLight", "!hexForm && !decForm && !formVar")
  elseif useSKSE && SKSE.GetVersion()
    if hexForm
      return cStringLeft(hexForm, 2) == "FE"
    elseif formVar
      return cStringLeft(cGetHexIDFromForm(formVar), 2) == "FE"
    elseif decForm
      return cStringLeft(cD2H(decForm), 2) == "FE"
    endif
  else
    String[] aArray
    if hexForm
      aArray = cStringHexToArray(hexForm)
    elseif decForm
      aArray = cStringHexToArray(cD2H(decForm))
    endif
    if aArray[0] + aArray[1] == "FE"
      return TRUE
    else
      return False
    endif
  endif
endfunction

String[] function cStringToArray(String aString, Int numChars = -1, Bool useSKSE = TRUE) global
  {Requirements: None, SKSE:Soft}
  ; Splits a string into an array of its characters 
  String[] stringBuild
  if !aString
    cErrInvalidArg("cStringToArray", "!aString")
  elseif useSKSE && SKSE.GetVersion()
    Int stringLength = StringUtil.GetLength(aString)
    if stringLength == 1 || numChars == 1
      stringBuild = New String[1] ; returns single index array containing aString
      stringBuild[0] = aString
    else
      ; Updated to use .Split()
      stringBuild = StringUtil.Split(aString,"")
      ;stringBuild = Utility.CreateStringArray(stringLength)
      ;Int i = 0
      ;while i < stringLength
      ;  stringBuild[i] = StringUtil.SubString(aString, i, 1)
      ;  i += 1
      ;endwhile
    endif
  else
    String[] asciiChars = cArrayASCIIChars()
    String builtString = ""
    stringBuild = cArrayCreateString(128, "") ; the individual letters
    Int maxIterations = 128
    Int iterations = 0
    if numChars != -1
      maxIterations = numChars
    endif
    while builtString != aString && iterations < maxIterations
      stringBuild[iterations] = cStringASCIICheck(aString, builtString, asciiChars)
      builtString += stringBuild[iterations]
      iterations += 1
    endwhile
    stringBuild = cArrayResizeString(stringBuild, iterations)
  endif
  return stringBuild
endfunction

String   function cArrayJoinString(String[] aArray, String delimiterString = "", Int startIndex = 0, \
    Int numIndices = -1) global
  {Requirements: None}
  String returnString
  if !aArray
    cErrInvalidArg("cArrayJoinString", "!aArray")
  elseif startIndex < 0 
    cErrInvalidArg("cArrayJoinString", "startIndex < 0")
  elseif startIndex > aArray.length - 1
    cErrInvalidArg("cArrayJoinString", "startIndex > aArray.length - 1")
  elseif (numIndices + startIndex) > aArray.length
    cErrInvalidArg("cArrayJoinString", "(numIndices + startIndex) > aArray.length")
  else
    if numIndices == -1
      numIndices = aArray.length - 1 - startIndex
    endif
    Int i = startIndex
    while i < aArray.length && i <= numIndices
      if !returnString
        returnString = aArray[i] ; skip delimiter until returnString has something in it
      else
        returnString += delimiterString + aArray[i]
      endif
      i += 1
    endwhile
  endif
  return returnString
endfunction

Int[] function cArrayCreateInt(Int indices, Int filler = 0, Bool outputTrace = TRUE) global
  {Requirements: None}
  Int[] aArray
  if cArrayArgumentValidation(indices, outputTrace, "Int")
    if indices < 65
      if indices < 33
        if indices < 17
          if indices < 9
            if indices < 5
              if indices < 2
                aArray = new Int[1]
              elseif indices < 3
                aArray = new Int[2]
              elseif indices < 4
                aArray = new Int[3]
              else
                aArray = new Int[4]
              endif
            else
              if indices < 6
                aArray = new Int[5]
              elseif indices < 7
                aArray = new Int[6]
              elseif indices < 8
                aArray = new Int[7]
              else
                aArray = new Int[8]
              endif
            endif
          else
            if indices < 13
              if indices < 10
                aArray = new Int[9]
              elseif indices < 11
                aArray = new Int[10]
              elseif indices < 12
                aArray = new Int[11]
              else
                aArray = new Int[12]
              endif
            else
              if indices < 14
                aArray = new Int[13]
              elseif indices < 15
                aArray = new Int[14]
              elseif indices < 16
                aArray = new Int[15]
              else
                aArray = new Int[16]
              endif
            endif
          endif
        else
          if indices < 25
            if indices < 21
              if indices < 18
                aArray = new Int[17]
              elseif indices < 19
                aArray = new Int[18]
              elseif indices < 20
                aArray = new Int[19]
              else
                aArray = new Int[20]
              endif
            else
              if indices < 22
                aArray = new Int[21]
              elseif indices < 23
                aArray = new Int[22]
              elseif indices < 24
                aArray = new Int[23]
              else
                aArray = new Int[24]
              endif
            endif
          else
            if indices < 29
              if indices < 26
                aArray = new Int[25]
              elseif indices < 27
                aArray = new Int[26]
              elseif indices < 28
                aArray = new Int[27]
              else
                aArray = new Int[28]
              endif
            else
              if indices < 30
                aArray = new Int[29]
              elseif indices < 31
                aArray = new Int[30]
              elseif indices < 32
                aArray = new Int[31]
              else
                aArray = new Int[32]
              endif
            endif
          endif
        endif
      else
        if indices < 49
          if indices < 41
            if indices < 37
              if indices < 34
                aArray = new Int[33]
              elseif indices < 35
                aArray = new Int[34]
              elseif indices < 36
                aArray = new Int[35]
              else
                aArray = new Int[36]
              endif
            else
              if indices < 38
                aArray = new Int[37]
              elseif indices < 39
                aArray = new Int[38]
              elseif indices < 40
                aArray = new Int[39]
              else
                aArray = new Int[40]
              endif
            endif
          else
            if indices < 45
              if indices < 42
                aArray = new Int[41]
              elseif indices < 43
                aArray = new Int[42]
              elseif indices < 44
                aArray = new Int[43]
              else
                aArray = new Int[44]
              endif
            else
              if indices < 46
                aArray = new Int[45]
              elseif indices < 47
                aArray = new Int[46]
              elseif indices < 48
                aArray = new Int[47]
              else
                aArray = new Int[48]
              endif
            endif
          endif
        else
          if indices < 57
            if indices < 53
              if indices < 50
                aArray = new Int[49]
              elseif indices < 51
                aArray = new Int[50]
              elseif indices < 52
                aArray = new Int[51]
              else
                aArray = new Int[52]
              endif
            else
              if indices < 54
                aArray = new Int[53]
              elseif indices < 55
                aArray = new Int[54]
              elseif indices < 56
                aArray = new Int[55]
              else
                aArray = new Int[56]
              endif
            endif
          else
            if indices < 61
              if indices < 58
                aArray = new Int[57]
              elseif indices < 59
                aArray = new Int[58]
              elseif indices < 60
                aArray = new Int[59]
              else
                aArray = new Int[60]
              endif
            else
              if indices < 62
                aArray = new Int[61]
              elseif indices < 63
                aArray = new Int[62]
              elseif indices < 64
                aArray = new Int[63]
              else
                aArray = new Int[64]
              endif
            endif
          endif
        endif
      endif
    else
      if indices < 97
        if indices < 81
          if indices < 73
            if indices < 69
              if indices < 66
                aArray = new Int[65]
              elseif indices < 67
                aArray = new Int[66]
              elseif indices < 68
                aArray = new Int[67]
              else
                aArray = new Int[68]
              endif
            else
              if indices < 70
                aArray = new Int[69]
              elseif indices < 71
                aArray = new Int[70]
              elseif indices < 72
                aArray = new Int[71]
              else
                aArray = new Int[72]
              endif
            endif
          else
            if indices < 77
              if indices < 74
                aArray = new Int[73]
              elseif indices < 75
                aArray = new Int[74]
              elseif indices < 76
                aArray = new Int[75]
              else
                aArray = new Int[76]
              endif
            else
              if indices < 78
                aArray = new Int[77]
              elseif indices < 79
                aArray = new Int[78]
              elseif indices < 80
                aArray = new Int[79]
              else
                aArray = new Int[80]
              endif
            endif
          endif
        else
          if indices < 89
            if indices < 85
              if indices < 82
                aArray = new Int[81]
              elseif indices < 83
                aArray = new Int[82]
              elseif indices < 84
                aArray = new Int[83]
              else
                aArray = new Int[84]
              endif
            else
              if indices < 86
                aArray = new Int[85]
              elseif indices < 87
                aArray = new Int[86]
              elseif indices < 88
                aArray = new Int[87]
              else
                aArray = new Int[88]
              endif
            endif
          else
            if indices < 93
              if indices < 90
                aArray = new Int[89]
              elseif indices < 91
                aArray = new Int[90]
              elseif indices < 92
                aArray = new Int[91]
              else
                aArray = new Int[92]
              endif
            else
              if indices < 94
                aArray = new Int[93]
              elseif indices < 95
                aArray = new Int[94]
              elseif indices < 96
                aArray = new Int[95]
              else
                aArray = new Int[96]
              endif
            endif
          endif
        endif
      else
        if indices < 113
          if indices < 105
            if indices < 101
              if indices < 98
                aArray = new Int[97]
              elseif indices < 99
                aArray = new Int[98]
              elseif indices < 100
                aArray = new Int[99]
              else
                aArray = new Int[100]
              endif
            else
              if indices < 102
                aArray = new Int[101]
              elseif indices < 103
                aArray = new Int[102]
              elseif indices < 104
                aArray = new Int[103]
              else
                aArray = new Int[104]
              endif
            endif
          else
            if indices < 109
              if indices < 106
                aArray = new Int[105]
              elseif indices < 107
                aArray = new Int[106]
              elseif indices < 108
                aArray = new Int[107]
              else
                aArray = new Int[108]
              endif
            else
              if indices < 110
                aArray = new Int[109]
              elseif indices < 111
                aArray = new Int[110]
              elseif indices < 112
                aArray = new Int[111]
              else
                aArray = new Int[112]
              endif
            endif
          endif
        else
          if indices < 121
            if indices < 117
              if indices < 114
                aArray = new Int[113]
              elseif indices < 115
                aArray = new Int[114]
              elseif indices < 116
                aArray = new Int[115]
              else
                aArray = new Int[116]
              endif
            else
              if indices < 118
                aArray = new Int[117]
              elseif indices < 119
                aArray = new Int[118]
              elseif indices < 120
                aArray = new Int[119]
              else
                aArray = new Int[120]
              endif
            endif
          else
            if indices < 125
              if indices < 122
                aArray = new Int[121]
              elseif indices < 123
                aArray = new Int[122]
              elseif indices < 124
                aArray = new Int[123]
              else
                aArray = new Int[124]
              endif
            else
              if indices < 126
                aArray = new Int[125]
              elseif indices < 127
                aArray = new Int[126]
              elseif indices < 128
                aArray = new Int[127]
              else
                aArray = new Int[128]
              endif
            endif
          endif
        endif
      endif
    endif
    if filler
      Int i = 0
      while i < aArray.length
        aArray[i] = filler
        i += 1
      endwhile
    endif
  endif
  return aArray
endfunction

String[] function cArrayASCIIChars() global
  {Requirements: None}
  String[] ascii = New String[69]
  ascii[0] = " "
  ascii[1] = "!"
  ascii[2] = "\""
  ascii[3] = "#"
  ascii[4] = "$"
  ascii[5] = "%"
  ascii[6] = "&"
  ascii[7] = "'"
  ascii[8] = "("
  ascii[9] = ")"
  ascii[10] = "*"
  ascii[11] = "+"
  ascii[12] = ","
  ascii[13] = "-"
  ascii[14] = "."
  ascii[15] = "/"
  ascii[16] = "0"
  ascii[17] = "1"
  ascii[18] = "2"
  ascii[19] = "3"
  ascii[20] = "4"
  ascii[21] = "5"
  ascii[22] = "6"
  ascii[23] = "7"
  ascii[24] = "8"
  ascii[25] = "9"
  ascii[26] = ":"
  ascii[27] = ";"
  ascii[28] = "<"
  ascii[29] = "="
  ascii[30] = ">"
  ascii[31] = "?"
  ascii[32] = "@"
  ascii[33] = "["
  ascii[34] = "\\"
  ascii[35] = "]"
  ascii[36] = "^"
  ascii[37] = "_"
  ascii[38] = "`"
  ascii[39] = "A"
  ascii[40] = "B"
  ascii[41] = "C"
  ascii[42] = "D"
  ascii[43] = "E"
  ascii[44] = "F"
  ascii[45] = "G"
  ascii[46] = "H"
  ascii[47] = "I"
  ascii[48] = "J"
  ascii[49] = "K"
  ascii[50] = "L"
  ascii[51] = "M"
  ascii[52] = "N"
  ascii[53] = "O"
  ascii[54] = "P"
  ascii[55] = "Q"
  ascii[56] = "R"
  ascii[57] = "S"
  ascii[58] = "T"
  ascii[59] = "U"
  ascii[60] = "V"
  ascii[61] = "W"
  ascii[62] = "X"
  ascii[63] = "Y"
  ascii[64] = "Z"
  ascii[65] = "{"
  ascii[66] = "|"
  ascii[67] = "}"
  ascii[68] = "~"
  return ascii
endfunction

String function cStringASCIICheck(String aString, String builtString, String[] asciiChars) global
  {Requirements: None}
  ; Returns next ASCII character in string without SKSE
  String returnString
  if aString < (builtstring + asciiChars[34])
    if aString < (builtstring + asciiChars[18])
      if aString < (builtstring + asciiChars[8])
        if aString < (builtstring + asciiChars[4])
          if aString < (builtstring + asciiChars[1])
            returnString = asciiChars[0]
          elseif aString < (builtstring + asciiChars[2])
            returnString = asciiChars[1]
          elseif aString < (builtstring + asciiChars[3])
            returnString = asciiChars[2]
          else
            returnString = asciiChars[3]
          endif
        elseif aString < (builtstring + asciiChars[5])
          returnString = asciiChars[4]
        elseif aString < (builtstring + asciiChars[6])
          returnString = asciiChars[5]
        elseif aString < (builtstring + asciiChars[7])
          returnString = asciiChars[6]
        else
          returnString = asciiChars[7]
        endif
      elseif aString < (builtstring + asciiChars[13])
        if aString < (builtstring + asciiChars[9])
          returnString = asciiChars[8]
        elseif aString < (builtstring + asciiChars[10])
          returnString = asciiChars[9]
        elseif aString < (builtstring + asciiChars[11])
          returnString = asciiChars[10]
        elseif aString < (builtstring + asciiChars[12])
          returnString = asciiChars[11]
        else
          returnString = asciiChars[12]
        endif
      elseif aString < (builtstring + asciiChars[14])
        returnString = asciiChars[13]
      elseif aString < (builtstring + asciiChars[15])
        returnString = asciiChars[14]
      elseif aString < (builtstring + asciiChars[16])
        returnString = asciiChars[15]
      elseif aString < (builtstring + asciiChars[17])
        returnString = asciiChars[16]
      else
        returnString = asciiChars[17]
      endif
    elseif aString < (builtstring + asciiChars[26])
      if aString < (builtstring + asciiChars[22])
        if aString < (builtstring + asciiChars[19])
          returnString = asciiChars[18]
        elseif aString < (builtstring + asciiChars[20])
          returnString = asciiChars[19]
        elseif aString < (builtstring + asciiChars[21])
          returnString = asciiChars[20]
        else
          returnString = asciiChars[21]
        endif
      elseif aString < (builtstring + asciiChars[23])
        returnString = asciiChars[22]
      elseif aString < (builtstring + asciiChars[24])
        returnString = asciiChars[23]
      elseif aString < (builtstring + asciiChars[25])
        returnString = asciiChars[24]
      else
        returnString = asciiChars[25]
      endif
    elseif aString < (builtstring + asciiChars[30])
      if aString < (builtstring + asciiChars[27])
        returnString = asciiChars[26]
      elseif aString < (builtstring + asciiChars[28])
        returnString = asciiChars[27]
      elseif aString < (builtstring + asciiChars[29])
        returnString = asciiChars[28]
      else
        returnString = asciiChars[29]
      endif
    elseif aString < (builtstring + asciiChars[31])
      returnString = asciiChars[30]
    elseif aString < (builtstring + asciiChars[32])
      returnString = asciiChars[31]
    elseif aString < (builtstring + asciiChars[33])
      returnString = asciiChars[32]
    else
      returnString = asciiChars[33]
    endif
  else ; don't feel like researching non-printable characters to add them 'else' catches the rest
    if aString < (builtstring + asciiChars[51])
      if aString < (builtstring + asciiChars[43])
        if aString < (builtstring + asciiChars[39])
          if aString < (builtstring + asciiChars[35])
            returnString = asciiChars[34]
          elseif aString < (builtstring + asciiChars[36])
            returnString = asciiChars[35]
          elseif aString < (builtstring + asciiChars[37])
            returnString = asciiChars[36]
          elseif aString < (builtstring + asciiChars[38])
            returnString = asciiChars[37]
          else ; < [39]
            returnString = asciiChars[38]
          endif
        elseif aString < (builtstring + asciiChars[40])
          returnString = asciiChars[39]
        elseif aString < (builtstring + asciiChars[41])
          returnString = asciiChars[40]
        elseif aString < (builtstring + asciiChars[42])
          returnString = asciiChars[41]
        else ; < [43]
          returnString = asciiChars[42]
        endif
      elseif aString < (builtstring + asciiChars[47])
        if aString < (builtstring + asciiChars[44])
          returnString = asciiChars[43]
        elseif aString < (builtstring + asciiChars[45])
          returnString = asciiChars[44]
        elseif aString < (builtstring + asciiChars[46])
          returnString = asciiChars[45]
        else
          returnString = asciiChars[46]
        endif
      elseif aString < (builtstring + asciiChars[48])
        returnString = asciiChars[47]
      elseif aString < (builtstring + asciiChars[49])
        returnString = asciiChars[48]
      elseif aString < (builtstring + asciiChars[50])
        returnString = asciiChars[49]
      else
        returnString = asciiChars[50]
      endif
    else ; don't feel like researching non-printable characters so this is the final stop
      if aString < (builtstring + asciiChars[60])
        if aString < (builtstring + asciiChars[55])
          if aString < (builtstring + asciiChars[52])
            returnString = asciiChars[51]
          elseif aString < (builtstring + asciiChars[53])
            returnString = asciiChars[52]
          elseif aString < (builtstring + asciiChars[54])
            returnString = asciiChars[53]
          else
            returnString = asciiChars[54]
          endif
        elseif aString < (builtstring + asciiChars[56])
          returnString = asciiChars[55]
        elseif aString < (builtstring + asciiChars[57])
          returnString = asciiChars[56]
        elseif aString < (builtstring + asciiChars[58])
          returnString = asciiChars[57]
        elseif aString < (builtstring + asciiChars[59])
          returnString = asciiChars[58]
        else
          returnString = asciiChars[59]
        endif
      elseif aString < (builtstring + asciiChars[65])
        if aString < (builtstring + asciiChars[61])
          returnString = asciiChars[60]
        elseif aString < (builtstring + asciiChars[62])
          returnString = asciiChars[61]
        elseif aString < (builtstring + asciiChars[63])
          returnString = asciiChars[62]
        elseif aString < (builtstring + asciiChars[64])
          returnString = asciiChars[63]
        else
          returnString = asciiChars[64]
        endif
      elseif aString < (builtstring + asciiChars[66])
        returnString = asciiChars[65]
      elseif aString < (builtstring + asciiChars[67])
        returnString = asciiChars[66]
      elseif aString < (builtstring + asciiChars[68])
        returnString = asciiChars[67]
      else ; don't feel like researching non-printable characters so this is the final stop
        returnString = asciiChars[68]
      endif
    endif
  endif
  ;cLibTrace("cStringASCIICheck", "aString: " + aString + ", builtString: " + builtString + ", returnString: " + returnString,0)
  return returnString
  
endfunction

Bool function cArrayArgumentValidation(Int indices, Bool outputTrace, String type) global
  if indices > 128 || indices < 1
    ; outputTrace = False    ; uncomment to stop trace messages
    if outputTrace
      String msg = "cArrayCreate" + type + "::Arg 'indices' (" + indices + ") out of bounds! (>128) Returning ArrayNone"
      Debug.Trace(msg, 2)
    endif
    return TRUE
  endif
  return False
endfunction

Bool function cIsBetweenInt(Int aValue, Int minV, Int maxV) global
  {Requirements: None}
  if minV > maxV
    cErrInvalidArg("cIsBetweenInt", "minV > maxV", "False")
  else
    return minV <= maxV && aValue >= minV && aValue <= maxV
  endif
  return False
endfunction

String function cStringLeft(String aString, Int numChars, Bool useSKSE = TRUE) global
  {Requirements: None, SKSE:Soft}
  ; thank you cadpnq for the suggestion that made the non-SKSE version possible!
  String returnString
  if !aString
    cErrInvalidArg("cStringLeft", "!aString", "\"\"")
  elseif !numChars 
    cErrInvalidArg("cStringLeft", "!numChars", "\"\"")
  elseif numChars < 0
    cErrInvalidArg("cStringLeft", "numChars < 0", "\"\"")
  elseif useSKSE && SKSE.GetVersion()
    if StringUtil.GetLength(aString) <= numChars
      numChars = 0 ; 0 == rest of string
    endif
    returnString = StringUtil.SubString(aString, 0, numChars)
  else
    String[] asciiChars = cArrayASCIIChars()
    Int iterations = 0
    while iterations < numChars && returnString != aString
      returnString += cStringASCIICheck(aString, returnString, asciiChars)
      iterations += 1
    endwhile
  endif
  return returnString
endfunction