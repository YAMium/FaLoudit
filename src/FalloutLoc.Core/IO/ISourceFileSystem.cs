namespace FalloutLoc.Core.IO;

public interface ISourceFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    IReadOnlyList<string> ReadAllLines(string path);

    byte[] ReadAllBytes(string path);

    IEnumerable<string> EnumerateDirectories(string path);

    IEnumerable<string> EnumerateFiles(string path);
}
