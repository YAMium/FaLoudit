namespace FalloutLoc.Core.IO;

public interface ISourceFileListing
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IEnumerable<string> EnumerateDirectories(string path);
    IEnumerable<string> EnumerateFiles(string path);

    IEnumerable<string> EnumerateFiles(string path, string extension) => EnumerateFiles(path)
        .Where(file => Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase));
}
