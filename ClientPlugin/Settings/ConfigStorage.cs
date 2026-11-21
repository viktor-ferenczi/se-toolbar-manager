using System;
using System.IO;
using System.Xml.Serialization;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Settings;

public static class ConfigStorage
{
    private static readonly string ConfigFileName = string.Concat(Plugin.Name, ".cfg");
    private static string ConfigFilePath =>
        Path.Combine(MyFileSystem.UserDataPath, "Storage", ConfigFileName);

    public static void Save(Config config)
    {
        var path = ConfigFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var text = File.CreateText(path))
            new XmlSerializer(typeof(Config)).Serialize(text, config);
    }

    public static Config Load()
    {
        var path = ConfigFilePath;
        if (!File.Exists(path))
        {
            return Config.Default;
        }

        var xmlSerializer = new XmlSerializer(typeof(Config));
        try
        {
            using (var streamReader = File.OpenText(path))
            {
                var config = (Config)xmlSerializer.Deserialize(streamReader);
                return config == null ? Config.Default : Migrate(config);
            }
        }
        catch (Exception)
        {
            MyLog.Default.Warning(
                $"{ConfigFileName}: Failed to read config file: {ConfigFilePath}"
            );
        }

        return Config.Default;
    }

    private static Config Migrate(Config config)
    {
        if (config.ConfigVersion < 1)
        {
            // 1.9.0 shipped "Vanilla block groups start over" on and saved it for everyone
            // who opened the settings, chosen or not. It changes the game's own behavior,
            // so turn it off once. Players who want it turn it on again.
            config.ResetVanillaGroupSlots = false;
        }

        config.ConfigVersion = Config.LatestVersion;
        return config;
    }
}
