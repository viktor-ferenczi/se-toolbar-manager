using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using ClientPlugin.Extensions;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Logic;

public static class ItemGroupStorage
{
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ItemGroup));
    private static readonly StringComparer PathComparer =
        Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    private static readonly HashSet<string> SavedPaths = new HashSet<string>(PathComparer);

    private static string Folder =>
        Path.Combine(MyFileSystem.UserDataPath, "ToolbarManager", "Groups");

    public static ItemGroup[] Load()
    {
        if (!Directory.Exists(Folder))
        {
            var defaults = new[] { ItemGroup.CreateDefault() };
            Save(defaults);
            return defaults;
        }

        var groups = new List<ItemGroup>();
        SavedPaths.Clear();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.xml"))
        {
            try
            {
                using (var reader = File.OpenText(path))
                {
                    var group = (ItemGroup)Serializer.Deserialize(reader);
                    group.Name = Path.GetFileNameWithoutExtension(path);
                    groups.Add(group);
                    SavedPaths.Add(path);
                }
            }
            catch (Exception e)
                when (e is IOException
                    || e is InvalidOperationException
                    || e is UnauthorizedAccessException
                )
            {
                MyLog.Default.Warning($"ToolbarManager: Failed to read group {path}: {e.Message}");
            }
        }

        return groups.OrderBy(group => group.Order).ToArray();
    }

    public static void Save(ItemGroup[] groups)
    {
        Directory.CreateDirectory(Folder);
        var paths = new HashSet<string>(PathComparer);
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            group.Order = i;
            var path = Path.Combine(Folder, group.Name.SanitizeFileName() + ".xml");
            var oldPath = SavedPaths.FirstOrDefault(saved => PathComparer.Equals(saved, path));
            if (oldPath != null && oldPath != path)
                File.Move(oldPath, path);
            using (var writer = File.CreateText(path))
                Serializer.Serialize(writer, group);
            paths.Add(path);
        }

        // Remove renamed/deleted groups, leaving unreadable files available for recovery.
        foreach (var path in SavedPaths.Except(paths, PathComparer))
            File.Delete(path);

        SavedPaths.Clear();
        SavedPaths.UnionWith(paths);
    }
}
