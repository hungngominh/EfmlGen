using System;
using System.IO;
using System.Text;

namespace EfmlGen.Wpf.Services;

/// <summary>
/// TextWriter chuyển mỗi line về một callback (UI dispatcher).
/// Dùng để Console.SetOut() trong khi gọi CLI helpers, log streamed real-time tới TextBox.
/// </summary>
public sealed class CallbackTextWriter : TextWriter
{
    private readonly Action<string> _onLine;
    private readonly StringBuilder _buffer = new();

    public CallbackTextWriter(Action<string> onLine) => _onLine = onLine;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\r') return;
        if (value == '\n')
        {
            _onLine(_buffer.ToString());
            _buffer.Clear();
        }
        else
        {
            _buffer.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (var ch in value) Write(ch);
    }

    public override void WriteLine(string? value)
    {
        _onLine((_buffer.Length > 0 ? _buffer.ToString() : "") + (value ?? ""));
        _buffer.Clear();
    }
}
