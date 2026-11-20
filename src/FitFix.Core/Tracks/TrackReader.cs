using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;

namespace FitFix.Core.Tracks;

/// <summary>Формат Еталонного треку, визначений за вмістом файлу.</summary>
public enum TrackFormat
{
    Fit,
    Gpx,
    Tcx
}

/// <summary>
/// Визначає формат Еталонного треку за вмістом, а не за розширенням (PRD §4.1),
/// і віддає готовий <see cref="ReferenceTrack"/>.
/// </summary>
public static class TrackReader
{
    /// <summary>
    /// Reads the track and normalises it with the de-duplication spacing from
    /// <paramref name="options"/>; no threshold is defaulted here (AD-10).
    /// </summary>
    public static ReferenceTrack ReadReferenceTrack(Stream stream, AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var points = ReadPoints(stream, out _);
        return ReferenceTrack.FromPoints(points, options);
    }

    public static IReadOnlyList<TrackPoint> ReadPoints(Stream stream, out TrackFormat format)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var seekable = EnsureSeekable(stream);
        format = DetectFormat(seekable);
        seekable.Position = 0;

        return format switch
        {
            TrackFormat.Fit => FitTrackReader.Read(seekable),
            TrackFormat.Gpx => GpxTrackReader.Read(seekable),
            TrackFormat.Tcx => TcxTrackReader.Read(seekable),
            _ => throw new InvalidTrackFileException(
                DiagnosticCodes.TrackFormatUnknown, $"Unsupported track format: {format}.")
        };
    }

    /// <inheritdoc cref="ReadReferenceTrack(Stream, AlignmentOptions)"/>
    public static ReferenceTrack ReadReferenceTrackFile(string path, AlignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var stream = System.IO.File.OpenRead(path);
        return ReadReferenceTrack(stream, options);
    }

    private static TrackFormat DetectFormat(Stream stream)
    {
        stream.Position = 0;
        var header = new byte[512];
        var read = stream.Read(header, 0, header.Length);

        // FIT: байти 8..11 заголовка — ".FIT".
        if (read >= 12 && header[8] == '.' && header[9] == 'F' && header[10] == 'I' && header[11] == 'T')
            return TrackFormat.Fit;

        var text = System.Text.Encoding.UTF8.GetString(header, 0, read);
        if (text.Contains("<gpx", StringComparison.OrdinalIgnoreCase))
            return TrackFormat.Gpx;
        if (text.Contains("TrainingCenterDatabase", StringComparison.OrdinalIgnoreCase))
            return TrackFormat.Tcx;

        throw new InvalidTrackFileException(
            DiagnosticCodes.TrackFormatUnknown, "The track format was not recognised; expected FIT, GPX or TCX.");
    }

    private static Stream EnsureSeekable(Stream stream)
    {
        if (stream.CanSeek)
            return stream;

        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }
}
