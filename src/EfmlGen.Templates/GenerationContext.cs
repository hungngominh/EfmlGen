using System;

namespace EfmlGen.Templates;

public sealed class GenerationContext
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Provider { get; init; } = "Npgsql";  // "Npgsql" | "SqlServer"
    public string? ConnectionString { get; init; }

    /// <summary>
    /// When true, EntityEmitter generates static GetByXxx convenience methods for each
    /// non-PK index of the entity. Default false to match Devart Entity Developer's
    /// default template.
    /// </summary>
    public bool GenerateIndexMethods { get; init; } = false;

    public string FormattedTimestamp =>
        Timestamp.ToString("M/d/yyyy h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);
}
