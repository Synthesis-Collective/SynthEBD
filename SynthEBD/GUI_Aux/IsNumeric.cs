namespace SynthEBD;

/// <summary>Text-input validation helper used by numeric <see cref="System.Windows.Controls.TextBox"/>
/// preview handlers to reject keystrokes that would make the field non-numeric.</summary>
public class IsNumeric
{
    //https://stackoverflow.com/questions/894263/identify-if-a-string-is-a-number

    /// <summary>Returns whether the box's current text with <paramref name="newText"/> appended parses
    /// as a number (invariant culture, any number style).</summary>
    /// <param name="currrentTextBox">The text box whose existing <c>Text</c> is the prefix.</param>
    /// <param name="newText">The candidate text to append (e.g. the keystroke or pasted text).</param>
    /// <returns><c>true</c> if the concatenated string parses as a <see cref="double"/>.</returns>
    public static bool IsTextNumeric(System.Windows.Controls.TextBox currrentTextBox, string newText)
    {
        string str = string.Join("", new string[] { currrentTextBox.Text, newText });
        double retNum;
        bool isNum = Double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.NumberFormatInfo.InvariantInfo, out retNum);
        return isNum;
    }
}