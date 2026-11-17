using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Services;

public sealed class TaskRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _storagePath;

    public TaskRepository()
    {
        _storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyTool",
            "tasks.json"
        );
    }

    public IReadOnlyList<MacroTask> Load()
    {
        if (!File.Exists(_storagePath))
        {
            return Array.Empty<MacroTask>();
        }

        try
        {
            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<List<MacroTask>>(json, JsonOptions)
                ?? new List<MacroTask>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KeyTool task load failed: {ex}");
            return Array.Empty<MacroTask>();
        }
    }

    public void Save(IEnumerable<MacroTask> tasks)
    {
        var directory = Path.GetDirectoryName(_storagePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _storagePath + ".tmp";
        var json = JsonSerializer.Serialize(tasks, JsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _storagePath, true);
    }
}
