namespace FitFix.Api;

/// <summary>
/// What every endpoint that reads an uploaded file needs of it: a stream the core's readers can
/// rewind. One owner, because the two endpoints must hand the core the same kind of stream — a
/// part prepared one way here and another way there would have the same bytes fail in one place
/// and work in the other.
/// </summary>
internal static class UploadPart
{
    /// <summary>
    /// The part as a stream the readers can rewind. The FIT decoder reads the header, the
    /// integrity check and the messages from the start each time, so a part the server did not
    /// buffer would fail on the first seek — as an unexplained 500, since no code covers it.
    /// A part that is already seekable is handed over untouched; the upload bound is what makes
    /// the copy safe.
    /// </summary>
    public static async Task<Stream> SeekableAsync(IFormFile file, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(file);

        var content = file.OpenReadStream();
        if (content.CanSeek)
            return content;

        await using (content)
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellation);
            buffer.Position = 0;
            return buffer;
        }
    }
}
