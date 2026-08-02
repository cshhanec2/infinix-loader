using System.IO.Compression;

namespace LoaderNL.Core.Services;

public sealed record NeverloseLibrarySeedResult(
    int Installed,
    int Updated,
    int Unchanged);

public sealed class NeverloseLibrarySeeder(LogService logService)
{
    private const int MaximumLibraryCount = 512;
    private const long MaximumLibrarySize = 2 * 1024 * 1024;
    private const long MaximumArchiveSize = 16 * 1024 * 1024;

    private readonly LogService _logService = logService;

    public NeverloseLibrarySeedResult Prepare(
        string gameExecutable,
        Stream librariesZip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameExecutable);
        ArgumentNullException.ThrowIfNull(librariesZip);

        var gameDirectory = Path.GetDirectoryName(gameExecutable);
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new InvalidOperationException(
                "Не удалось определить папку игры для установки библиотек Neverlose.");
        }

        var librariesDirectory = Path.Combine(
            gameDirectory,
            "nl_cloud",
            "scripts",
            "libraries");
        var preparedFiles = ReadArchive(librariesZip, librariesDirectory);

        Directory.CreateDirectory(librariesDirectory);

        var installed = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var file in preparedFiles)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(file.TargetPath) ??
                librariesDirectory);

            if (File.Exists(file.TargetPath))
            {
                if (File.ReadAllBytes(file.TargetPath)
                    .AsSpan()
                    .SequenceEqual(file.Contents))
                {
                    unchanged++;
                    continue;
                }

                WriteAtomically(file.TargetPath, file.Contents);
                updated++;
                continue;
            }

            WriteAtomically(file.TargetPath, file.Contents);
            installed++;
        }

        _logService.Success(
            $"Библиотеки Neverlose подготовлены: " +
            $"установлено {installed}, обновлено {updated}, без изменений {unchanged}.");

        return new NeverloseLibrarySeedResult(
            installed,
            updated,
            unchanged);
    }

    private static IReadOnlyList<PreparedLibrary> ReadArchive(
        Stream librariesZip,
        string librariesDirectory)
    {
        using var archive = new ZipArchive(
            librariesZip,
            ZipArchiveMode.Read,
            leaveOpen: true);

        var rootPath = Path.GetFullPath(librariesDirectory);
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        var preparedFiles = new List<PreparedLibrary>();
        var targetPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) ||
                !entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (preparedFiles.Count >= MaximumLibraryCount)
            {
                throw new InvalidDataException(
                    $"Архив Neverlose содержит больше {MaximumLibraryCount} Lua-библиотек.");
            }

            if (entry.Length > MaximumLibrarySize)
            {
                throw new InvalidDataException(
                    $"Библиотека {entry.FullName} превышает допустимый размер.");
            }

            totalSize += entry.Length;
            if (totalSize > MaximumArchiveSize)
            {
                throw new InvalidDataException(
                    "Распакованный архив библиотек Neverlose слишком большой.");
            }

            var targetPath = ResolveTargetPath(
                rootPath,
                rootPrefix,
                entry.FullName);

            if (!targetPaths.Add(targetPath))
            {
                throw new InvalidDataException(
                    $"Архив содержит повторяющийся путь библиотеки: {entry.FullName}.");
            }

            using var source = entry.Open();
            using var buffer = new MemoryStream(
                entry.Length <= int.MaxValue
                    ? (int)entry.Length
                    : 0);
            source.CopyTo(buffer);

            if (buffer.Length != entry.Length)
            {
                throw new InvalidDataException(
                    $"Библиотека {entry.FullName} распакована не полностью.");
            }

            preparedFiles.Add(
                new PreparedLibrary(
                    targetPath,
                    buffer.ToArray()));
        }

        if (preparedFiles.Count == 0)
        {
            throw new InvalidDataException(
                "Архив Neverlose не содержит Lua-библиотек.");
        }

        return preparedFiles;
    }

    private static string ResolveTargetPath(
        string rootPath,
        string rootPrefix,
        string entryName)
    {
        try
        {
            var normalizedEntryName = entryName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(
                Path.Combine(rootPath, normalizedEntryName));

            if (!targetPath.StartsWith(
                    rootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Недопустимый путь библиотеки в архиве: {entryName}.");
            }

            return targetPath;
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"Недопустимый путь библиотеки в архиве: {entryName}.",
                exception);
        }
    }

    private static void WriteAtomically(string path, byte[] contents)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllBytes(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record PreparedLibrary(
        string TargetPath,
        byte[] Contents);
}
