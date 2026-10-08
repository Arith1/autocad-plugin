using System;
using System.IO;
using System.Xml.Serialization;

namespace SteelGrid.Plugin.UI
{
    /// <summary>排条参数保存到本地用户目录。</summary>
    public static class GridSettingsStore
    {
        private static string DirectoryPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SteelGrid");

        private static string FilePath => Path.Combine(DirectoryPath, "settings.xml");

        public static GridSettingsData Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new GridSettingsData();
                }

                var serializer = new XmlSerializer(typeof(GridSettingsData));
                using (var stream = File.OpenRead(FilePath))
                {
                    var settings = serializer.Deserialize(stream) as GridSettingsData;
                    return settings ?? new GridSettingsData();
                }
            }
            catch
            {
                return new GridSettingsData();
            }
        }

        public static void Save(GridSettingsData settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Directory.CreateDirectory(DirectoryPath);
            var serializer = new XmlSerializer(typeof(GridSettingsData));
            using (var stream = File.Create(FilePath))
            {
                serializer.Serialize(stream, settings);
            }
        }
    }
}
