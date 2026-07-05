namespace Ecad.Rendering.Tests;

/// <summary>A fresh temp directory per test, recursively deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ecad-symtest-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
