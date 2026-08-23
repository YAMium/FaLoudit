using System.Reflection;
using System.Text.Json;

namespace FalloutLoc.Cli.Tests;

public sealed class ProgramTests
{
    private static readonly object ConsoleLock = new();

    [Fact]
    public void VersionCommandPrintsProductVersion()
    {
        var result = Invoke("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0.4.1", result.Output.Trim());
    }

    [Fact]
    public void HelpIncludesProductionCommands()
    {
        var result = Invoke("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("faloudit index [--status | --rebuild | --reparse]", result.Output);
        Assert.Contains("--source-language <tag> --target-language <tag>", result.Output);
        Assert.Contains("faloudit edid <editor-id>", result.Output);
        Assert.Contains("faloudit form <form-id|form-key>", result.Output);
        Assert.Contains("faloudit analyze <text>", result.Output);
        Assert.Contains("faloudit content <text>", result.Output);
        Assert.Contains("faloudit coverage [--issues <n>]", result.Output);
        Assert.Contains("faloudit explain <form-key>", result.Output);
        Assert.Contains("faloudit report <regressions|untranslated>", result.Output);
        Assert.Contains("faloudit compare <baseline-snapshot> <current-snapshot>", result.Output);
    }

    [Fact]
    public void JsonSuccessEnvelopeIsVersionedAndPreservesCommandPayload()
    {
        var output = InvokeJsonWriter("find", 0, new
        {
            success = true,
            query = "needle",
            results = new[] { new { formKey = "000123:Base.esm" } },
        });

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("0.4.1", root.GetProperty("applicationVersion").GetString());
        Assert.Equal("find", root.GetProperty("command").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("needle", root.GetProperty("query").GetString());
        Assert.Equal("000123:Base.esm", root.GetProperty("results")[0].GetProperty("formKey").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
    }

    [Fact]
    public void JsonFailureHasStableCodeAndLegacyExceptionType()
    {
        var result = Invoke("not-a-command", "--json");

        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.Equal(1, result.ExitCode);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("invalidArguments", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("ArgumentException", root.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public void InvalidNumericOptionUsesInvalidArgumentsCode()
    {
        var result = Invoke("find", "text", "--limit", "not-a-number", "--json");

        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
            "invalidArguments",
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ConfigureRequiresExplicitLanguagePairBeforeDiscovery()
    {
        var result = Invoke("configure", @"C:\missing", "--json");

        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("invalidArguments",
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("--source-language", document.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    private static (int ExitCode, string Output) Invoke(params string[] args)
    {
        lock (ConsoleLock)
        {
            var original = Console.Out;
            using var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                return (Program.Main(args), output.ToString());
            }
            finally
            {
                Console.SetOut(original);
            }
        }
    }

    private static string InvokeJsonWriter(string command, int exitCode, object payload)
    {
        lock (ConsoleLock)
        {
            var original = Console.Out;
            using var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                var writer = typeof(Program).GetMethod("WriteJson", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("JSON contract writer was not found.");
                writer.Invoke(null, [command, exitCode, payload, null]);
                return output.ToString();
            }
            finally
            {
                Console.SetOut(original);
            }
        }
    }
}
