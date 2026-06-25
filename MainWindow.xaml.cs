using ArchiveThumbViewer.Models;
using ArchiveThumbViewer.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using WF = System.Windows.Forms;

namespace ArchiveThumbViewer
{
    public partial class MainWindow : Window
    {
        private const string DragFormatTag = "ATV/Tag";
        private bool _mouseDownOnItem = false;
        private ThumbnailItem? _pressedItem;

        public MainWindow()
        {
            InitializeComponent();

            var vm = new MainViewModel();
            this.DataContext = vm;

            HookRequestScrollTop(vm);

            this.DataContextChanged += (_, e) =>
            {
                if (e.NewValue is MainViewModel newVm)
                    HookRequestScrollTop(newVm);
            };

            vm.LoadWindowPlacement(this);
            this.Closing += (_, __) => vm.SaveWindowPlacement(this);
        }

        private void HookRequestScrollTop(MainViewModel vm)
        {
            vm.RequestScrollTop -= OnRequestScrollTop;
            vm.RequestScrollTop += OnRequestScrollTop;
        }

        private void OnRequestScrollTop()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var sv = FindDescendant<ScrollViewer>(ThumbList);
                sv?.ScrollToTop();
            }, DispatcherPriority.Background);
        }

        private MainViewModel VM => (MainViewModel)DataContext;

        // Ctrl + 휠 → Scale 조정
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            VM.AdjustScaleOnWheel(ctrl, e.Delta);
            if (ctrl) e.Handled = true;
        }

        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownOnItem = true;
            _pressedItem = GetThumbnailItemFromSender(sender);
        }

        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = GetThumbnailItemFromSender(sender);
            if (_mouseDownOnItem)
            {
                if (e.ClickCount == 2)
                    VM.CmdItemDoubleClick.Execute(item);
                else
                    VM.CmdItemSingleClick.Execute(item);
            }
            _mouseDownOnItem = false;
            _pressedItem = null;
        }

        private void ListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem lbi && DataContext is MainViewModel vm && lbi.DataContext is ThumbnailItem item)
            {
                if (vm.CmdItemDoubleClick?.CanExecute(item) == true)
                    vm.CmdItemDoubleClick.Execute(item);
            }
        }

        // 태그 토글: 드래그 시작점 기록 (④)
        private void Tag_Toggle_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
        }

        private ThumbnailItem? GetThumbnailItemFromSender(object sender)
        {
            if (sender is not DependencyObject d) return null;
            var cur = d;
            while (cur != null)
            {
                if (cur is FrameworkElement fe && fe.DataContext is ThumbnailItem ti)
                    return ti;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private Point _dragStart;
        private void Tag_Toggle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is not FrameworkElement fe) return;

            var tagName = (fe.DataContext as ArchiveThumbViewer.Models.TagItem)?.Name;
            if (string.IsNullOrWhiteSpace(tagName)) return;

            var pos = e.GetPosition(null);
            var diff = _dragStart - pos;
            if (e.LeftButton == MouseButtonState.Pressed &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                var data = new DataObject();
                data.SetData(DragFormatTag, tagName);
                data.SetData(DataFormats.Text, tagName);

                DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);

            if (e.ChangedButton == MouseButton.XButton1 && DataContext is MainViewModel vm)
            {
                if (vm.CmdGoUp?.CanExecute(null) == true)
                {
                    vm.CmdGoUp.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void ListBoxItem_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = CanAcceptTagDrop(sender, e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ListBoxItem_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = CanAcceptTagDrop(sender, e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ListBoxItem_DragLeave(object sender, DragEventArgs e)
        {
        }

        private void ListBoxItem_Drop(object sender, DragEventArgs e)
        {
            if (!CanAcceptTagDrop(sender, e)) return;

            string? tag = null;
            if (e.Data.GetDataPresent(DragFormatTag))
                tag = e.Data.GetData(DragFormatTag) as string;
            else if (e.Data.GetDataPresent(DataFormats.Text))
                tag = e.Data.GetData(DataFormats.Text) as string;

            if (string.IsNullOrWhiteSpace(tag)) return;

            var item = GetThumbnailItemFromSender(sender);
            if (item == null || item.IsFolder) return;

            if (DataContext is MainViewModel vm && vm.CmdEditTags != null)
            {
                vm.ApplyTagsToItem(item, new[] { tag! });
            }
        }

        private bool CanAcceptTagDrop(object sender, DragEventArgs e)
        {
            var item = GetThumbnailItemFromSender(sender);
            if (item == null) return false;

            bool hasTag = e.Data.GetDataPresent(DragFormatTag) || e.Data.GetDataPresent(DataFormats.Text);
            if (!hasTag) return false;

            if (item.IsFolder) return false;
            return true;
        }

        private void OnPickTagColor(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is ToggleButton tb)
            {
                if (tb.DataContext is ArchiveThumbViewer.Models.TagItem tagItem && DataContext is ArchiveThumbViewer.ViewModels.MainViewModel vm)
                {
                    using var cd = new WF.ColorDialog
                    {
                        AllowFullOpen = true,
                        FullOpen = true
                    };

                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(tagItem.ColorHex)!;
                        cd.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                    }
                    catch { }

                    if (cd.ShowDialog() == WF.DialogResult.OK)
                    {
                        var hex = $"#{cd.Color.A:X2}{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
                        tagItem.ColorHex = hex;
                        vm.SetTagColorPersist(tagItem.Name, hex);
                    }
                }
            }
        }

        private void ThumbList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var panel = FindDescendant<Controls.VirtualizingWrapPanel>(ThumbList);
            if (panel == null || ThumbList.Items.Count == 0) return;

            int cur = ThumbList.SelectedIndex;
            if (cur < 0) cur = 0;

            double viewportW = panel.ViewportWidth > 0 ? panel.ViewportWidth : panel.ActualWidth;
            double viewportH = panel.ViewportHeight > 0 ? panel.ViewportHeight : panel.ActualHeight; // ③ 가드
            double iw = Math.Max(1.0, panel.ItemWidth);
            double ih = Math.Max(1.0, panel.ItemHeight);
            double gap = Math.Max(0.0, panel.ItemGap);

            int cols = Math.Max(1, (int)Math.Floor((viewportW - gap) / (iw + gap)));
            double stepH = ih + gap;

            int next = cur;
            bool handled = false;

            switch (e.Key)
            {
                case Key.Left: next = cur - 1; handled = true; break;
                case Key.Right: next = cur + 1; handled = true; break;
                case Key.Up: next = cur - cols; handled = true; break;
                case Key.Down: next = cur + cols; handled = true; break;

                case Key.Home: next = 0; handled = true; break;
                case Key.End: next = ThumbList.Items.Count - 1; handled = true; break;

                case Key.PageUp:
                    {
                        int rowsPerPage = Math.Max(1, (int)Math.Floor(viewportH / stepH)); // ③
                        next = cur - rowsPerPage * cols;
                        handled = true;
                        break;
                    }
                case Key.PageDown:
                    {
                        int rowsPerPage = Math.Max(1, (int)Math.Floor(viewportH / stepH)); // ③
                        next = cur + rowsPerPage * cols;
                        handled = true;
                        break;
                    }
            }

            if (!handled) return;

            e.Handled = true;

            next = Math.Max(0, Math.Min(next, ThumbList.Items.Count - 1));
            ThumbList.SelectedIndex = next;

            int row = next / cols;
            double itemTop = row * stepH;
            double newOffset = panel.VerticalOffset;

            if (itemTop < panel.VerticalOffset)
                newOffset = itemTop;
            else if (itemTop + ih > panel.VerticalOffset + viewportH)
                newOffset = itemTop + ih - viewportH;

            panel.SetVerticalOffset(newOffset);
        }

        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private void ThumbList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            var lbi = FindAncestor<ListBoxItem>(dep);
            if (lbi == null) return;

            var item = lbi.DataContext as ArchiveThumbViewer.Models.ThumbnailItem;
            if (item == null) return;

            if (item.IsFolder) return;

            var vm = DataContext as ArchiveThumbViewer.ViewModels.MainViewModel;
            if (vm == null) return;

            var all = new System.Collections.Generic.HashSet<string>(
                vm.AllTags.Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

            var dlg = new ArchiveThumbViewer.Views.TagEditDialog(all, item.Tags)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                Topmost = false
            };

            Point winPos = e.GetPosition(this);
            Point screenPos = this.PointToScreen(winPos);

            dlg.Loaded += (_, __) =>
            {
                const double offset = -100;
                var work = SystemParameters.WorkArea;

                double desiredLeft = screenPos.X + offset;
                double desiredTop = screenPos.Y + offset;

                double w = dlg.ActualWidth > 0 ? dlg.ActualWidth : (dlg.Width > 0 ? dlg.Width : 220);
                double h = dlg.ActualHeight > 0 ? dlg.ActualHeight : (dlg.Height > 0 ? dlg.Height : 360);

                if (desiredLeft + w > work.Right)
                    desiredLeft = Math.Max(work.Left, screenPos.X - w - offset);
                if (desiredTop + h > work.Bottom)
                    desiredTop = Math.Max(work.Top, screenPos.Y - h - offset);

                dlg.Left = desiredLeft;
                dlg.Top = desiredTop;
            };

            if (dlg.ShowDialog() == true)
            {
                item.Tags.Clear();
                foreach (var t in dlg.ResultTags)
                    item.Tags.Add(t);

                vm.ApplyTagsToItem(item, item.Tags);
            }

            e.Handled = true;
        }
    }
}
