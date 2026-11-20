using FitFix.Core;
using FitFix.Core.Diagnostics;
using FitFix.Core.Tests.Fixtures;

namespace FitFix.Core.Tests;

public class RepairServiceTests
{
    private static RepairResult RepairSyntheticRun(bool withGpsFailure = true, bool withAnyGps = true)
    {
        var run = SyntheticRun.Create(withGpsFailure, withAnyGps);
        return RepairService.Repair(run.Original, run.ReferenceTrack);
    }

    [Fact]
    public void Total_distance_equals_track_length()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;
        var result = RepairService.Repair(run.Original, track);

        // FR-6: total_distance = довжина Еталонного треку (±0,5 %).
        Assert.Equal(track.Length, result.Report.RepairedDistanceMeters, track.Length * 0.005);
    }

    [Fact]
    public void Timestamps_and_elapsed_time_are_preserved()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        Assert.Equal(run.Original.Records.Count, result.Records.Count);
        Assert.Equal(
            run.Original.Records.Select(r => r.Timestamp),
            result.Records.Select(r => r.Timestamp));
        Assert.Equal(run.Original.ElapsedSeconds, result.Report.ElapsedSeconds, 1.0);
    }

    [Fact]
    public void Distance_is_monotonically_non_decreasing()
    {
        var result = RepairSyntheticRun();

        for (var i = 1; i < result.Records.Count; i++)
            Assert.True(
                result.Records[i].Distance >= result.Records[i - 1].Distance,
                $"Дистанція зменшилась на записі {i}: {result.Records[i - 1].Distance} → {result.Records[i].Distance}");
    }

    [Fact]
    public void Sensor_values_are_carried_over_unchanged()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        for (var i = 0; i < result.Records.Count; i++)
        {
            Assert.Equal(run.Original.Records[i].HeartRate, result.Records[i].HeartRate);
            Assert.Equal(run.Original.Records[i].Cadence, result.Records[i].Cadence);
            Assert.Equal(run.Original.Records[i].Temperature, result.Records[i].Temperature);
        }
    }

    [Fact]
    public void Gps_stall_is_detected_without_hardcoded_cutoff()
    {
        var result = RepairSyntheticRun();

        Assert.Contains(DiagnosticCodes.GpsStallDetected, result.Report.Warnings);
        Assert.NotNull(result.Report.GpsStallAtSeconds);
        // Детектор має знайти збій біля реального моменту, а не за жорстким порогом.
        Assert.InRange(result.Report.GpsStallAtSeconds!.Value, SyntheticRun.GpsFailureSeconds - 30, SyntheticRun.GpsFailureSeconds + 120);
    }

    [Fact]
    public void Measured_zone_pace_matches_the_athlete_within_five_percent()
    {
        var result = RepairSyntheticRun();
        var measuredUntil = result.Report.MeasuredUntilSeconds;

        Assert.True(measuredUntil > 1000, $"Надійна зона надто коротка: {measuredUntil} с");

        var start = result.Records[0];
        var endIndex = (int)measuredUntil;
        var end = result.Records[endIndex];
        var averageSpeed = (end.Distance - start.Distance) / (end.Timestamp - start.Timestamp).TotalSeconds;

        // SM-1: темп у Надійній зоні відрізняється від реального ≤ 5 %.
        Assert.Equal(SyntheticRun.SpeedMetersPerSecond, averageSpeed, SyntheticRun.SpeedMetersPerSecond * 0.05);
    }

    [Fact]
    public void Zero_power_from_broken_gps_is_not_carried_over()
    {
        var result = RepairSyntheticRun();

        var afterFailure = result.Records
            .Where(r => (r.Timestamp - result.Records[0].Timestamp).TotalSeconds > SyntheticRun.GpsFailureSeconds + 30)
            .ToArray();

        Assert.All(afterFailure, record => Assert.Null(record.Power));

        var beforeFailure = result.Records
            .Where(r => (r.Timestamp - result.Records[0].Timestamp).TotalSeconds < SyntheticRun.GpsFailureSeconds - 30)
            .ToArray();

        Assert.All(beforeFailure, record => Assert.Equal((ushort)200, record.Power));
    }

    [Fact]
    public void Altitude_comes_from_track_shifted_to_the_original_start()
    {
        var run = SyntheticRun.Create();
        var result = RepairService.Repair(run.Original, run.ReferenceTrack);

        // Трек зсунуто на −400.8 м; після вирівнювання старт має збігатись з Оригіналом.
        Assert.NotNull(result.Records[0].Altitude);
        Assert.Equal(SyntheticRun.OriginalStartAltitude, result.Records[0].Altitude!.Value, 1.0);
    }

    [Fact]
    public void Activity_without_any_gps_is_fully_estimated()
    {
        var result = RepairSyntheticRun(withGpsFailure: false, withAnyGps: false);

        Assert.Contains(DiagnosticCodes.NoOriginalGps, result.Report.Warnings);
        Assert.Equal(0, result.Report.MeasuredUntilSeconds);
        Assert.Equal(result.Report.TrackLengthMeters, result.Report.RepairedDistanceMeters, 1.0);

        // Дистанція розподілена рівномірно в часі.
        var middle = result.Records[result.Records.Count / 2];
        Assert.Equal(result.Report.TrackLengthMeters / 2, middle.Distance, result.Report.TrackLengthMeters * 0.02);
    }

    [Fact]
    public void Track_from_another_city_raises_a_warning()
    {
        var run = SyntheticRun.Create();
        var shifted = run.TrackPoints
            .Select(p => new TrackPoint(new GeoPoint(p.Position.Latitude + 1.0, p.Position.Longitude + 1.0), p.Altitude))
            .ToArray();

        var result = RepairService.Repair(run.Original, ReferenceTrack.FromPoints(shifted, SyntheticRun.Options));

        Assert.Contains(DiagnosticCodes.TrackFarFromOriginal, result.Report.Warnings);
    }

    [Fact]
    public void Repair_of_a_two_hour_activity_takes_less_than_three_seconds()
    {
        var run = SyntheticRun.Create();
        var track = run.ReferenceTrack;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        RepairService.Repair(run.Original, track);
        stopwatch.Stop();

        // SM-3: обробка активності на 2 години — менше 3 с.
        Assert.True(stopwatch.Elapsed.TotalSeconds < 3, $"Обробка зайняла {stopwatch.Elapsed.TotalSeconds:F2} с");
    }
}
