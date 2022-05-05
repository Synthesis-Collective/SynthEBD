namespace SynthEBD
{
    public class IsNumeric
    {
        //https://stackoverflow.com/questions/894263/identify-if-a-string-is-a-number

        public static bool IsTextNumeric(System.Windows.Controls.TextBox currrentTextBox, string newText)
        {
            string str = string.Join("", new string[] { currrentTextBox.Text, newText });
            double retNum;
            bool isNum = Double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.NumberFormatInfo.InvariantInfo, out retNum);
            return isNum;
        }
    }
}
