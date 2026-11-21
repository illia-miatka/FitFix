using Dynastream.Fit;
using FitFix.Core.Diagnostics;
using FitDateTime = Dynastream.Fit.DateTime;

namespace FitFix.Core.Fit;

/// <summary>
/// Записує Відновлену активність у FIT так, щоб її прийняли Strava й Garmin Connect:
/// file_id → event(start) → records → event(stop_all) → lap × M → session → activity (PRD FR-6,
/// AD-19). The laps are the Original's own (AD-24): the writer transcribes the partition the
/// model derived and never invents a boundary of its own.
/// <para>
/// Every number a summary message states comes from <see cref="RepairResult.Summary"/>, computed
/// once by the core (AD-16). This file converts units and types — metres per second to the FIT
/// field's <c>float</c>, a climb to <c>total_ascent</c>'s whole metres — and performs no
/// arithmetic over a series: no average, no sum, no distance divided by a duration. A value the
/// summary leaves absent is written as no field at all, because absent and zero are different
/// claims about an activity.
/// </para>
/// </summary>
public static class FitActivityWriter
{
    /// <summary>
    /// The most laps a FIT file can carry. <c>message_index</c> is a 16-bit field whose top bits
    /// are reserved — one marks a selected message, three more are reserved outright — leaving
    /// twelve bits, so 4096 is the last index a reader can be relied on to see. A file past that
    /// is refused with a code rather than written with indices that collide (AD-18).
    /// </summary>
    private const int MaxLaps = 4096;

    /// <summary>
    /// Writes the repaired activity. <paramref name="utcOffsetSeconds"/> is stated by the caller
    /// rather than defaulted, so no entry point can inherit a silent zero (AD-10); Story 1.10
    /// takes the real offset from the original activity.
    /// <para>
    /// <see cref="RepairResult.Summary"/> is taken to describe <see cref="RepairResult.Records"/>:
    /// that is what <see cref="ActivitySummary.Of"/> produces and the one place the two are
    /// checked against each other, because a writer that measured the records again to verify them
    /// would be the second owner this story exists to remove (AD-16).
    /// </para>
    /// </summary>
    public static void Write(Stream stream, RepairResult result, int utcOffsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(result);

        var records = result.Records;

        // Derived from user data, so it carries a code rather than surfacing as an argument error (AD-18).
        if (records.Count < 2)
            throw new MalformedArgumentException(
                DiagnosticCodes.ActivityTooFewRecords,
                $"Writing a FIT activity needs at least 2 records ('{nameof(result)}').");

        var start = new FitDateTime(records[0].Timestamp);
        var end = new FitDateTime(records[^1].Timestamp);

        // Every duration, distance and average below is the core's (AD-16). The laps are the
        // Original's own, as a partition of the activity (AD-24): an Original with no lap message
        // yields exactly one lap covering everything, which is the file this writer produced
        // before laps existed.
        var summary = result.Summary;
        var laps = summary.Laps;

        // A limit of the format rather than a property of the activity, which is why it is refused
        // here and not where the laps were measured (AD-18).
        if (laps.Count > MaxLaps)
            throw new MalformedArgumentException(
                DiagnosticCodes.ActivityTooManyLaps,
                $"The activity has {laps.Count} laps, and a FIT file can index {MaxLaps} " +
                $"('{nameof(result)}').");

        // An activity nobody lapped is one lap covering everything (AD-24), so an empty list is a
        // summary of something else: writing it would produce a file with no lap message and
        // `num_laps = 0`, which Strava may refuse (AD-19).
        if (laps.Count == 0)
            throw new MalformedArgumentException(
                DiagnosticCodes.SummaryWithoutLaps,
                $"The summary carries no lap, so there is no lap message to write ('{nameof(result)}').");

        // Each lap's positions are read at the records its boundaries fall on. A summary measured
        // from a different series would index past this one, and an index out of range is not a
        // refusal a shell can report (AD-18).
        foreach (var lap in laps)
        {
            if (lap.FirstRecord < 0 || lap.LastRecord >= records.Count)
                throw new MalformedArgumentException(
                    DiagnosticCodes.SummarySeriesMismatch,
                    $"A lap of the summary spans records {lap.FirstRecord}..{lap.LastRecord} of a "
                        + $"series that holds {records.Count} ('{nameof(result)}').");
        }

        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(stream);

        encoder.Write(BuildFileId(end));
        encoder.Write(BuildTimerEvent(start, EventType.Start));

        foreach (var record in records)
            encoder.Write(BuildRecord(record));

        encoder.Write(BuildTimerEvent(end, EventType.StopAll));

        for (var index = 0; index < laps.Count; index++)
            encoder.Write(BuildLap(
                result,
                laps[index],
                messageIndex: (ushort)index,
                lastOfTheActivity: index == laps.Count - 1));

        encoder.Write(BuildSession(result, start, end, laps.Count));
        encoder.Write(BuildActivity(end, (float)summary.Total.MovingSeconds, utcOffsetSeconds));

        encoder.Close();
    }

    /// <inheritdoc cref="Write"/>
    public static void WriteFile(string path, RepairResult result, int utcOffsetSeconds)
    {
        using var stream = System.IO.File.Create(path);
        Write(stream, result, utcOffsetSeconds);
    }

    /// <inheritdoc cref="Write"/>
    public static byte[] WriteToArray(RepairResult result, int utcOffsetSeconds)
    {
        using var buffer = new MemoryStream();
        Write(buffer, result, utcOffsetSeconds);
        return buffer.ToArray();
    }

    private static FileIdMesg BuildFileId(FitDateTime created)
    {
        var mesg = new FileIdMesg();
        mesg.SetType(Dynastream.Fit.File.Activity);
        mesg.SetManufacturer(Manufacturer.Development);
        mesg.SetProduct(0);
        mesg.SetSerialNumber(0x12345678);
        mesg.SetTimeCreated(created);
        return mesg;
    }

    private static EventMesg BuildTimerEvent(FitDateTime timestamp, EventType type)
    {
        var mesg = new EventMesg();
        mesg.SetTimestamp(timestamp);
        mesg.SetEvent(Event.Timer);
        mesg.SetEventType(type);
        return mesg;
    }

    private static RecordMesg BuildRecord(RepairedRecord record)
    {
        var mesg = new RecordMesg();
        mesg.SetTimestamp(new FitDateTime(record.Timestamp));
        mesg.SetPositionLat(FitDecoder.ToSemicircles(record.Latitude));
        mesg.SetPositionLong(FitDecoder.ToSemicircles(record.Longitude));
        mesg.SetDistance((float)record.Distance);
        mesg.SetEnhancedSpeed((float)record.Speed);
        mesg.SetSpeed((float)record.Speed);

        if (record.Altitude is { } altitude)
        {
            mesg.SetEnhancedAltitude((float)altitude);
            mesg.SetAltitude((float)altitude);
        }

        if (record.HeartRate is { } heartRate)
            mesg.SetHeartRate(heartRate);
        if (record.Power is { } power)
            mesg.SetPower(power);
        if (record.Cadence is { } cadence)
            mesg.SetCadence(cadence);
        if (record.Temperature is { } temperature)
            mesg.SetTemperature(temperature);

        return mesg;
    }

    /// <summary>
    /// One <c>lap</c> message, transcribed from the lap's own summary (AD-16). The boundaries are
    /// where the athlete pressed the button (AD-24) and the numbers are the core's: a lap that was
    /// nothing but a pause therefore states a timer time of zero and no average speed at all,
    /// which is what standing still for a whole lap is.
    /// </summary>
    private static LapMesg BuildLap(
        RepairResult result,
        IntervalSummary lap,
        ushort messageIndex,
        bool lastOfTheActivity)
    {
        var mesg = new LapMesg();
        mesg.SetTimestamp(new FitDateTime(lap.End));
        mesg.SetStartTime(new FitDateTime(lap.Start));
        mesg.SetTotalElapsedTime((float)lap.ElapsedSeconds);
        mesg.SetTotalTimerTime((float)lap.MovingSeconds);
        mesg.SetTotalDistance((float)lap.DistanceMeters);
        mesg.SetSport(MapSport(result.Sport));
        mesg.SetEvent(Event.Lap);
        mesg.SetEventType(EventType.Stop);
        mesg.SetMessageIndex(messageIndex);

        // Why the lap ended, which Strava reads to tell a split the athlete pressed from an
        // automatic one. The last lap ends with the session; for the others the Original's own
        // trigger is not carried in the model, and a press is the honest reading of a boundary
        // somebody put there.
        mesg.SetLapTrigger(lastOfTheActivity ? LapTrigger.SessionEnd : LapTrigger.Manual);

        var records = result.Records;
        mesg.SetStartPositionLat(FitDecoder.ToSemicircles(records[lap.FirstRecord].Latitude));
        mesg.SetStartPositionLong(FitDecoder.ToSemicircles(records[lap.FirstRecord].Longitude));
        mesg.SetEndPositionLat(FitDecoder.ToSemicircles(records[lap.LastRecord].Latitude));
        mesg.SetEndPositionLong(FitDecoder.ToSemicircles(records[lap.LastRecord].Longitude));

        if (lap.AverageSpeedMetersPerSecond is { } averageSpeed)
        {
            mesg.SetAvgSpeed((float)averageSpeed);
            mesg.SetEnhancedAvgSpeed((float)averageSpeed);
        }

        if (lap.MaximumSpeedMetersPerSecond is { } maximumSpeed)
        {
            mesg.SetMaxSpeed((float)maximumSpeed);
            mesg.SetEnhancedMaxSpeed((float)maximumSpeed);
        }

        if (lap.AverageHeartRate is { } averageHeartRate)
            mesg.SetAvgHeartRate(averageHeartRate);
        if (lap.MaximumHeartRate is { } maximumHeartRate)
            mesg.SetMaxHeartRate(maximumHeartRate);
        if (lap.AveragePower is { } averagePower)
            mesg.SetAvgPower(averagePower);
        if (lap.MaximumPower is { } maximumPower)
            mesg.SetMaxPower(maximumPower);
        if (lap.AverageCadence is { } averageCadence)
            mesg.SetAvgCadence(averageCadence);
        if (lap.MaximumCadence is { } maximumCadence)
            mesg.SetMaxCadence(maximumCadence);
        if (lap.ElevationGainMeters is { } gain)
            mesg.SetTotalAscent(WholeMetres(gain));
        if (lap.ElevationLossMeters is { } loss)
            mesg.SetTotalDescent(WholeMetres(loss));

        return mesg;
    }

    /// <summary>
    /// The <c>session</c> message, transcribed from the activity's own summary (AD-16).
    /// </summary>
    private static SessionMesg BuildSession(
        RepairResult result,
        FitDateTime start,
        FitDateTime end,
        int lapCount)
    {
        var total = result.Summary.Total;

        var mesg = new SessionMesg();
        mesg.SetTimestamp(end);
        mesg.SetStartTime(start);
        mesg.SetTotalElapsedTime((float)total.ElapsedSeconds);
        mesg.SetTotalTimerTime((float)total.MovingSeconds);
        mesg.SetTotalDistance((float)total.DistanceMeters);
        mesg.SetSport(MapSport(result.Sport));
        mesg.SetSubSport(SubSport.Generic);
        mesg.SetEvent(Event.Session);
        mesg.SetEventType(EventType.Stop);
        mesg.SetMessageIndex(0);
        mesg.SetFirstLapIndex(0);
        // How many lap messages precede this session. A count of one beside several laps is a file
        // that contradicts itself, and Strava reads this field to find them.
        mesg.SetNumLaps((ushort)lapCount);
        mesg.SetTrigger(SessionTrigger.ActivityEnd);

        var records = result.Records;
        mesg.SetStartPositionLat(FitDecoder.ToSemicircles(records[0].Latitude));
        mesg.SetStartPositionLong(FitDecoder.ToSemicircles(records[0].Longitude));

        if (total.AverageSpeedMetersPerSecond is { } averageSpeed)
        {
            mesg.SetAvgSpeed((float)averageSpeed);
            mesg.SetEnhancedAvgSpeed((float)averageSpeed);
        }

        if (total.MaximumSpeedMetersPerSecond is { } maximumSpeed)
        {
            mesg.SetMaxSpeed((float)maximumSpeed);
            mesg.SetEnhancedMaxSpeed((float)maximumSpeed);
        }

        if (total.AverageHeartRate is { } averageHeartRate)
            mesg.SetAvgHeartRate(averageHeartRate);
        if (total.MaximumHeartRate is { } maximumHeartRate)
            mesg.SetMaxHeartRate(maximumHeartRate);
        if (total.AveragePower is { } averagePower)
            mesg.SetAvgPower(averagePower);
        if (total.MaximumPower is { } maximumPower)
            mesg.SetMaxPower(maximumPower);
        if (total.AverageCadence is { } averageCadence)
            mesg.SetAvgCadence(averageCadence);
        if (total.MaximumCadence is { } maximumCadence)
            mesg.SetMaxCadence(maximumCadence);
        if (total.ElevationGainMeters is { } gain)
            mesg.SetTotalAscent(WholeMetres(gain));
        if (total.ElevationLossMeters is { } loss)
            mesg.SetTotalDescent(WholeMetres(loss));

        return mesg;
    }

    private static ActivityMesg BuildActivity(FitDateTime end, float timer, int utcOffsetSeconds)
    {
        var mesg = new ActivityMesg();
        mesg.SetTimestamp(end);
        mesg.SetTotalTimerTime(timer);
        mesg.SetNumSessions(1);
        mesg.SetType(Activity.Manual);
        mesg.SetEvent(Event.Activity);
        mesg.SetEventType(EventType.Stop);
        mesg.SetLocalTimestamp((uint)(end.GetTimeStamp() + utcOffsetSeconds));
        return mesg;
    }

    /// <summary>
    /// A climb or a descent in the whole metres <c>total_ascent</c> and <c>total_descent</c> are
    /// stated in — a unit conversion, not a measurement: both were measured in the core (AD-16).
    /// The field is sixteen bits whose top value means "no value", so an absurd number is pinned
    /// just below it: wrapping round would state a small climb, and landing on the sentinel would
    /// state none at all where the summary states an enormous one.
    /// </summary>
    private static ushort WholeMetres(double meters) =>
        (ushort)Math.Clamp(Math.Round(meters), 0, ushort.MaxValue - 1);

    private static Sport MapSport(SportKind sport) => sport switch
    {
        SportKind.Running => Sport.Running,
        SportKind.Cycling => Sport.Cycling,
        _ => Sport.Generic
    };
}
