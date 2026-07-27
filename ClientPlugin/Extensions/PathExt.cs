namespace ClientPlugin.Extensions;

public static class PathExt
{
    public static string SanitizeFileName(this string name)
    {
        return name
            .Trim()
            .Replace(':', '.')
            .Replace('?', '.')
            .Replace('*', '-')
            .Replace('/', '-')
            .Replace('\\', '_');
    }
}
