using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using ArchiveThumbViewer.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ArchiveThumbViewer.Utils
{
    /// <summary>
    /// (태그이름, 색상HEX) 조합 → SolidColorBrush 캐시.
    /// AllTags 컬렉션에서 현재 색상을 찾아 칩 배경 브러시를 만들어준다.
    /// </summary>
    public class TagToBrushConverter : IMultiValueConverter
    {
        // key: $"{tag}|{hex}"  (hex는 없을 수 있으므로 빈 문자열 허용)
        private static readonly ConcurrentDictionary<string, SolidColorBrush> _cache = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is not [string tagName, var allTagsObj] || string.IsNullOrWhiteSpace(tagName))
                return Brushes.Transparent;

            var tags = allTagsObj as System.Collections.IEnumerable;
            string? hex = null;

            if (tags != null)
            {
                foreach (var t in tags)
                {
                    if (t is TagItem ti && ti.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                    {
                        hex = ti.ColorHex;
                        break;
                    }
                }
            }

            // fallback 색(약한 회색)
            if (string.IsNullOrWhiteSpace(hex))
                hex = "#FF666666";

            var key = $"{tagName}|{hex}".ToLowerInvariant();

            if (_cache.TryGetValue(key, out var cached))
                return cached;

            // 생성 & Freeze하여 UI스레드 제약/GC 부담 ↓
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();

            _cache[key] = brush;
            return brush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
