// Converters/TagOrderConverter.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace ArchiveThumbViewer.Converters
{
    // MultiBinding: [0] IEnumerable<string> (item.Tags), [1] IEnumerable<string> (global order)
    public class TagOrderConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var itemTags = values.Length > 0 ? values[0] as IEnumerable<string> : null;
            var order = values.Length > 1 ? values[1] as IEnumerable<string> : null;

            if (itemTags == null) return Array.Empty<string>();
            var set = new HashSet<string>(itemTags, StringComparer.OrdinalIgnoreCase);

            if (order != null)
            {
                var ordered = order.Where(t => set.Contains(t)).ToList();
                // order에 없는 잔여 태그는 뒤에 사전순으로 붙인다
                var rest = set.Except(order, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                ordered.AddRange(rest);
                return ordered;
            }
            return itemTags.ToList();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
