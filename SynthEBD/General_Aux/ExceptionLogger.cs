namespace SynthEBD;

/// <summary>Flattens an exception and its inner-exception chain into a human-readable log string.</summary>
public class ExceptionLogger
{
    /// <summary>
    /// Builds a multi-layer textual dump of <paramref name="e"/> and every nested
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    /// <param name="e">The exception to format.</param>
    /// <returns>A formatted string with one labelled section per layer of the inner-exception chain.</returns>
    public static string GetExceptionStack(Exception e)
    {
        return GetExceptionStack(e, "", 0);
    }
    /// <summary>
    /// Recursive worker: appends one layer of the inner-exception chain to <paramref name="error"/>
    /// and recurses into the inner exception.
    /// </summary>
    /// <param name="e">Exception for the current layer.</param>
    /// <param name="error">Text accumulated from outer layers.</param>
    /// <param name="layer">Zero-based depth of the current layer, used in the section header.</param>
    /// <returns>The fully accumulated text once the chain is exhausted.</returns>
    /// <remarks>
    /// The outermost layer is skipped when it is a ReactiveUI <c>UnhandledErrorException</c> wrapping an
    /// inner exception, since that wrapper carries no SynthEBD-specific detail. A
    /// <c>TooManyMastersException</c> additionally has its master list appended.
    /// </remarks>
    private static string GetExceptionStack(Exception e, string error, int layer)
    {
        if (e is not ReactiveUI.UnhandledErrorException || e.InnerException is null) // skip logging the top layer since it is not specific to SynthEBD
        {
            error += Environment.NewLine + "======= Layer " + layer.ToString() + ": " + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace + Environment.NewLine + Environment.NewLine;
        }
        
        if (e is Mutagen.Bethesda.Plugins.Exceptions.TooManyMastersException tooManyMastersEx)
        {
            error += "Current Masters:" + Environment.NewLine + String.Join(Environment.NewLine, tooManyMastersEx.Masters.Select(x => x.FileName).ToArray()) + Environment.NewLine + Environment.NewLine;
        }

        if (e.InnerException != null)
        {
            return GetExceptionStack(e.InnerException, error, layer + 1);
        }
        else
        {
            return error;
        }
    }
}