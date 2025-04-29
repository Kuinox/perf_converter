using System.Text;

namespace PerfConverter;

public class WrappingWriter : TextWriter
{
    private readonly TextWriter _original;
    private const int MaxWidth = 90;

    public WrappingWriter(TextWriter original)
    {
        _original = original;
    }

    public override Encoding Encoding => _original.Encoding;

    public override void WriteLine(string? value)
    {
        if (value == null)
        {
            _original.WriteLine();
            return;
        }

        while (value.Length > MaxWidth)
        {
            _original.WriteLine(value.Substring(0, MaxWidth));
            value = value.Substring(MaxWidth);
        }

        _original.WriteLine(value);
        _original.Flush();
    }
}
