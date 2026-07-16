namespace Ecad.Reports.LayoutSchema;

/// <summary>
/// The flattened data a report Builder hands to LayoutRenderer: scalar values for header/footer/field
/// regions, plus named row-sources for repeating tables. Keeps the renderer decoupled from domain
/// models — a Builder is the only code that ever touches Connection/Cable/Part/... directly; the
/// renderer only ever resolves a dotted DataFieldKey/DataSourceKey against this context.
/// </summary>
public sealed class ReportDataContext
{
    private readonly Dictionary<string, object?> _scalars = new();
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> _tables = new();

    public void SetScalar(string key, object? value) => _scalars[key] = value;

    public object? GetScalar(string key) => _scalars.GetValueOrDefault(key);

    public void SetTable(string key, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => _tables[key] = rows;

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetTable(string key) =>
        _tables.TryGetValue(key, out var rows) ? rows : [];
}
