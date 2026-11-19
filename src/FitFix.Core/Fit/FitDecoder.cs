using Dynastream.Fit;
using FitFix.Core.Diagnostics;

namespace FitFix.Core.Fit;

/// <summary>Обгортка над Decode/MesgBroadcaster офіційного Garmin FIT SDK.</summary>
internal static class FitDecoder
{
    public static void Decode(Stream stream, Action<MesgBroadcaster> subscribe)
    {
        var decoder = new Decode();
        var broadcaster = new MesgBroadcaster();
        decoder.MesgEvent += broadcaster.OnMesg;
        decoder.MesgDefinitionEvent += broadcaster.OnMesgDefinition;
        subscribe(broadcaster);

        if (!decoder.IsFIT(stream))
            throw new InvalidFitFileException(DiagnosticCodes.NotAFitFile, "The stream does not carry a FIT header.");

        stream.Position = 0;
        if (!decoder.CheckIntegrity(stream))
            throw new InvalidFitFileException(
                DiagnosticCodes.FitIntegrityFailed, "The FIT file failed the SDK integrity check.");

        stream.Position = 0;

        // A file can pass both checks above and still fail mid-read; that refusal needs a
        // code just as much as the two above (AD-18).
        try
        {
            decoder.Read(stream);
        }
        catch (Exception exception) when (exception is FitException or EndOfStreamException)
        {
            throw new InvalidFitFileException(
                DiagnosticCodes.FitIntegrityFailed, "The FIT file could not be read to the end.", exception);
        }
    }

    public static double? ToDegrees(int? semicircles) =>
        semicircles is { } value && value != 0x7FFFFFFF ? value * Geo.SemicirclesToDegrees : null;

    public static int ToSemicircles(double degrees) => (int)Math.Round(degrees / Geo.SemicirclesToDegrees);
}
