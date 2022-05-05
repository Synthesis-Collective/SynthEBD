namespace SynthEBD
{
    public class VM_BodyGenMiscMenu
    {
        public VM_BodyGenMiscMenu()
        {
            SetRaceMenuINI = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (RaceMenuIniHandler.SetRaceMenuIniForBodyGen())
                    {
                        Logger.CallTimedLogErrorWithStatusUpdateAsync("RaceMenu Ini set successfully", ErrorType.Warning, 2); // Warning yellow font is easier to see than green
                    }
                    else
                    {
                        Logger.LogErrorWithStatusUpdate("Error encountered trying to set RaceMenu's ini.", ErrorType.Error);
                        Logger.SwitchViewToLogDisplay();
                    }
                }
                );
        }

        public RelayCommand SetRaceMenuINI { get; }
    }
}
