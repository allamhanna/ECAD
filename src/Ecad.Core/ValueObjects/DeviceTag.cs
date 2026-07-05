using System.Text;

namespace Ecad.Core.ValueObjects;

/// <summary>IEC 81346 device tag: =Function +Location -DeviceTag. Any segment may be absent.</summary>
public sealed record DeviceTag(string? Function, string? Location, string? Designation)
{
    public static DeviceTag Parse(string tag)
    {
        string? function = null, location = null, designation = null;
        var current = new StringBuilder();
        char? currentPrefix = null;

        void Flush()
        {
            if (currentPrefix is null) return;
            var value = current.ToString();
            switch (currentPrefix)
            {
                case '=': function = value; break;
                case '+': location = value; break;
                case '-': designation = value; break;
            }
            current.Clear();
        }

        foreach (var c in tag)
        {
            if (c is '=' or '+' or '-')
            {
                Flush();
                currentPrefix = c;
            }
            else if (!char.IsWhiteSpace(c) || current.Length > 0)
            {
                current.Append(c);
            }
        }
        Flush();

        return new DeviceTag(function, location, designation);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Function is { Length: > 0 }) sb.Append('=').Append(Function);
        if (Location is { Length: > 0 }) sb.Append('+').Append(Location);
        if (Designation is { Length: > 0 }) sb.Append('-').Append(Designation);
        return sb.ToString();
    }
}
