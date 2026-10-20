using System;
using System.Collections.Generic;
using System.Linq;
using Game.Content;
using Godot;

namespace Game.View;

/// <summary>content/ из ресурсов проекта (res://content) — работает и в редакторе, и в экспортированной игре.</summary>
public sealed class GodotContentSource : IContentSource
{
    private readonly string _root;

    public GodotContentSource(string root) => _root = root.TrimEnd('/');

    public string Read(string relativePath)
    {
        string path = $"{_root}/{relativePath}";
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Не удалось открыть {path}: {Godot.FileAccess.GetOpenError()}");
        return file.GetAsText();
    }

    public IReadOnlyList<string> List(string relativeDir, string extension) =>
        DirAccess.GetFilesAt($"{_root}/{relativeDir}")
            .Where(f => f.EndsWith(extension, StringComparison.Ordinal))
            .Select(f => $"{relativeDir}/{f}")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
}
