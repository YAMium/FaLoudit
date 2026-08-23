using System.Text;

namespace FalloutLoc.Core.IO;

public sealed class SourceFileSystem(ReadOnlySourceGuard guard) : ISourceFileSystem
{
    public bool FileExists(string path) => File.Exists(guard.EnsureReadableSource(path));

    public bool DirectoryExists(string path) => Directory.Exists(guard.EnsureReadableSource(path));

    public string ReadAllText(string path) =>
        File.ReadAllText(guard.EnsureReadableSource(path), Encoding.UTF8);

    public IReadOnlyList<string> ReadAllLines(string path) =>
        File.ReadAllLines(guard.EnsureReadableSource(path), Encoding.UTF8);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(guard.EnsureReadableSource(path));

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(guard.EnsureReadableSource(path), "*", SearchOption.TopDirectoryOnly);

    public IEnumerable<string> EnumerateFiles(string path) =>
        Directory.EnumerateFiles(guard.EnsureReadableSource(path), "*", SearchOption.TopDirectoryOnly);
}
