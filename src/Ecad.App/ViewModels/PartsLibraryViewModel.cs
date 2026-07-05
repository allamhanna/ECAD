using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.Core.Models;
using Ecad.Data;
using Ecad.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace Ecad.App.ViewModels;

/// <summary>A Part plus its resolved manufacturer/supplier display names, for list/detail binding.</summary>
public sealed record PartListItem(Part Part, string? ManufacturerName, string? SupplierName);

/// <summary>
/// Browses the shared Library DB independently of any open project (same pattern as MainViewModel's
/// EPLAN import command) — opens its own connection, disposed when the window closes.
/// </summary>
public partial class PartsLibraryViewModel : ObservableObject, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PartRepository _parts;
    private List<Part> _allParts = [];
    private Dictionary<long, string> _organizationNames = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private PartListItem? _selectedPart;

    [ObservableProperty]
    private BitmapImage? _previewImage;

    public ObservableCollection<PartListItem> Parts { get; } = [];
    public ObservableCollection<PartPinTemplate> PinTemplates { get; } = [];
    public ObservableCollection<PartTerminalSpec> TerminalSpecs { get; } = [];
    public ObservableCollection<PartAccessory> Accessories { get; } = [];

    public PartsLibraryViewModel()
    {
        _connection = LibraryDatabase.Open();
        _parts = new PartRepository(_connection);
        Load();
    }

    [RelayCommand]
    private void Refresh() => Load();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedPartChanged(PartListItem? value)
    {
        PinTemplates.Clear();
        TerminalSpecs.Clear();
        Accessories.Clear();
        PreviewImage = null;
        if (value is null) return;

        foreach (var template in _parts.GetPartPinTemplates(value.Part.Id)) PinTemplates.Add(template);
        foreach (var spec in _parts.GetPartTerminalSpecs(value.Part.Id)) TerminalSpecs.Add(spec);
        foreach (var accessory in _parts.GetPartAccessories(value.Part.Id)) Accessories.Add(accessory);
        PreviewImage = ToBitmapImage(_parts.GetImage(value.Part.Id)?.ImageData);
    }

    private static BitmapImage? ToBitmapImage(byte[]? data)
    {
        if (data is null) return null;

        var image = new BitmapImage();
        using var stream = new MemoryStream(data);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // load fully now so the stream can be disposed
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Load()
    {
        _organizationNames = _parts.GetAllOrganizations().ToDictionary(o => o.Id, o => o.Name);
        _allParts = _parts.GetAllParts().ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        SelectedPart = null;
        Parts.Clear();

        var search = SearchText.Trim();
        var matches = search.Length == 0 ? _allParts : _allParts.Where(p => Matches(p, search));
        foreach (var part in matches)
            Parts.Add(ToListItem(part));
    }

    private bool Matches(Part part, string search) =>
        Contains(part.ExternalKey, search) || Contains(part.Description1, search) ||
        Contains(part.Description2, search) || Contains(part.TypeNumber, search) ||
        Contains(NameOf(part.ManufacturerId), search) || Contains(NameOf(part.SupplierId), search);

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private PartListItem ToListItem(Part part) => new(part, NameOf(part.ManufacturerId), NameOf(part.SupplierId));

    private string? NameOf(long? organizationId) =>
        organizationId is { } id && _organizationNames.TryGetValue(id, out var name) ? name : null;

    public void Dispose() => _connection.Dispose();
}
