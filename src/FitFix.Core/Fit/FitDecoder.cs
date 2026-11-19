using Dynastream.Fit;

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
            throw new InvalidFitFileException("Це не схоже на FIT-файл.");

        stream.Position = 0;
        if (!decoder.CheckIntegrity(stream))
            throw new InvalidFitFileException("FIT-файл пошкоджено (не пройдено перевірку цілісності).");

        stream.Position = 0;
        decoder.Read(stream);
    }

    public static double? ToDegrees(int? semicircles) =>
        semicircles is { } value && value != 0x7FFFFFFF ? value * Geo.SemicirclesToDegrees : null;

    public static int ToSemicircles(double degrees) => (int)Math.Round(degrees / Geo.SemicirclesToDegrees);
}

public sealed class InvalidFitFileException(string message) : Exception(message);
