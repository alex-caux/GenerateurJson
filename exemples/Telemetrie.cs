namespace ModelesTelemetrie;

// Modele fourni par l'integrateur de la jauge de planeite, commente en anglais : grammaire anglaise, types
// numeriques bornes (byte, sbyte, short, ushort, long, float, double), struct a champs publics, record struct.
//   dotnet run --project src/GenerateurJson -- --source exemples/Telemetrie.cs --explain --seed 42 > NUL

/// <summary>Operating mode reported by the gauge.</summary>
public enum GaugeMode
{
    Auto,
    Manual,
    Maintenance,
    Simulation,
}

/// <summary>Flatness measurement batch sent every second by the gauge.</summary>
public sealed class FlatnessBatch
{
    public long SequenceNumber { get; set; } // auto-increment

    public Guid BatchId { get; set; }

    public string SensorTag { get; set; } = string.Empty; // pattern ^FLT-\d{3}$, required

    public DateTimeOffset Timestamp { get; set; } // UTC, not in the future

    public GaugeMode Mode { get; set; } // one of Auto, Manual, Maintenance

    public float StripSpeed { get; set; } // [m/s] between 0.5 and 25, 2 decimal places

    public ushort StatusCode { get; set; } // 0 = OK, 10 = WARN, 20 = FAULT

    public byte SignalQuality { get; set; } // 0..100

    public sbyte TiltDeg { get; set; } // between -45 and 45

    public short ActiveZones { get; set; } // even, from 2 to 24

    public string? Operator { get; set; } // optional, lower case, letters only, at most 12 characters

    public string FirmwareVersion { get; set; } = "1.0.0"; // pattern ^\d+\.\d+\.\d+$

    public bool Simulated { get; set; } // default: false

    public TimeSpan SamplingPeriod { get; set; } // from 1 to 60 seconds

    public DateOnly CalibrationDate { get; set; } // since 2023, in the past

    public Uri? DashboardUrl { get; set; }

    /// <summary>Flatness profile across the strip, in I-units.</summary>
    /// <remarks>4 to 8 items. Each item: between -50 and 50, 2 decimals.</remarks>
    public List<double> Profile { get; set; } = [];

    /// <summary>Zone temperatures keyed by zone name: exactly 3 entries; each value: between 20 and 80, 1 decimal.</summary>
    public Dictionary<string, float> ZoneTemperatures { get; set; } = [];

    public StripPosition Head { get; set; }

    public List<Alarm> Alarms { get; set; } = []; // at most 4 items, empty list allowed
}

/// <summary>Position of the strip head: a plain struct with public fields.</summary>
public struct StripPosition
{
    public double X; // between 0 and 2100 mm, 1 decimal

    public double Y; // between -5 and 5 mm, 2 decimals
}

/// <summary>Alarm raised by the gauge.</summary>
/// <param name="Code">Pattern ^A[0-9]{3}$.</param>
/// <param name="Severity">1 = info, 2 = warning, 3 = critical.</param>
/// <param name="Message">Optional, at most 80 characters.</param>
/// <param name="RaisedAt">UTC, in the past.</param>
public readonly record struct Alarm(string Code, int Severity, string? Message, DateTime RaisedAt);
