using System.Collections.Generic;

namespace EfmlGen.Core;

/// <summary>
/// Escape C# reserved keyword bằng prefix `@`. KHÔNG đổi casing, KHÔNG strip ký tự.
/// Mục đích: identifier ở code compile được khi DB có column/table tên trùng keyword
/// (vd column "class", "event", "operator", "is"...).
/// </summary>
public static class CsKeywords
{
    private static readonly HashSet<string> Reserved = new(System.StringComparer.Ordinal)
    {
        // C# reserved keywords (C# 12)
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    /// <summary>
    /// Trả về `@name` nếu name là C# reserved keyword, ngược lại trả về name nguyên bản.
    /// </summary>
    public static string Escape(string name) =>
        string.IsNullOrEmpty(name) || !Reserved.Contains(name) ? name : "@" + name;

    public static bool IsReserved(string name) =>
        !string.IsNullOrEmpty(name) && Reserved.Contains(name);

    /// <summary>
    /// Convenience wrapper: sanitize tên thành C# identifier hợp lệ (nếu cần), rồi
    /// escape reserved keyword bằng `@`. Dùng cho mọi vị trí emit identifier vào file .cs.
    /// Tên gốc hợp lệ + không phải keyword → trả về nguyên.
    /// </summary>
    public static string SafeId(string name) =>
        Escape(IdentifierSanitizer.SafeName(name));
}
