namespace DepthVideo.Core.Services;

public static class ExecutableLocator
{
    public static string? Find(string executableName)
    {
        var fileName = executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName
            : $"{executableName}.exe";

        var appLocal = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", fileName);
        if (File.Exists(appLocal))
        {
            return appLocal;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}
