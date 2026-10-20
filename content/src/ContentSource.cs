namespace Game.Content;

/// <summary>Откуда читать файлы content/: каталог на диске или ресурсы движка (M5).</summary>
public interface IContentSource
{
    /// <summary>Текст файла по пути относительно content/.</summary>
    string Read(string relativePath);

    /// <summary>Файлы каталога с расширением, пути относительно content/, по порядку ordinal.</summary>
    IReadOnlyList<string> List(string relativeDir, string extension);
}

/// <summary>content/ в файловой системе.</summary>
public sealed class DirectorySource : IContentSource
{
    private readonly string _root;

    public DirectorySource(string root) => _root = root;

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(_root, relativePath));

    public IReadOnlyList<string> List(string relativeDir, string extension) =>
        Directory.GetFiles(Path.Combine(_root, relativeDir), "*" + extension)
            .Select(f => relativeDir + "/" + Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
}
