using System.Text.Json;
using System.Text.Json.Serialization;
using FalloutLoc.Core.IO;

namespace FalloutLoc.Core.Configuration;

public sealed class ProjectConfigurationStore(IWorkspaceFileSystem workspaceFileSystem)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public ProjectConfiguration Load(string path)
    {
        var json = workspaceFileSystem.ReadAllText(path);
        return JsonSerializer.Deserialize<ProjectConfiguration>(json, JsonOptions)
            ?? throw new InvalidDataException($"Configuration is empty or invalid: {path}");
    }

    public void Save(string path, ProjectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var json = JsonSerializer.Serialize(configuration, JsonOptions) + Environment.NewLine;
        workspaceFileSystem.WriteAllTextAtomic(path, json);
    }
}
