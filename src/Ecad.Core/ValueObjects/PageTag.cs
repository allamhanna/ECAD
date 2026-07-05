using System.Text;

namespace Ecad.Core.ValueObjects;

/// <summary>IEC 81346 page tag: =Function +Location &amp;DocumentType /Page. Any segment may be absent.</summary>
public sealed record PageTag(string? Function, string? Location, string? DocumentType, string? Page)
{
    public static PageTag Parse(string tag)
    {
        string? function = null, location = null, documentType = null, page = null;
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
                case '&': documentType = value; break;
                case '/': page = value; break;
            }
            current.Clear();
        }

        foreach (var c in tag)
        {
            if (c is '=' or '+' or '&' or '/')
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

        return new PageTag(function, location, documentType, page);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Function is { Length: > 0 }) sb.Append('=').Append(Function);
        if (Location is { Length: > 0 }) sb.Append('+').Append(Location);
        if (DocumentType is { Length: > 0 }) sb.Append('&').Append(DocumentType);
        if (Page is { Length: > 0 }) sb.Append('/').Append(Page);
        return sb.ToString();
    }
}
