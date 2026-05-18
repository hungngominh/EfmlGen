using System;

namespace EfmlGen.Templates;

public sealed class GenerationContext
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Provider { get; init; } = "Npgsql";  // "Npgsql" | "SqlServer"
    public string? ConnectionString { get; init; }

    public string FormattedTimestamp =>
        Timestamp.ToString("M/d/yyyy h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);
}
