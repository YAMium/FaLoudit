using System.Text;

namespace FalloutLoc.Core.IO;

public sealed class WorkspaceFileSystem(ReadOnlySourceGuard guard) : IWorkspaceFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string ReadAllText(string path)
    {
        var destination = guard.EnsureWritableDestination(path);
        return File.ReadAllText(destination, Encoding.UTF8);
    }

    public void WriteAllTextAtomic(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var destination = PrepareFileDestination(path);
        var directory = Path.GetDirectoryName(destination)!;

        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        guard.EnsureWritableDestination(temporary);
        try
        {
            File.WriteAllText(temporary, content, Utf8NoBom);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public string PrepareFileDestination(string path)
    {
        var destination = guard.EnsureWritableDestination(path);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException($"Destination has no parent directory: {destination}", nameof(path));
        guard.EnsureWritableDestination(Path.Combine(directory, ".directory-probe"));
        Directory.CreateDirectory(directory);
        return destination;
    }

    public void CopyFileWithinWorkspace(string sourcePath, string destinationPath)
    {
        var source = guard.EnsureWritableDestination(sourcePath);
        var destination = PrepareFileDestination(destinationPath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Workspace source file does not exist.", source);
        }

        File.Copy(source, destination, overwrite: false);
    }

    public void ReplaceFileAtomic(string stagedPath, string destinationPath)
    {
        var staged = guard.EnsureWritableDestination(stagedPath);
        var destination = PrepareFileDestination(destinationPath);
        if (!File.Exists(staged))
        {
            throw new FileNotFoundException("Staged workspace file does not exist.", staged);
        }

        File.Move(staged, destination, overwrite: true);
    }

    public void DeleteFileIfExists(string path)
    {
        var destination = guard.EnsureWritableDestination(path);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }
    }
}
