// Utils/TagStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArchiveThumbViewer.Utils
{
    public class TagStore
    {
        public class DataModel
        {
            public Dictionary<string, List<string>> Items { get; set; } = new(); // fileKey -> tags
            public HashSet<string> AllTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> TagColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> TagOrder { get; set; } = new(); // ▲ 전역 태그 순서 (이름 리스트)
        }

        private readonly string _path;
        private readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true
        };
        private DataModel _data = new();

        public TagStore(string path) { _path = path; Load(); }

        public static string ComputeKey(string filePath)
        {
            using var sha1 = SHA1.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(filePath.ToLowerInvariant());
            return string.Concat(sha1.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_path)) { _data = new(); return; }
                var json = File.ReadAllText(_path);
                _data = JsonSerializer.Deserialize<DataModel>(json, _opts) ?? new();
                _data.TagColors ??= new(StringComparer.OrdinalIgnoreCase);
                _data.TagOrder ??= new();
            }
            catch { _data = new(); }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(_data, _opts));
            }
            catch { }
        }

        public IReadOnlyCollection<string> GetTags(string filePath)
        {
            var key = ComputeKey(filePath);
            return _data.Items.TryGetValue(key, out var list) ? list : Array.Empty<string>();
        }

        public void SetTags(string filePath, IEnumerable<string> tags)
        {
            var arr = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var key = ComputeKey(filePath);
            if (arr.Count == 0) _data.Items.Remove(key);
            else _data.Items[key] = arr;
            foreach (var t in arr) _data.AllTags.Add(t);
        }

        public HashSet<string> GetAllTags() => new(_data.AllTags, StringComparer.OrdinalIgnoreCase);

        // ▲ 순서를 함께 교체 (권장)
        public void ReplaceAllTagsAndOrder(IList<string> orderedNames)
        {
            orderedNames ??= Array.Empty<string>();
            _data.AllTags = new HashSet<string>(orderedNames, StringComparer.OrdinalIgnoreCase);
            _data.TagOrder = new List<string>(orderedNames);
            // 색상 맵 정리: 남은 태그만 유지
            var pruned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _data.TagColors)
                if (_data.AllTags.Contains(kv.Key)) pruned[kv.Key] = kv.Value;
            _data.TagColors = pruned;
        }

        public string? GetTagColor(string tagName)
            => _data.TagColors.TryGetValue(tagName, out var hex) ? hex : null;

        public void SetTagColor(string tagName, string colorHex)
        {
            if (string.IsNullOrWhiteSpace(tagName)) return;
            _data.TagColors[tagName] = colorHex;
            _data.AllTags.Add(tagName);
            if (!_data.TagOrder.Contains(tagName, StringComparer.OrdinalIgnoreCase))
                _data.TagOrder.Add(tagName);
        }

        public Dictionary<string, string> GetAllTagColors()
            => new(_data.TagColors, StringComparer.OrdinalIgnoreCase);

        public void ReplaceAllTagColors(Dictionary<string, string> colorsByTag)
            => _data.TagColors = new(colorsByTag ?? new(), StringComparer.OrdinalIgnoreCase);

        // ▲ 순서 get/set
        public IReadOnlyList<string> GetTagOrder() => _data.TagOrder.AsReadOnly();
        public void SetTagOrder(IList<string> orderedNames)
        {
            orderedNames ??= Array.Empty<string>();
            _data.TagOrder = new List<string>(orderedNames);
            foreach (var n in orderedNames) _data.AllTags.Add(n);
        }
    }
}
