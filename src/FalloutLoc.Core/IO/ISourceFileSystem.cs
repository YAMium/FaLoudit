namespace FalloutLoc.Core.IO;

public interface ISourceFileSystem : ISourceFileListing
{
    string ReadAllText(string path);

    IReadOnlyList<string> ReadAllLines(string path);

    byte[] ReadAllBytes(string path);
}
