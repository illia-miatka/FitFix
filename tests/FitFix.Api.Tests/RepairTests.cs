using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FitFix.Api.Contracts;
using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Fit;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Api.Tests;

/// <summary>
/// Story 2.3: <c>POST /api/repair</c> takes the original and the track and answers with everything
/// the result screen and the later preview need. Over real HTTP, because a contract is an HTTP
/// shape: the parts of the request, the status of a refusal and the members of the body are the
/// thing under test.
/// </summary>
public class RepairTests : IDisposable
{
    /// <summary>A name that must never reach a log (AD-9), and a lie about the format besides (AD-13).</summary>
    private const string UploadedFileName = "chabany-secret-run.fit";

    /// <summary>
    /// The budget of one repair, s. Epic 2's own constraint: a two-hour activity is repaired
    /// synchronously, inside the request.
    /// </summary>
    private const double BudgetSeconds = 3;

    /// <summary>
    /// How long the two-hour fixture runs, s. Past the budget's own subject: the activity the
    /// constraint names, with the series to prove nothing was thinned out of it.
    /// </summary>
    private const int TwoHoursSeconds = 7200;

    private readonly Lazy<ApiHost> _host = new(() => new ApiHost());

    private ApiHost Host => _host.Value;

    public void Dispose()
    {
        if (_host.IsValueCreated)
            _host.Value.Dispose();
    }

    [Fact]
    public async Task A_repair_answers_with_the_numbers_the_core_settled()
    {
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        // The same bytes through the core directly: the answer must quote these numbers, not
        // numbers of its own (AD-16).
        var expected = Repair(original, track);
        var report = expected.Report;

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var before = body.GetProperty("before");
        Assert.Equal("running", before.GetProperty("sport").GetString());
        Assert.Equal(
            expected.CleanedOriginal.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            before.GetProperty("startedAt").GetString());
        Assert.Equal(expected.Records.Count, before.GetProperty("recordCount").GetInt32());
        Assert.Equal(report.OriginalDistanceMeters, before.GetProperty("recordedDistanceMeters").GetDouble());
        Assert.Equal(report.DiscardedPositionCount, before.GetProperty("discardedPositionCount").GetInt32());

        AssertAfterSummary(body.GetProperty("after"), expected);

        // Both lengths, in both modes: the athlete cannot choose between two numbers they are
        // never shown (AD-17).
        Assert.Equal(report.TrackLengthMeters, body.GetProperty("track").GetProperty("lengthMeters").GetDouble());
        Assert.Equal(
            report.UsedTrackLengthMeters,
            body.GetProperty("track").GetProperty("usedLengthMeters").GetDouble());

        Assert.NotEmpty(body.GetProperty("fitBase64").GetString()!);
    }

    [Fact]
    public async Task Every_member_of_the_after_summary_is_its_own_number()
    {
        // The plain fixture cannot catch a transcription that reads the wrong field: its cadence
        // never changes and its power is one value, so an average filled from a maximum is the
        // same number, and its loop course climbs and descends equally, so swapping the gain and
        // the loss changes nothing. This pair makes all six of those distinct.
        var run = SyntheticRun.Create();
        var original = SyntheticFitBuilder.WriteActivity(run.Original with
        {
            Records = [.. run.Original.Records.Select((record, index) => record with
            {
                Power = (ushort)(200 + (index % 7)),
                Cadence = (byte)(80 + (index % 5)),
                // The watch's own altitude only climbs too, and more gently than the track does
                // (0.015 m/m against 0.02): a measured record keeps its own altitude and an
                // estimated one takes the track's (AD-26), so a watch that climbed faster than the
                // route would make the switch of source a descent and put a loss in the summary
                // that neither profile contains.
                Altitude = SyntheticRun.OriginalStartAltitude
                    + 0.015 * index * SyntheticRun.SpeedMetersPerSecond
            })]
        });
        var track = SyntheticFitBuilder.WriteTrack(Climbing(run), SyntheticRun.StartTime);

        var expected = Repair(original, track);
        var total = expected.Summary.Total;

        // The fixture is only worth what it distinguishes, so it says so itself.
        Assert.True(total.AveragePower < total.MaximumPower);
        Assert.True(total.AverageCadence < total.MaximumCadence);
        Assert.True(total.AverageHeartRate < total.MaximumHeartRate);
        Assert.True(total.AverageSpeedMetersPerSecond < total.MaximumSpeedMetersPerSecond);
        // A route that only climbs: the descent is zero and the climb is not, so neither can stand
        // in for the other.
        Assert.True(total.ElevationGainMeters > 1);
        Assert.Equal(0, total.ElevationLossMeters!.Value, 0.001);

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAfterSummary(body.GetProperty("after"), expected);
    }

    [Fact]
    public async Task A_paused_run_states_the_moving_time_the_file_states()
    {
        // Elapsed, moving and paused are one number in a run nobody stopped, so only a paused run
        // can tell them apart — and the pace the screen shows is the distance over the moving
        // seconds it was given (FR-5, AD-7). A UI handed the elapsed time there would show a pace
        // over the whole clock, which is exactly what the written file contradicts.
        var run = SyntheticRun.CreateWithPause(SyntheticRun.PauseStartSeconds, SyntheticRun.PauseSeconds);
        var (original, track) = Files(run);
        var expected = Repair(original, track);

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);
        var after = body.GetProperty("after");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var elapsed = after.GetProperty("elapsedSeconds").GetDouble();
        var moving = after.GetProperty("movingSeconds").GetDouble();
        var paused = after.GetProperty("pausedSeconds").GetDouble();

        // The three are three numbers here, and the pause is the whole difference between them.
        Assert.Equal(SyntheticRun.PauseSeconds, paused, 0.001);
        Assert.True(moving < elapsed, $"{moving:F0} s moving against {elapsed:F0} s elapsed");
        Assert.Equal(elapsed - moving, paused, 0.001);

        // And all three are the core's own, not a subtraction performed on the way out (AD-16).
        AssertAfterSummary(after, expected);

        // The file says the same, so the screen and the upload cannot disagree.
        var session = FitMessageInspector.InspectTiming(
            Convert.FromBase64String(body.GetProperty("fitBase64").GetString()!)).Session;
        Assert.Equal(moving, session.TimerSeconds!.Value, 0.01);
        Assert.Equal(elapsed, session.ElapsedSeconds!.Value, 0.01);
    }

    [Fact]
    public async Task The_series_axis_is_each_records_own_second()
    {
        // A ride under smart recording writes a record every ten seconds, so the index of a record
        // and its second are different numbers — and the zones are stated in seconds. A series
        // carrying indices, or moving seconds, would draw the chart's axis and the shaded zones on
        // two different axes and put every hover in the wrong place (AD-5).
        var ride = SyntheticRun.CreateRide();
        var (original, track) = Files(ride);
        var expected = Repair(original, track);
        var cleaned = expected.CleanedOriginal;

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        var seconds = Values(body, "secondsFromStart");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(cleaned.Records.Count, seconds.Length);
        Assert.True(seconds.Length > 2, $"the ride holds {seconds.Length} records");

        for (var index = 0; index < seconds.Length; index++)
        {
            Assert.Equal(
                cleaned.SecondsFromStart(cleaned.Records[index]),
                seconds[index].GetDouble(),
                0.001);
        }

        // Which is not the record's index, and this fixture is what proves the difference.
        Assert.Equal(SyntheticRun.RideSecondsBetweenRecords, seconds[1].GetDouble(), 0.001);
        Assert.Equal(
            body.GetProperty("after").GetProperty("elapsedSeconds").GetDouble(),
            seconds[^1].GetDouble(),
            0.001);

        // The zones are stated in the same measure, which is the whole point of stating it.
        Assert.Equal(
            seconds[^1].GetDouble(),
            body.GetProperty("zones").EnumerateArray().Last().GetProperty("toSeconds").GetDouble(),
            0.001);
    }

    [Fact]
    public async Task A_file_that_measured_no_distance_says_so_rather_than_zero()
    {
        // The claim the inspect endpoint already makes about this very file
        // (InspectTests.One_number_one_claim_about_a_file_that_recorded_no_distance): a watch that
        // measured nothing has not measured zero metres, and the two endpoints may not describe
        // one file differently (AD-15). One `?? 0` in the transcription would put a zero on the
        // left of the before-and-after.
        var run = SyntheticRun.Create(withAnyGps: false);
        var (original, track) = Files(run);

        var expected = Repair(original, track);
        Assert.Null(expected.Report.OriginalDistanceMeters);

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("before").GetProperty("recordedDistanceMeters").ValueKind);

        // The same of the device's own series: absent readings, not a row of zeroes that would
        // draw a watch standing at the start line for the whole run.
        var distance = Values(body, "original", "distanceMeters");
        var speed = Values(body, "original", "speedMetersPerSecond");
        for (var index = 0; index < expected.Records.Count; index++)
        {
            AssertMeasurement(expected.CleanedOriginal.Records[index].Distance, distance[index]);
            AssertMeasurement(expected.CleanedOriginal.Records[index].Speed, speed[index]);
        }

        // Every summary member against the core, nulls included: whatever this file left absent,
        // the answer leaves absent too.
        AssertAfterSummary(body.GetProperty("after"), expected);
    }

    [Fact]
    public async Task A_track_without_elevation_leaves_the_originals_own_altitude_in_place()
    {
        // A route drawn without elevation has not drawn flat ground. The repaired series keeps the
        // Original's own altitude there — a null or a zero would be a profile the activity never
        // had, and the climb would vanish from a file that climbed.
        var run = SyntheticRun.Create();
        var original = SyntheticFitBuilder.WriteActivity(run.Original);
        var flat = SyntheticFitBuilder.WriteTrack(
            [.. run.TrackPoints.Select(point => new TrackPoint(point.Position, null))],
            SyntheticRun.StartTime);

        var expected = Repair(original, flat);

        using var response = await RepairAsync(original, flat);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var altitude = Values(body, "repaired", "altitudeMeters");
        for (var index = 0; index < expected.Records.Count; index++)
            AssertMeasurement(expected.Records[index].Altitude, altitude[index]);

        // Which is the Original's own, measured and present — so the summary states a climb.
        Assert.Equal(
            run.Original.Records[0].Altitude!.Value,
            altitude[0].GetDouble(),
            0.001);
        AssertAfterSummary(body.GetProperty("after"), expected);
        Assert.Equal(JsonValueKind.Number, body.GetProperty("after").GetProperty("elevationGainMeters").ValueKind);
    }

    [Fact]
    public async Task The_file_in_the_answer_is_a_fit_whose_session_states_the_after_summary()
    {
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        var repaired = Convert.FromBase64String(body.GetProperty("fitBase64").GetString()!);

        // The core reads it back: a base64 string that is not a FIT file would be the one defect
        // every other assertion here could pass over.
        using var stream = new MemoryStream(repaired);
        var read = FitActivityReader.Read(stream);
        Assert.Equal(body.GetProperty("before").GetProperty("recordCount").GetInt32(), read.Records.Count);

        // And the file states the very numbers the answer does: the screen and the file cannot
        // disagree about one run (AD-16).
        var session = FitMessageInspector.InspectTiming(repaired).Session;
        var after = body.GetProperty("after");

        Assert.Equal(after.GetProperty("distanceMeters").GetDouble(), session.DistanceMeters!.Value, 0.5);
        Assert.Equal(after.GetProperty("elapsedSeconds").GetDouble(), session.ElapsedSeconds!.Value, 0.01);
        Assert.Equal(after.GetProperty("movingSeconds").GetDouble(), session.TimerSeconds!.Value, 0.01);
    }

    [Fact]
    public async Task Every_series_is_as_long_as_the_record_list_and_a_gap_is_null()
    {
        // The GPS drops out twice and comes back: the records keep arriving throughout, so the
        // original's position series has holes in it while every other series is complete. A
        // shorter array there would mean the client has to work out which record each entry
        // belongs to (AD-5).
        var run = SyntheticRun.CreateWithGpsReturning();
        var (original, track) = Files(run);

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        var count = body.GetProperty("before").GetProperty("recordCount").GetInt32();
        Assert.Equal(run.Original.Records.Count, count);
        // Nothing was discarded here, so a null position below is a fix the watch never had rather
        // than one cleaning threw away — which is what makes the indices comparable with the
        // fixture's own seconds.
        Assert.Equal(0, body.GetProperty("before").GetProperty("discardedPositionCount").GetInt32());

        foreach (var (name, series) in EverySeries(body))
            Assert.Equal(count, series.GetArrayLength());

        var latitude = Values(body, "original", "latitude");
        var repairedLatitude = Values(body, "repaired", "latitude");

        // One record a second in this fixture, so an index is a second: the holes are exactly the
        // two dropouts, stated as null rather than as a missing element or a zero — a zero would
        // put the athlete on Null Island.
        for (var second = 0; second < count; second++)
        {
            Assert.Equal(SyntheticRun.WithoutAFixAt(second), latitude[second].ValueKind == JsonValueKind.Null);
            Assert.Equal(JsonValueKind.Number, repairedLatitude[second].ValueKind);
        }

        // The series is the one the repair worked from, record by record — a preview drawn from it
        // is a preview of the file that will be uploaded.
        var expected = Repair(original, track);
        var distance = Values(body, "repaired", "distanceMeters");
        for (var index = 0; index < count; index++)
            Assert.Equal(expected.Records[index].Distance, distance[index].GetDouble(), 0.001);
    }

    [Fact]
    public async Task Nothing_is_thinned_out_of_a_two_hour_activity()
    {
        var (original, track) = TwoHourFiles();

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        var count = body.GetProperty("before").GetProperty("recordCount").GetInt32();

        // One entry per record, not a sample of them: a server that thinned would decide for the
        // client what resolution it may have (AD-5, AD-23).
        Assert.True(count > TwoHoursSeconds, $"the fixture holds {count} records");
        Assert.All(EverySeries(body), series => Assert.Equal(count, series.Series.GetArrayLength()));
    }

    [Fact]
    public async Task A_two_hour_activity_is_repaired_inside_the_budget()
    {
        var (original, track) = TwoHourFiles();

        // One small repair first. The budget is about the service, not about the first call into a
        // cold pipeline: without this the measurement below would include the JIT of the whole
        // core, which no later request pays.
        var run = SyntheticRun.Create();
        var (warmOriginal, warmTrack) = Files(run);
        using var warmUp = await RepairAsync(warmOriginal, warmTrack);
        Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);

        var startedAt = Stopwatch.GetTimestamp();
        using var response = await RepairAsync(original, track);
        var body = await response.Content.ReadAsStringAsync();
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(body);
        Assert.True(
            elapsed.TotalSeconds < BudgetSeconds,
            $"the repair took {elapsed.TotalSeconds:F2} s against a budget of {BudgetSeconds:F0} s");
    }

    [Fact]
    public async Task The_zones_travel_whole()
    {
        // The GPS returns twice, so the activity has several measured stretches: a single
        // "measured until" would call the kilometre whose signal came back estimated (FR-8).
        var run = SyntheticRun.CreateWithGpsReturning();
        var (original, track) = Files(run);
        var expected = Repair(original, track).Report;

        using var response = await RepairAsync(original, track);
        var body = await ReadJsonAsync(response);

        var zones = body.GetProperty("zones").EnumerateArray().ToArray();

        Assert.Equal(expected.Zones.Count, zones.Length);
        Assert.True(zones.Length > 2, $"the fixture has {zones.Length} zones");

        for (var index = 0; index < zones.Length; index++)
        {
            var zone = expected.Zones[index];
            Assert.Equal(
                zone.Kind == ZoneKind.Measured ? ZoneKindName.Measured : ZoneKindName.Estimated,
                zones[index].GetProperty("kind").GetString());
            Assert.Equal(zone.FromSeconds, zones[index].GetProperty("fromSeconds").GetDouble(), 0.001);
            Assert.Equal(zone.ToSeconds, zones[index].GetProperty("toSeconds").GetDouble(), 0.001);
        }

        // Tiling the activity exactly once — no gap, no overlap — so a bar drawn from it covers
        // the chart and the client can work out the measured share for itself (AD-14).
        Assert.Equal(0, zones[0].GetProperty("fromSeconds").GetDouble(), 0.001);
        Assert.Equal(
            body.GetProperty("after").GetProperty("elapsedSeconds").GetDouble(),
            zones[^1].GetProperty("toSeconds").GetDouble(),
            0.001);
        for (var index = 1; index < zones.Length; index++)
        {
            Assert.Equal(
                zones[index - 1].GetProperty("toSeconds").GetDouble(),
                zones[index].GetProperty("fromSeconds").GetDouble(),
                0.001);
            Assert.NotEqual(
                zones[index - 1].GetProperty("kind").GetString(),
                zones[index].GetProperty("kind").GetString());
        }
    }

    [Fact]
    public async Task Each_warning_carries_its_severity_as_a_string()
    {
        // An over-long track blocks the download until the user confirms it; the stalled GPS this
        // service exists for blocks nothing. One activity raises both, so the answer has to carry
        // the severity per code rather than one verdict for the lot (AD-22).
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var (original, track) = Files(run);
        var expected = Repair(original, track).Report.Warnings;

        using var response = await RepairAsync(original, track);
        var warnings = Warnings(await ReadJsonAsync(response));

        Assert.Equal(expected, warnings.Keys);
        Assert.Equal(SeverityName.Blocking, warnings[DiagnosticCodes.TrackLongerThanActivity]);
        Assert.Equal(SeverityName.Info, warnings[DiagnosticCodes.GpsStallDetected]);
    }

    [Fact]
    public async Task No_derived_value_travels_in_the_answer()
    {
        // The core computes a pace and a measured fraction for the CLI and the DTO still may not
        // carry them: a UI that divides the distance it was given by the moving seconds it was
        // given can never disagree with the file, while a second number beside them can (AD-14).
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        using var response = await RepairAsync(original, track);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var derived = new List<string>();
        Walk(document.RootElement, derived);

        Assert.Empty(derived);
    }

    [Fact]
    public async Task The_mode_is_honoured_and_the_warning_stands_in_both()
    {
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var (original, track) = Files(run);
        var whole = Repair(original, track).Report.TrackLengthMeters;
        var confirmedDistance = Repair(original, track, TrackLengthMode.Full).Report.RepairedDistanceMeters;

        using var conservative = await RepairAsync(original, track);
        using var confirmed = await RepairAsync(original, track, TrackLengthModes.Full);

        var trimmed = await ReadJsonAsync(conservative);
        var full = await ReadJsonAsync(confirmed);

        var trimmedDistance = trimmed.GetProperty("after").GetProperty("distanceMeters").GetDouble();
        var fullDistance = full.GetProperty("after").GetProperty("distanceMeters").GetDouble();

        // Confirming the track changes the result — that is why the client repeats the request
        // instead of merely opening the gate (AD-17).
        Assert.True(fullDistance > trimmedDistance, $"{fullDistance:F0} m against {trimmedDistance:F0} m");

        // The whole track is what the estimated zone drew from; the distance the answer states is
        // that plus what the measured zone's own positions measured, so it is past the track's
        // length rather than on it (AD-26) — and it is the core's number, not one of the API's.
        Assert.Equal(confirmedDistance, fullDistance, 0.001);
        Assert.True(fullDistance > whole, $"{fullDistance:F1} m is not past the track's {whole:F1} m");
        Assert.Equal(whole, full.GetProperty("track").GetProperty("usedLengthMeters").GetDouble(), 0.001);
        Assert.True(trimmed.GetProperty("track").GetProperty("lengthMeters").GetDouble() > trimmedDistance);

        // And it does not erase the fact: a report that dropped the code would leave the screen
        // unable to say which of the two lengths the file in front of the user ended on.
        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, Warnings(trimmed).Keys);
        Assert.Contains(DiagnosticCodes.TrackLongerThanActivity, Warnings(full).Keys);

        // The mode named explicitly is the mode a request inherits when it names none.
        using var stated = await RepairAsync(original, track, TrackLengthModes.Plausible);
        Assert.Equal(
            trimmedDistance,
            (await ReadJsonAsync(stated)).GetProperty("after").GetProperty("distanceMeters").GetDouble(),
            0.001);
    }

    [Theory]
    [InlineData("whatever")]
    // The mode is a token of the protocol, matched exactly, like the inspect endpoint's role.
    [InlineData("Full")]
    [InlineData("FULL")]
    public async Task An_unknown_mode_is_refused_rather_than_guessed(string mode)
    {
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        using var response = await RepairAsync(original, track, mode);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TrackLengthModeInvalid, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Two_modes_state_no_mode_at_all()
    {
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        using var content = Upload(original, track);
        content.Add(new StringContent(TrackLengthModes.Plausible), RepairEndpoint.TrackLengthModeField);
        content.Add(new StringContent(TrackLengthModes.Full), RepairEndpoint.TrackLengthModeField);

        using var response = await Host.Client.PostAsync(RepairEndpoint.Route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TrackLengthModeInvalid, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task One_file_only_says_the_other_is_missing()
    {
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        using var withoutTrack = await PostAsync(Part(original, FileRole.Original));
        Assert.Equal(HttpStatusCode.BadRequest, withoutTrack.StatusCode);
        Assert.Equal(DiagnosticCodes.FileMissing, await ReadCodeAsync(withoutTrack));

        using var withoutOriginal = await PostAsync(Part(track, FileRole.Track));
        Assert.Equal(HttpStatusCode.BadRequest, withoutOriginal.StatusCode);
        Assert.Equal(DiagnosticCodes.FileMissing, await ReadCodeAsync(withoutOriginal));
    }

    [Fact]
    public async Task Two_files_named_for_no_role_are_two_files_the_service_cannot_place()
    {
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        // The right number of files and nothing saying which is which. A FIT file is valid as an
        // activity and as a track, so there is nothing here to guess from (AD-15).
        using var response = await PostAsync(Part(original, "file"), Part(track, "file2"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.FileMissing, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Two_files_in_one_role_are_refused_rather_than_the_first_one_read()
    {
        var run = SyntheticRun.Create();
        var (original, track) = Files(run);

        using var twoOriginals = await PostAsync(
            Part(original, FileRole.Original), Part(original, FileRole.Original));
        Assert.Equal(HttpStatusCode.BadRequest, twoOriginals.StatusCode);
        Assert.Equal(DiagnosticCodes.TooManyFiles, await ReadCodeAsync(twoOriginals));

        // A third file is a mistake about the request rather than about either role, and saying a
        // role is missing would send the user looking for a file they already sent.
        using var three = await PostAsync(
            Part(original, FileRole.Original), Part(track, FileRole.Track), Part(track, "spare"));
        Assert.Equal(HttpStatusCode.BadRequest, three.StatusCode);
        Assert.Equal(DiagnosticCodes.TooManyFiles, await ReadCodeAsync(three));
    }

    [Fact]
    public async Task A_request_that_is_not_multipart_is_refused()
    {
        using var response = await Host.Client.PostAsync(
            RepairEndpoint.Route,
            new StringContent("""{"trackLengthMode":"full"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.RequestMalformed, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_refusal_of_the_files_is_the_cores_own_code()
    {
        // A track of one point has no geometry, and the core says so. The shell adds nothing: the
        // status comes from the code, and the human sentence lives in the UI dictionary (AD-18).
        var run = SyntheticRun.Create();
        var onePoint = SyntheticFitBuilder.WriteTrack([run.TrackPoints[0]], SyntheticRun.StartTime);

        using var response = await RepairAsync(SyntheticFitBuilder.WriteActivity(run.Original), onePoint);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DiagnosticCodes.TrackTooFewPoints, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_repair_logs_the_shape_of_the_activity_and_nothing_of_either_file()
    {
        // AD-9 allows durations, counts, the sport, the mode and the codes. The two things this
        // endpoint actually holds and could leak are the name a file was uploaded under and the
        // repair's own distances — the one number that is the athlete's route rather than its
        // shape — so those are what the assertions below look for. Whether anything is kept is
        // settled where it is decided: FailureContractTests pins the form reader's buffering, and
        // AD-3 has no other door into this process.
        var run = SyntheticRun.Create().WithTrackDrawn(SyntheticRun.LoopsPastTwiceThePlausibleDistance);
        var (original, track) = Files(run);
        var expected = Repair(original, track).Report;

        using var response = await RepairAsync(original, track, TrackLengthModes.Full, UploadedFileName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var text = Host.Logs.Text;

        // The whole line, field by field, so a line that quietly grew a distance or a coordinate
        // fails here rather than passing a search for what it never formatted.
        Assert.Contains($"POST {RepairEndpoint.Route} -> 200", text, StringComparison.Ordinal);
        Assert.Contains(
            $"Repaired an activity: running, {run.Original.Records.Count} records, "
            + $"{expected.ElapsedSeconds:F0} s, {expected.AnchorCount} anchors, "
            + $"{expected.LapCount} laps, {TrackLengthMode.Full} track length, "
            + $"{expected.DiscardedPositionCount} positions discarded, "
            + $"codes {string.Join(' ', expected.Warnings)}.",
            text,
            StringComparison.Ordinal);

        Assert.DoesNotContain(UploadedFileName, text, StringComparison.OrdinalIgnoreCase);

        // Not the route, nor how long it was: the distances are the activity itself.
        foreach (var meters in new[]
        {
            expected.RepairedDistanceMeters, expected.TrackLengthMeters, expected.UsedTrackLengthMeters
        })
        {
            Assert.DoesNotContain(meters.ToString("F0", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            SyntheticRun.StartLatitude.ToString("F4", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SyntheticRun.StartLongitude.ToString("F4", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two uploads of one fixture: the original as a FIT activity, the track as a FIT track.
    /// </summary>
    private static (byte[] Original, byte[] Track) Files(SyntheticRun run) =>
        (SyntheticFitBuilder.WriteActivity(run.Original),
            SyntheticFitBuilder.WriteTrack(run.TrackPoints, SyntheticRun.StartTime));

    /// <summary>
    /// A two-hour pair: the fixture's own run with the frozen GPS, its records carried on at 1 Hz
    /// to the duration Epic 2's budget names. Only the tail is added — the route, the anchors and
    /// the stall are the fixture's, so the repair under measurement is the ordinary one and merely
    /// longer.
    /// </summary>
    private static (byte[] Original, byte[] Track) TwoHourFiles()
    {
        var run = SyntheticRun.Create();
        var records = run.Original.Records.ToList();
        var last = records[^1];

        while ((records[^1].Timestamp - records[0].Timestamp).TotalSeconds < TwoHoursSeconds)
            records.Add(last with { Timestamp = records[^1].Timestamp.AddSeconds(1) });

        return (SyntheticFitBuilder.WriteActivity(run.Original with { Records = records }),
            SyntheticFitBuilder.WriteTrack(run.TrackPoints, SyntheticRun.StartTime));
    }

    /// <summary>The same two files through the core, which is where every expected number comes from.</summary>
    private static RepairResult Repair(byte[] original, byte[] track)
    {
        using var originalStream = new MemoryStream(original);
        using var trackStream = new MemoryStream(track);
        return RepairPipeline.Repair(originalStream, trackStream);
    }

    /// <inheritdoc cref="Repair(byte[], byte[])"/>
    private static RepairResult Repair(byte[] original, byte[] track, TrackLengthMode mode)
    {
        using var originalStream = new MemoryStream(original);
        using var trackStream = new MemoryStream(track);
        return RepairPipeline.Repair(originalStream, trackStream, mode);
    }

    /// <summary>Every series of the answer, by the name a failure should report it under.</summary>
    private static IReadOnlyList<(string Name, JsonElement Series)> EverySeries(JsonElement body)
    {
        var series = body.GetProperty("series");
        List<(string, JsonElement)> all = [("secondsFromStart", series.GetProperty("secondsFromStart"))];

        foreach (var group in new[] { "original", "repaired" })
        {
            foreach (var member in series.GetProperty(group).EnumerateObject())
                all.Add(($"{group}.{member.Name}", member.Value));
        }

        return all;
    }

    private static JsonElement[] Values(JsonElement body, string group, string name) =>
        [.. body.GetProperty("series").GetProperty(group).GetProperty(name).EnumerateArray()];

    private static JsonElement[] Values(JsonElement body, string name) =>
        [.. body.GetProperty("series").GetProperty(name).EnumerateArray()];

    /// <summary>
    /// The track's own geometry with a route that only climbs: the altitude rises with every
    /// point, so the descent of the repaired activity is zero and its climb is not. The fixture's
    /// own course returns to where it started, which makes the two equal and lets one stand in for
    /// the other unnoticed.
    /// </summary>
    private static IReadOnlyList<TrackPoint> Climbing(SyntheticRun run) =>
        [.. run.TrackPoints.Select((point, index) =>
            new TrackPoint(point.Position, SyntheticRun.OriginalStartAltitude + (index * 0.1)))];

    /// <summary>
    /// Every member of the after-summary against the summary the core settled once (AD-16). All of
    /// them together, because a member nobody reads is a member the transcription is free to fill
    /// from the field beside it.
    /// </summary>
    private static void AssertAfterSummary(JsonElement after, RepairResult expected)
    {
        var report = expected.Report;
        var total = expected.Summary.Total;

        Assert.Equal(report.RepairedDistanceMeters, after.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(report.ElapsedSeconds, after.GetProperty("elapsedSeconds").GetDouble());
        Assert.Equal(report.MovingSeconds, after.GetProperty("movingSeconds").GetDouble());
        Assert.Equal(report.PausedSeconds, after.GetProperty("pausedSeconds").GetDouble());
        Assert.Equal(report.LapCount, after.GetProperty("lapCount").GetInt32());

        AssertMeasurement(total.AverageSpeedMetersPerSecond, after, "averageSpeedMetersPerSecond");
        AssertMeasurement(total.MaximumSpeedMetersPerSecond, after, "maximumSpeedMetersPerSecond");
        AssertMeasurement(total.ElevationGainMeters, after, "elevationGainMeters");
        AssertMeasurement(total.ElevationLossMeters, after, "elevationLossMeters");

        AssertMeasurement(total.AverageHeartRate, after, "averageHeartRate");
        AssertMeasurement(total.MaximumHeartRate, after, "maximumHeartRate");
        AssertMeasurement(total.AveragePower, after, "averagePower");
        AssertMeasurement(total.MaximumPower, after, "maximumPower");
        AssertMeasurement(total.AverageCadence, after, "averageCadence");
        AssertMeasurement(total.MaximumCadence, after, "maximumCadence");
    }

    /// <summary>
    /// A member the summary may leave absent, which the contract states as null and never as zero:
    /// an activity that measured nothing has not measured zero.
    /// </summary>
    private static void AssertMeasurement(double? expected, JsonElement body, string name) =>
        AssertMeasurement(expected, body.GetProperty(name));

    /// <inheritdoc cref="AssertMeasurement(double?, JsonElement, string)"/>
    private static void AssertMeasurement(double? expected, JsonElement member)
    {
        if (expected is { } value)
            Assert.Equal(value, member.GetDouble(), 0.001);
        else
            Assert.Equal(JsonValueKind.Null, member.ValueKind);
    }

    /// <inheritdoc cref="AssertMeasurement(double?, JsonElement, string)"/>
    private static void AssertMeasurement(int? expected, JsonElement body, string name)
    {
        var member = body.GetProperty(name);

        if (expected is { } value)
            Assert.Equal(value, member.GetInt32());
        else
            Assert.Equal(JsonValueKind.Null, member.ValueKind);
    }

    /// <summary>
    /// The answer's warnings as code to severity. Every code is the registry's and every severity
    /// is the one the registry declares, because the interface keys its dictionary and its download
    /// gate on exactly these two (AD-18, AD-22).
    /// </summary>
    private static Dictionary<string, string> Warnings(JsonElement body)
    {
        var warnings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var warning in body.GetProperty("warnings").EnumerateArray())
        {
            var code = warning.GetProperty("code").GetString()!;
            var severity = warning.GetProperty("severity").GetString()!;

            Assert.True(DiagnosticCodes.IsDeclared(code), code);
            Assert.Equal(SeverityName.Of(DiagnosticCodes.SeverityOf(code)), severity);
            warnings.Add(code, severity);
        }

        return warnings;
    }

    /// <summary>
    /// Collects every member name anywhere in the answer that states a pace, a share or a
    /// difference — the three kinds of value the contract may not carry (AD-14). Recursive on
    /// purpose: a derived value added inside a nested record would pass a check on the root alone.
    /// </summary>
    private static void Walk(JsonElement element, List<string> derived)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    if (member.Name.Contains("pace", StringComparison.OrdinalIgnoreCase)
                        || member.Name.Contains("fraction", StringComparison.OrdinalIgnoreCase)
                        || member.Name.Contains("percent", StringComparison.OrdinalIgnoreCase)
                        || member.Name.Contains("delta", StringComparison.OrdinalIgnoreCase)
                        || member.Name.Contains("difference", StringComparison.OrdinalIgnoreCase))
                    {
                        derived.Add(member.Name);
                    }

                    Walk(member.Value, derived);
                }

                break;

            case JsonValueKind.Array:
                // One element is enough: a series holds numbers, and a member name can only
                // appear on an object inside it.
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        Walk(item, derived);
                }

                break;
        }
    }

    private Task<HttpResponseMessage> RepairAsync(
        byte[] original,
        byte[] track,
        string? mode = null,
        string fileName = "upload.bin")
    {
        var content = Upload(original, track, fileName);
        if (mode is not null)
            content.Add(new StringContent(mode), RepairEndpoint.TrackLengthModeField);

        return Host.Client.PostAsync(RepairEndpoint.Route, content);
    }

    /// <summary>
    /// An arrangement of file parts of the test's own choosing — one per role, two in one role,
    /// three altogether, or none named for a role at all.
    /// </summary>
    private async Task<HttpResponseMessage> PostAsync(params (byte[] Bytes, string Field)[] parts)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (bytes, field) in parts)
            content.Add(Body(bytes), field, "upload.bin");

        return await Host.Client.PostAsync(RepairEndpoint.Route, content);
    }

    private static (byte[] Bytes, string Field) Part(byte[] bytes, string field) => (bytes, field);

    /// <summary>
    /// The two files, each in the part named for its role, and both lying about their type in every
    /// way a browser can: the content type and the file name decide nothing (AD-13).
    /// </summary>
    private static MultipartFormDataContent Upload(byte[] original, byte[] track, string fileName = "upload.bin")
    {
        var content = new MultipartFormDataContent();
        content.Add(Body(original), FileRole.Original, fileName);
        content.Add(Body(track), FileRole.Track, fileName);
        return content;
    }

    private static ByteArrayContent Body(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return part;
    }

    /// <summary>The body as a detached element, so the document can be disposed with it.</summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(ProblemDetailsFactory.MediaType, response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadJsonAsync(response);
        var code = problem.GetProperty("code").GetString()!;

        Assert.True(DiagnosticCodes.IsDeclared(code), code);
        Assert.Equal((int)response.StatusCode, problem.GetProperty("status").GetInt32());
        return code;
    }
}
