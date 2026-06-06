namespace SynthEBD;

/// <summary>
/// View model for the BodyGen config editor's miscellaneous menu. Exposes the command that
/// configures RaceMenu's ini for BodyGen via <see cref="RaceMenuIniHandler"/>.
/// </summary>
public class VM_BodyGenMiscMenu
{
    private readonly Logger _logger;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    /// <summary>Wires up the SetRaceMenuINI command, which sets RaceMenu's ini for BodyGen and reports success/failure via the status logger.</summary>
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