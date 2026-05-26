using System;
using System.Collections.Generic;

namespace EfmlGen.Core;

/// <summary>
/// Minimal English pluralization / singularization. Covers the common cases that
/// show up in DB table names; not a substitute for Humanizer if the schema uses
/// irregular nouns extensively.
/// </summary>
public static class Inflector
{
    private static readonly Dictionary<string, string> IrregularSingulars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["people"] = "person",
        ["men"] = "man",
        ["women"] = "woman",
        ["children"] = "child",
        ["teeth"] = "tooth",
        ["feet"] = "foot",
        ["geese"] = "goose",
        ["mice"] = "mouse",
        ["oxen"] = "ox"
    };

    private static readonly Dictionary<string, string> IrregularPlurals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = "people",
        ["man"] = "men",
        ["woman"] = "women",
        ["child"] = "children",
        ["tooth"] = "teeth",
        ["foot"] = "feet",
        ["goose"] = "geese",
        ["mouse"] = "mice",
        ["ox"] = "oxen"
    };

    /// <summary>Words that are the same in singular and plural form.</summary>
    private static readonly HashSet<string> Uncountable = new(StringComparer.OrdinalIgnoreCase)
    {
        "equipment", "information", "rice", "money", "species", "series", "fish",
        "sheep", "deer", "data", "media", "news"
    };

    public static string Singularize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (Uncountable.Contains(word)) return word;
        if (IrregularSingulars.TryGetValue(word, out var s)) return PreserveCase(word, s);

        var lower = word.ToLowerInvariant();

        // -ies → -y  (categories → category)
        if (lower.EndsWith("ies") && word.Length > 3)
            return word[..^3] + "y";

        // -ves → -f / -fe (knives → knife, leaves → leaf). We can't tell which without a list;
        // default to -fe for "-ives", else -f.
        if (lower.EndsWith("ves") && word.Length > 3)
            return lower.EndsWith("ives") ? word[..^3] + "fe" : word[..^3] + "f";

        // -ses, -xes, -zes, -shes, -ches → strip -es
        if (lower.EndsWith("ses") || lower.EndsWith("xes") || lower.EndsWith("zes")
            || lower.EndsWith("shes") || lower.EndsWith("ches"))
            return word[..^2];

        // Generic -s
        if (lower.EndsWith('s') && !lower.EndsWith("ss"))
            return word[..^1];

        return word;
    }

    public static string Pluralize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (Uncountable.Contains(word)) return word;
        if (IrregularPlurals.TryGetValue(word, out var p)) return PreserveCase(word, p);

        var lower = word.ToLowerInvariant();

        // Already plural-ish: ends with 's' but not 'ss', leave alone.
        if (lower.EndsWith('s') && !lower.EndsWith("ss") && !lower.EndsWith("us") && !lower.EndsWith("is"))
            return word;

        // consonant + y → ies (category → categories)
        if (lower.EndsWith('y') && word.Length > 1 && !IsVowel(lower[^2]))
            return word[..^1] + "ies";

        // -s, -x, -z, -ch, -sh → es
        if (lower.EndsWith('s') || lower.EndsWith('x') || lower.EndsWith('z')
            || lower.EndsWith("ch") || lower.EndsWith("sh"))
            return word + "es";

        // -f / -fe → ves
        if (lower.EndsWith("fe")) return word[..^2] + "ves";
        if (lower.EndsWith('f')) return word[..^1] + "ves";

        return word + "s";
    }

    private static bool IsVowel(char c) => "aeiou".IndexOf(c) >= 0;

    private static string PreserveCase(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original)) return replacement;
        // Preserve the casing pattern of the first character only.
        if (char.IsUpper(original[0]) && replacement.Length > 0)
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }
}
