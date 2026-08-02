using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoaderNL.Core.Services;

public sealed class NeverloseProfileSeeder(LogService logService)
{
    private const string AvatarRelativePath = "nl_cloud/avatar.png";
    private static readonly byte[] PngSignature =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly LogService _logService = logService;

    public void Prepare(
        string gameExecutable,
        Stream avatarPng,
        Stream globalDataTemplate,
        string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameExecutable);
        ArgumentNullException.ThrowIfNull(avatarPng);
        ArgumentNullException.ThrowIfNull(globalDataTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var gameDirectory = Path.GetDirectoryName(gameExecutable);
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new InvalidOperationException(
                "Не удалось определить папку игры для подготовки профиля Neverlose.");
        }

        var avatarBytes = ReadAllBytes(avatarPng);
        if (!avatarBytes.AsSpan().StartsWith(PngSignature))
        {
            throw new InvalidDataException(
                "Встроенный аватар Neverlose должен быть PNG-файлом.");
        }

        var templateBytes = ReadAllBytes(globalDataTemplate);
        var nlCloudDirectory = Path.Combine(gameDirectory, "nl_cloud");
        var avatarPath = Path.Combine(nlCloudDirectory, "avatar.png");
        var globalDataPath = Path.Combine(nlCloudDirectory, "global_data.json");

        Directory.CreateDirectory(nlCloudDirectory);

        var root = LoadGlobalData(globalDataPath, templateBytes);
        root["avatar"] = AvatarRelativePath;
        root["username"] = username;

        WriteAtomically(avatarPath, avatarBytes);
        WriteAtomically(
            globalDataPath,
            JsonSerializer.SerializeToUtf8Bytes(root, SerializerOptions));

        _logService.Success(
            $"Профиль Neverlose подготовлен: {nlCloudDirectory}");
    }

    private JsonObject LoadGlobalData(
        string globalDataPath,
        byte[] templateBytes)
    {
        if (File.Exists(globalDataPath))
        {
            try
            {
                return JsonNode.Parse(File.ReadAllBytes(globalDataPath))
                           as JsonObject
                       ?? throw new JsonException(
                           "Корневой элемент global_data.json не является объектом.");
            }
            catch (JsonException exception)
            {
                _logService.Warning(
                    $"Не удалось прочитать существующий global_data.json: " +
                    $"{exception.Message}. Используется встроенный шаблон.");
            }
        }

        return JsonNode.Parse(templateBytes) as JsonObject
               ?? throw new InvalidDataException(
                   "Встроенный шаблон global_data.json должен содержать JSON-объект.");
    }

    private static byte[] ReadAllBytes(Stream source)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
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
}
