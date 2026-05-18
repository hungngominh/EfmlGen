using System;
using System.Collections.Generic;
using EfmlGen.Core;

namespace EfmlGen.Cli;

internal static class WarningPrinter
{
    /// <summary>
    /// Print collision warnings tới console. Trả về true nếu có ít nhất 1 Error.
    /// </summary>
    public static bool Print(IReadOnlyList<CollisionDetector.Warning> warnings)
    {
        var hasError = false;
        if (warnings.Count == 0) return false;

        Console.WriteLine();
        Console.WriteLine($"--- {warnings.Count} validation issue(s) ---");
        foreach (var w in warnings)
        {
            var prefix = w.Severity == CollisionDetector.Severity.Error ? "[error]  " : "[warning]";
            Console.WriteLine($"  {prefix} {w.Message}");
            if (w.Severity == CollisionDetector.Severity.Error) hasError = true;
        }
        return hasError;
    }
}
