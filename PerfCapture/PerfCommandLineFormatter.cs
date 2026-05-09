using System.Text;

namespace PerfCapture;

public static class PerfCommandLineFormatter
{
    public static string Format(PerfCommandPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var parts = new List<string>();
        if (plan.Environment.Count > 0)
        {
            parts.Add("env");
            foreach (var (key, value) in plan.Environment)
            {
                if (value is not null)
                    parts.Add($"{key}={value}");
            }
        }

        parts.Add(plan.FileName.Value);
        parts.AddRange(plan.Arguments);

        return string.Join(' ', parts.Select(Quote));
    }

    static string Quote(string value)
    {
        if (value.Length == 0)
            return "''";

        if (value.All(IsSafeShellCharacter))
            return value;

        var builder = new StringBuilder("'");
        foreach (var character in value)
        {
            if (character == '\'')
                builder.Append("'\\''");
            else
                builder.Append(character);
        }

        builder.Append('\'');
        return builder.ToString();
    }

    static bool IsSafeShellCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) ||
               character is '_' or '-' or '.' or '/' or ':' or ',' or '=' or '+';
    }
}
