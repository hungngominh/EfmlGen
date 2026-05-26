using System.Text;
using System.Text.RegularExpressions;

namespace EfmlGen.Core;

/// <summary>
/// Sanitize tên DB thành C# identifier hợp lệ khi tên gốc không hợp lệ.
/// Strategy port từ EntityFrameworkCore.Generator.ToLegalName:
///   1) Strip ký tự leading không phải letter/underscore (regex ^[^a-zA-Z_]+)
///   2) Nếu rỗng sau strip → prefix "Number" + tên gốc
///   3) Split trên non-word + underscore, ghép PascalCase
///
/// Quy ước: chỉ sanitize khi tên gốc KHÔNG là C# identifier hợp lệ.
/// Tên hợp lệ trả về nguyên — giữ đúng policy "DB sao thì code vậy" trong điều kiện cho phép.
///
/// DB-facing strings (HasColumnName, ToTable) vẫn dùng tên raw, KHÔNG qua sanitizer này.
/// </summary>
public static class IdentifierSanitizer
{
    private static readonly Regex LeadingNonAlpha = new(@"^[^a-zA-Z_]+", RegexOptions.Compiled);
    private static readonly Regex SplitNonWord = new(@"[\W_]+", RegexOptions.Compiled);

    /// <summary>
    /// Trả về tên đã sanitize nếu name không phải C# identifier hợp lệ;
    /// nếu hợp lệ thì trả về nguyên.
    /// </summary>
    public static string SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        if (IsValid(name)) return name;
        return ToLegalName(name);
    }

    /// <summary>
    /// C# identifier rules: first char là letter/_, các char sau là letter/digit/_.
    /// Không kiểm tra reserved keyword — đó là việc của CsKeywords.Escape.
    /// </summary>
    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var first = name[0];
        if (first != '_' && !char.IsLetter(first)) return false;
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c != '_' && !char.IsLetterOrDigit(c)) return false;
        }
        return true;
    }

    /// <summary>
    /// Port from ref/EntityFrameworkCore.Generator ModelGenerator.ToLegalName.
    /// </summary>
    public static string ToLegalName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var legal = LeadingNonAlpha.Replace(name, "");
        if (string.IsNullOrWhiteSpace(legal))
            legal = "Number" + name;

        // PascalCase split on non-word + underscore.
        var parts = SplitNonWord.Split(legal);
        if (parts.Length == 0) return legal;

        var sb = new StringBuilder(legal.Length);
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1) sb.Append(part[1..]);
        }
        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? legal : result;
    }
}
