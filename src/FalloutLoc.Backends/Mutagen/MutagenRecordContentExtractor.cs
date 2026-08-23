using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;
using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Plugins.Records;

namespace FalloutLoc.Backends.Mutagen;

internal sealed class MutagenRecordContentExtractor : IRecordContentExtractor<IMajorRecordGetter>
{
    public IReadOnlyList<RawRecordContent> Extract(IMajorRecordGetter record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var contents = new List<RawRecordContent>();

        if (record is IScriptGetter script)
        {
            AddSource(contents, "Fields.SourceCode", script.Fields);
        }

        if (record is IDialogResponsesGetter dialog)
        {
            AddSource(contents, "BeginScript.SourceCode", dialog.BeginScript);
            AddSource(contents, "EndScript.SourceCode", dialog.EndScript);
        }

        if (record is ITerminalGetter terminal)
        {
            for (var index = 0; index < terminal.MenuItems.Count; index++)
            {
                AddSource(
                    contents,
                    $"MenuItems[{index}].EmbeddedScript.SourceCode",
                    terminal.MenuItems[index].EmbeddedScript);
            }
        }

        if (record is IQuestGetter quest)
        {
            foreach (var stage in quest.Stages)
            {
                for (var index = 0; index < stage.LogEntries.Count; index++)
                {
                    AddSource(
                        contents,
                        $"Stages[index={stage.Index}].LogEntries[{index}].EmbeddedScript.SourceCode",
                        stage.LogEntries[index].EmbeddedScript);
                }
            }
        }

        if (record is IPerkGetter perk)
        {
            for (var index = 0; index < perk.Effects.Count; index++)
            {
                if (perk.Effects[index] is IPerkEntryPointAddActivateChoiceGetter choice)
                {
                    AddSource(contents, $"Effects[{index}].Script.SourceCode", choice.Script);
                }
            }
        }

        if (record is IPackageGetter package)
        {
            AddPackageEvent(contents, "OnBegin", package.OnBegin);
            AddPackageEvent(contents, "OnEnd", package.OnEnd);
            AddPackageEvent(contents, "OnChange", package.OnChange);
        }

        switch (record)
        {
            case IPlacedObjectGetter placed:
                AddPatrol(contents, placed.Patrol);
                break;
            case IPlacedBeamGetter placed:
                AddPatrol(contents, placed.Patrol);
                break;
            case IPlacedCreatureGetter placed:
                AddPatrol(contents, placed.Patrol);
                break;
            case IPlacedGrenadeGetter placed:
                AddPatrol(contents, placed.Patrol);
                break;
            case IPlacedMissileGetter placed:
                AddPatrol(contents, placed.Patrol);
                break;
            case IPlacedNpcGetter placed:
                AddPatrol(contents, placed.Patrol);
                break;
        }

        return contents;
    }

    private static void AddPackageEvent(
        ICollection<RawRecordContent> contents,
        string semanticPath,
        IPackageEventGetter? packageEvent)
    {
        if (packageEvent is not null)
        {
            AddSource(contents, $"{semanticPath}.EmbeddedScript.SourceCode", packageEvent.EmbeddedScript);
        }
    }

    private static void AddPatrol(ICollection<RawRecordContent> contents, IPatrolDataGetter? patrol)
    {
        if (patrol is not null)
        {
            AddSource(contents, "Patrol.EmbeddedScript.SourceCode", patrol.EmbeddedScript);
        }
    }

    private static void AddSource(
        ICollection<RawRecordContent> contents,
        string semanticPath,
        IScriptFieldsGetter? fields)
    {
        if (fields is null || string.IsNullOrEmpty(fields.SourceCode))
        {
            return;
        }

        contents.Add(new RawRecordContent(
            semanticPath,
            RecordContentSourceKind.EmbeddedScriptSource,
            fields.SourceCode.TrimEnd('\0')));
    }
}