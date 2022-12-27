namespace SynthEBD;

public class VM_BodyGenMiscMenu
{
    private readonly Logger _logger;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    public VM_BodyGenMiscMenu(Logger logger, RaceMenuIniHandler raceMenuHandler)
    {
        _logger = logger;
        _raceMenuHandler = raceMenuHandler;

        SetRaceMenuINI = new(
            canExecute: _ => true,
            execute: _ =>
            {
                if (_raceMenuHandler.SetRaceMenuIniForBodyGen())
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("RaceMenu Ini set successfully", ErrorType.Warning, 2); // Warning yellow font is easier to see than green
                }
                else
                {
                    _logger.LogErrorWithStatusUpdate("Error encountered trying to set RaceMenu's ini.", ErrorType.Error);
                }
            }
        );
    }
    public RelayCommand SetRaceMenuINI { get; set; } 
}