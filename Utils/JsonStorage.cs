using System;
using System.IO;
using System.Text.Json;

namespace ArchiveThumbViewer.Utils
{
    public static class JsonStorage
    {
        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true
        };

        public static T? Load<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, _opts);
            }
            catch { return null; }
        }

        public static void Save<T>(string path, T obj)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(obj, _opts);
                File.WriteAllText(path, json);
            }
            catch { /* 설정 저장 실패는 치명적이지 않으므로 조용히 무시 */ }
        }
    }
}
