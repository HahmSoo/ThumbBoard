// Views/TagManageDialog.xaml.cs
using ArchiveThumbViewer.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using WF = System.Windows.Forms;

namespace ArchiveThumbViewer.Views
{
    public partial class TagManageDialog : Window
    {
        public class TagRow
        {
            public string Name { get; set; } = "";
            public string ColorHex { get; set; } = "#FF3A74D8";
            public Brush ColorBrush
            {
                get
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(ColorHex)!;
                        var b = new SolidColorBrush(c); b.Freeze(); return b;
                    }
                    catch { return Brushes.SlateBlue; }
                }
            }
        }

        public IReadOnlyList<TagRow> Result { get; private set; } = Array.Empty<TagRow>();
        public ICommand CmdPickColor { get; }

        private readonly Dictionary<string, string> _initialColors;
        private readonly ObservableCollection<TagRow> _rows;   // ▲ ObservableCollection
        private Point _dragStart;
        private TagRow? _dragged;

        public TagManageDialog(IEnumerable<string> existingTags, Dictionary<string, string>? colorsByTag = null)
        {
            InitializeComponent();

            _initialColors = colorsByTag != null
                ? new Dictionary<string, string>(colorsByTag, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var seed = existingTags?
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => new TagRow
                {
                    Name = t,
                    ColorHex = _initialColors.TryGetValue(t, out var hex) ? hex : "#FF3A74D8"
                }) ?? Enumerable.Empty<TagRow>();

            _rows = new ObservableCollection<TagRow>(seed);
            TagList.ItemsSource = _rows;

            CmdPickColor = new RelayCommand(o =>
            {
                if (o is TagRow row)
                {
                    using var cd = new WF.ColorDialog { AllowFullOpen = true, FullOpen = true };
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(row.ColorHex)!;
                        cd.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                    }
                    catch { }

                    if (cd.ShowDialog() == WF.DialogResult.OK)
                    {
                        row.ColorHex = $"#{cd.Color.A:X2}{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
                        // 강제 리프레시
                        var i = _rows.IndexOf(row);
                        if (i >= 0) { _rows.RemoveAt(i); _rows.Insert(i, row); }
                    }
                }
            });

            this.DataContext = this;
        }

        private static string RandomColorHex()
        {
            var rnd = new Random();
            double h = rnd.NextDouble(), s = 0.55, v = 0.90;
            (byte r, byte g, byte b) = HsvToRgb(h, s, v);
            return $"#FF{r:X2}{g:X2}{b:X2}";
        }

        private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
        {
            double i = Math.Floor(h * 6), f = h * 6 - i;
            double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            return ((byte)(new[] { (v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q) }[(int)(i % 6)].Item1 * 255),
                    (byte)(new[] { (v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q) }[(int)(i % 6)].Item2 * 255),
                    (byte)(new[] { (v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q) }[(int)(i % 6)].Item3 * 255));
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            var t = NewTagBox.Text?.Trim();
            if (string.IsNullOrEmpty(t)) return;
            if (_rows.Any(r => string.Equals(r.Name, t, StringComparison.OrdinalIgnoreCase))) { NewTagBox.Clear(); return; }

            _rows.Add(new TagRow { Name = t, ColorHex = RandomColorHex() }); // ▲ 랜덤 색
            NewTagBox.Clear();
            NewTagBox.Focus();
        }

        private void OnSort(object sender, RoutedEventArgs e)
        {
            var sorted = _rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _rows.Clear();
            foreach (var r in sorted) _rows.Add(r);
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            var toRemove = TagList.SelectedItems.Cast<TagRow>().ToList();
            if (toRemove.Count == 0) return;
            foreach (var r in toRemove) _rows.Remove(r);
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Result = _rows.Select(r => new TagRow { Name = r.Name, ColorHex = r.ColorHex }).ToList();
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ===== 드래그 재정렬 =====
        private void OnListMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragged = GetRowUnderMouse(e.GetPosition(TagList));
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragged == null) return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            DragDrop.DoDragDrop(TagList, _dragged, DragDropEffects.Move);
        }

        private void OnListDragOver(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(typeof(TagRow)) is TagRow)) e.Effects = DragDropEffects.None;
            else e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnListDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(typeof(TagRow)) is TagRow row)) return;
            var target = GetRowUnderMouse(e.GetPosition(TagList));
            if (target == null || ReferenceEquals(target, row))
            {
                // 빈 공간으로 떨어뜨린 경우: 맨 끝으로
                if (!_rows.Contains(row)) return;
                _rows.Remove(row);
                _rows.Add(row);
                return;
            }

            int oldIdx = _rows.IndexOf(row);
            int newIdx = _rows.IndexOf(target);
            if (oldIdx < 0 || newIdx < 0) return;

            if (oldIdx != newIdx)
            {
                _rows.RemoveAt(oldIdx);
                if (oldIdx < newIdx) newIdx--; // 제거 후 인덱스 보정
                _rows.Insert(newIdx, row);
            }
        }

        private TagRow? GetRowUnderMouse(Point p)
        {
            var el = TagList.InputHitTest(p) as DependencyObject;
            while (el != null && el != TagList)
            {
                if (el is FrameworkElement fe && fe.DataContext is TagRow r) return r;
                el = VisualTreeHelper.GetParent(el);
            }
            return null;
        }
    }
}
