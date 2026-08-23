namespace FalloutLoc.Core.IO;

public interface IWorkspaceFileSystem
{
    string ReadAllText(string path);

    void WriteAllTextAtomic(string path, string content);

    string PrepareFileDestination(string path);

    void CopyFileWithinWorkspace(string sourcePath, string destinationPath);

    void ReplaceFileAtomic(string stagedPath, string destinationPath);

    void DeleteFileIfExists(string path);
}
