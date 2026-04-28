using System.Text;

namespace KeyMouseSyncReplica;

public sealed class ConfigService
{
    private const string Section = "config";
    private readonly string _path;

    public ConfigService(string? path = null)
    {
        _path = path ?? ResolveConfigPath();
    }

    public string Path => _path;

    public AppConfig Load()
    {
        return new AppConfig
        {
            Dm = ReadValue("dm", "0"),
            Display = ReadValue("display", "normal"),
            Mouse = ReadValue("mouse", "0"),
            Keypad = ReadValue("keypad", "0"),
            Public = ReadValue("public", string.Empty),
            Mode = ReadValue("mode", "0")
        };
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);

        var builder = new StringBuilder();
        builder.AppendLine("[config]");
        builder.AppendLine($"dm={config.Dm}");
        builder.AppendLine($"display={config.Display}");
        builder.AppendLine($"mouse={config.Mouse}");
        builder.AppendLine($"keypad={config.Keypad}");
        builder.AppendLine($"public={config.Public}");
        builder.AppendLine($"mode={config.Mode}");

        File.WriteAllText(_path, builder.ToString(), Encoding.UTF8);
    }

    private string ReadValue(string key, string defaultValue)
    {
        if (!File.Exists(_path))
        {
            return defaultValue;
        }

        var inConfigSection = false;
        foreach (var rawLine in File.ReadLines(_path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inConfigSection = string.Equals(
                    line[1..^1].Trim(),
                    Section,
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inConfigSection)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var candidateKey = line[..separator].Trim();
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return defaultValue;
    }

    private static string ResolveConfigPath()
    {
        var basePath = System.IO.Path.Combine(AppContext.BaseDirectory, "配置.ini");
        if (File.Exists(basePath))
        {
            return basePath;
        }

        var currentPath = System.IO.Path.Combine(Environment.CurrentDirectory, "配置.ini");
        if (File.Exists(currentPath))
        {
            return currentPath;
        }

        return basePath;
    }
}
