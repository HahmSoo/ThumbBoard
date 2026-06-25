using ArchiveThumbViewer.Models;
using ArchiveThumbViewer.Utils;
using ArchiveThumbViewer.Views;
using SharpCompress.Archives;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using WF = System.Windows.Forms;
using System.Collections.Specialized;
using System.Collections.Generic;
using MessageBox = System.Windows.MessageBox;


namespace ArchiveThumbViewer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {

        private const int MaxThumbConcurrency = 3;
        private const int MaxCountConcurrency = 4;

        private readonly Dispatcher _ui;

        private static readonly string AppDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArchiveThumbViewer");
        private static readonly string SettingsPath = Path.Combine(AppDataDir, "appsettings.json");

        private static readonly string[] ArchiveExt = new[] { ".zip", ".cbz", ".rar", ".7z" };

        private AppSettings _settings = new();
        private TagStore _tagStore;
        private readonly string _tagStorePath = Path.Combine(AppDataDir, "tags.json");

        public ICommand CmdOpenRootFolder { get; }

        private string _currentPath = "";
        public string RootPath
        {
            get => _currentPath;
            private set { if (_currentPath != value) { _currentPath = value; OnPropertyChanged(); } }
        }

        public IReadOnlyList<string> TagOrder => AllTags.Select(t => t.Name).ToList();

        public enum SortKind { FileName, Title, Author }

        private SortKind _sortMode = SortKind.FileName;
        public SortKind SortMode
        {
            get => _sortMode;
            set { if (_sortMode != value) { _sortMode = value; OnPropertyChanged(); ApplySort(); } }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }
            }
        }

        private double _scale = 1.0;
        public double Scale
        {
            get => _scale;
            set { if (Math.Abs(_scale - value) > 0.001) { _scale = Math.Clamp(value, 0.6, 2.0); OnPropertyChanged(); } }
        }

        private string _statusCenter = "";
        public string StatusCenter
        {
            get => _statusCenter;
            set { if (_statusCenter != value) { _statusCenter = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<ThumbnailItem> Items { get; } = new();
        public ObservableCollection<TagItem> AllTags { get; } = new();

        private readonly ListCollectionView _itemsView;
        public ICollectionView ItemsView => _itemsView;

        public ICommand CmdBrowsePath { get; }
        public ICommand CmdGoUp { get; }
        public ICommand CmdOpenTagSettings { get; }
        public ICommand CmdOpenViewerSettings { get; }
        public ICommand CmdClearSearch { get; }
        public ICommand CmdSortTitle { get; }
        public ICommand CmdSortAuthor { get; }
        public ICommand CmdSortFileName { get; }
        public ICommand CmdClearTags { get; }
        public ICommand CmdItemDoubleClick { get; }
        public ICommand CmdItemSingleClick { get; }

        public ICommand CmdEditTags { get; }
        public ICommand CmdClearItemTags { get; }
        public ICommand CmdOpenFileLocation { get; }
        public ICommand CmdClearCache { get; }

        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _thumbCts;
        private readonly DispatcherTimer _searchDebounceTimer;

        private bool _wiredTagEvents = false;

        private readonly ConcurrentDictionary<string, byte> _thumbFail = new();

        public event Action? RequestScrollTop;

        public MainViewModel()
        {
            _ui = Dispatcher.CurrentDispatcher;

            _itemsView = (ListCollectionView)CollectionViewSource.GetDefaultView(Items);
            _itemsView.Filter = FilterBySearchAndTags;

            _tagStore = new TagStore(_tagStorePath);

            var initialAll = _tagStore.GetAllTags();
            var colorMap = _tagStore.GetAllTagColors();
            var order = _tagStore.GetTagOrder();

            IEnumerable<string> sourceNames = order?.Count > 0 ? order : initialAll.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            if (!sourceNames.Any())
                sourceNames = new[] { "액션", "로맨스", "판타지", "단편", "장편" };

            foreach (var name in sourceNames)
            {
                colorMap.TryGetValue(name, out var hex);
                AllTags.Add(new TagItem(name, hex));
            }

            WireTagEvents();

            CmdBrowsePath = new Utils.RelayCommand(_ => BrowsePath());
            CmdGoUp = new Utils.RelayCommand(_ => GoUpDirectory(), _ => CanGoUp());
            CmdOpenTagSettings = new Utils.RelayCommand(_ => OpenTagSettings());
            CmdOpenViewerSettings = new Utils.RelayCommand(_ => OpenViewerSettings());

            CmdOpenRootFolder = new Utils.RelayCommand(
                _ => OpenRootFolder(),
                _ => !string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath)
            );

            CmdClearSearch = new RelayCommand(_ =>
            {
                SearchText = "";
                using (_itemsView.DeferRefresh())
                {
                    _itemsView.Refresh();
                }
                RequestScrollTop?.Invoke();
                UpdateStatusToVisibleCount();
            });

            CmdSortTitle = new Utils.RelayCommand(_ => SortMode = SortKind.Title);
            CmdSortAuthor = new Utils.RelayCommand(_ => SortMode = SortKind.Author);
            CmdSortFileName = new Utils.RelayCommand(_ => SortMode = SortKind.FileName);

            CmdClearTags = new RelayCommand(_ =>
            {
                using (_itemsView.DeferRefresh())
                {
                    foreach (var tg in AllTags) tg.IsSelected = false;
                }
                UpdateStatusToVisibleCount();
            });

            CmdItemDoubleClick = new Utils.RelayCommand(p => OnItemDoubleClick(p as ThumbnailItem));
            CmdItemSingleClick = new Utils.RelayCommand(p => OnItemSingleClick(p as ThumbnailItem));

            CmdEditTags = new Utils.RelayCommand(p => EditTags(p as ThumbnailItem), p => p is ThumbnailItem);
            CmdClearItemTags = new Utils.RelayCommand(p => ClearItemTags(p as ThumbnailItem), p => p is ThumbnailItem);
            CmdOpenFileLocation = new Utils.RelayCommand(p => OpenFileLocation(p as ThumbnailItem), p => p is ThumbnailItem);
            CmdClearCache = new Utils.RelayCommand(async _ => await ClearThumbnailCacheAsync());

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += (_, __) =>
            {
                _searchDebounceTimer.Stop();
                _itemsView.Refresh();
                UpdateStatusToVisibleCount();
            };

            LoadSettingsAndRestore();
            ApplySort();
        }

        private void RunOnUI(Action action)
        {
            var d = _ui;
            if (d == null || d.HasShutdownStarted || d.HasShutdownFinished) return;

            if (d.CheckAccess()) action();
            else
            {
                try { d.BeginInvoke(action, DispatcherPriority.Background); }
                catch { /* 종료 경합 시 무시 */ }
            }
        }

        public void Dispose()
        {
            try { _scanCts?.Cancel(); } catch { }
            try { _thumbCts?.Cancel(); } catch { }
            _scanCts?.Dispose();
            _thumbCts?.Dispose();
        }

        private void LoadSettingsAndRestore()
        {
            _settings = JsonStorage.Load<AppSettings>(SettingsPath) ?? new AppSettings();

            if (_settings.ThumbnailScale > 0)
                Scale = _settings.ThumbnailScale;

            if (!string.IsNullOrWhiteSpace(_settings.RootPath) && Directory.Exists(_settings.RootPath))
            {
                _ = LoadDirectoryAsync(_settings.RootPath);
            }
            else
            {
                StatusCenter = "표시된 목록 수: 0";
                (CmdOpenRootFolder as Utils.RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.RootPath = RootPath;
                JsonStorage.Save(SettingsPath, _settings);
            }
            catch { }
        }

        private bool FilterBySearchAndTags(object obj)
        {
            if (obj is not ThumbnailItem it) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var s = SearchText.Trim();
                if (!(it.Title.Contains(s, StringComparison.OrdinalIgnoreCase)
                   || it.Author.Contains(s, StringComparison.OrdinalIgnoreCase)
                   || it.RawName.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            var selected = AllTags.Where(t => t.IsSelected).Select(t => t.Name).ToList();
            if (selected.Count > 0)
            {
                foreach (var tag in selected)
                    if (!it.Tags.Contains(tag)) return false;
            }
            return true;
        }

        private void ApplySort()
        {
            using (_itemsView.DeferRefresh())
            {
                _itemsView.SortDescriptions.Clear();

                if (_itemsView is ListCollectionView lcv)
                    lcv.CustomSort = null;

                switch (SortMode)
                {
                    case SortKind.Title:
                        _itemsView.SortDescriptions.Add(
                            new SortDescription(nameof(ThumbnailItem.Title), ListSortDirection.Ascending));
                        break;

                    case SortKind.Author:
                        _itemsView.SortDescriptions.Add(
                            new SortDescription(nameof(ThumbnailItem.Author), ListSortDirection.Ascending));
                        break;

                    case SortKind.FileName:
                    default:
                        _itemsView.SortDescriptions.Add(
                            new SortDescription(nameof(ThumbnailItem.RawName), ListSortDirection.Ascending));
                        break;
                }
            }
        }

        private void BrowsePath()
        {
            using var dlg = new WF.FolderBrowserDialog
            {
                Description = "목록의 루트 폴더를 선택하세요.",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() == WF.DialogResult.OK && Directory.Exists(dlg.SelectedPath))
            {
                _ = LoadDirectoryAsync(dlg.SelectedPath);
            }
        }

        private async Task LoadDirectoryAsync(string path)
        {
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;

            RootPath = path;
            SaveSettings();

            StatusCenter = "스캔 중…";
            _thumbFail.Clear();
            try
            {
                var list = await Task.Run(() =>
                {
                    var results = new System.Collections.Generic.List<ThumbnailItem>(capacity: 256);

                    try
                    {
                        foreach (var dir in Directory.EnumerateDirectories(path))
                        {
                            if (ct.IsCancellationRequested) break;
                            var info = new DirectoryInfo(dir);
                            if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                            results.Add(new ThumbnailItem
                            {
                                FilePath = dir,
                                IsFolder = true,
                                DateAdded = info.CreationTimeUtc
                            });
                        }
                    }
                    catch { }

                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(path))
                        {
                            if (ct.IsCancellationRequested) break;
                            var attr = File.GetAttributes(f);
                            if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;

                            var ext = Path.GetExtension(f).ToLowerInvariant();
                            if (!ArchiveExt.Contains(ext)) continue;

                            FileInfo fi = new(f);
                            results.Add(new ThumbnailItem
                            {
                                FilePath = f,
                                IsFolder = false,
                                DateAdded = fi.CreationTimeUtc
                            });
                        }
                    }
                    catch { }

                    return results;
                }, ct);

                if (ct.IsCancellationRequested) return;

                Items.Clear();
                foreach (var it in list) Items.Add(it);

                foreach (var it in Items.Where(x => !x.IsFolder))
                {
                    var tags = _tagStore.GetTags(it.FilePath);
                    foreach (var t in tags) it.Tags.Add(t);
                }

                ApplySort();
                _itemsView.Refresh();

                StatusCenter = $"표시된 목록 수: {Items.Count}";
                (CmdGoUp as Utils.RelayCommand)?.RaiseCanExecuteChanged();
                (CmdOpenRootFolder as Utils.RelayCommand)?.RaiseCanExecuteChanged();

                StartLoadThumbnails();
                StartPopulateEntryCounts();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusCenter = $"스캔 실패: {ex.Message}";
            }
        }

        private void StartPopulateEntryCounts()
        {
            var ct = _scanCts?.Token ?? CancellationToken.None;

            _ = Task.Run(async () =>
            {
                using var sem = new SemaphoreSlim(MaxCountConcurrency);
                var tasks = Items.Select(async it =>
                {
                    if (ct.IsCancellationRequested) return;

                    if (!(it.IsFolder || ArchiveExt.Contains(Path.GetExtension(it.FilePath).ToLowerInvariant())))
                        return;

                    await sem.WaitAsync(ct);
                    try
                    {
                        int count = TryCountEntriesAny(it.FilePath, it.IsFolder);
                        if (ct.IsCancellationRequested) return;
                        RunOnUI(() => it.EntryCount = count);
                    }
                    catch { }
                    finally { sem.Release(); }
                }).ToArray();

                try { await Task.WhenAll(tasks); } catch { }
            }, ct);
        }

        private bool CanGoUp() => !string.IsNullOrEmpty(RootPath) && Directory.GetParent(RootPath) != null;

        private void GoUpDirectory()
        {
            if (!CanGoUp()) return;
            var parent = Directory.GetParent(RootPath)!.FullName;
            _ = LoadDirectoryAsync(parent);
        }

        private void StartLoadThumbnails()
        {
            _thumbCts?.Cancel();
            _thumbCts = new CancellationTokenSource();
            var ct = _thumbCts.Token;

            _ = Task.Run(async () =>
            {
                using var sem = new SemaphoreSlim(MaxThumbConcurrency);
                var tasks = Items
                    .Where(it => !it.IsFolder && it.Thumbnail == null && !_thumbFail.ContainsKey(it.FilePath))
                    .Select(async it =>
                    {
                        if (ct.IsCancellationRequested) return;
                        await sem.WaitAsync(ct);
                        try
                        {
                            var bmp = await ThumbnailService.GetThumbnailAsync(it.FilePath, ct);
                            if (ct.IsCancellationRequested) return;

                            if (bmp != null)
                                RunOnUI(() => it.Thumbnail = bmp);
                            else
                                _thumbFail.TryAdd(it.FilePath, 1);
                        }
                        catch { _thumbFail.TryAdd(it.FilePath, 1); }
                        finally { sem.Release(); }
                    }).ToArray();

                try { await Task.WhenAll(tasks); } catch { }
            }, ct);
        }

        private void OnItemDoubleClick(ThumbnailItem? item)
        {
            if (item == null) return;

            if (item.IsFolder)
            {
                if (Directory.Exists(item.FilePath))
                    _ = LoadDirectoryAsync(item.FilePath);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_settings.ViewerPath) && File.Exists(_settings.ViewerPath))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = _settings.ViewerPath,
                        Arguments = $"\"{item.FilePath}\"",
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(_settings.ViewerPath) ?? Environment.CurrentDirectory
                    };
                    Process.Start(psi);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"뷰어 실행 실패: {ex.Message}");
                }
            }

            MessageBox.Show("뷰어가 연결되어 있지 않습니다. 메뉴의 [뷰어설정]에서 exe를 지정하세요.");
        }

        public void SetTagColorPersist(string tag, string colorHex)
        {
            _tagStore.SetTagColor(tag, colorHex);
            _tagStore.Save();
        }

        private void OnItemSingleClick(ThumbnailItem? item)
        {
            if (item == null)
            {
                UpdateStatusToVisibleCount();
                return;
            }

            if (item.IsFolder)
            {
                StatusCenter = $"폴더: {item.RawName}";
                return;
            }

            StatusCenter = $"{item.RawName} — 내부 파일 계산 중…";

            Task.Run(() =>
            {
                int cnt = TryCountEntries(item.FilePath);
                RunOnUI(() =>
                {
                    StatusCenter = $"{item.RawName} — 내부 파일 {cnt:N0}개";
                });
            });
        }

        private int TryCountEntries(string archivePath)
        {
            try
            {
                var ext = Path.GetExtension(archivePath).ToLowerInvariant();
                if (ext == ".zip" || ext == ".cbz")
                {
                    using var zip = ZipFile.OpenRead(archivePath);
                    return zip.Entries.Count;
                }

                using var archive = ArchiveFactory.Open(archivePath);
                return archive.Entries.Count(e => !e.IsDirectory);
            }
            catch { }
            return 0;
        }

        private void OpenTagSettings()
        {
            var ordered = AllTags.Select(t => t.Name).ToList();
            var colors = AllTags.ToDictionary(t => t.Name, t => t.ColorHex, StringComparer.OrdinalIgnoreCase);

            var dlg = new TagManageDialog(ordered, colors) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                var result = dlg.Result;

                UnwireTagEvents();
                AllTags.Clear();
                foreach (var r in result)
                    AllTags.Add(new TagItem(r.Name, r.ColorHex));
                WireTagEvents();

                var orderedNames = result.Select(x => x.Name).ToList();
                _tagStore.ReplaceAllTagsAndOrder(orderedNames);
                _tagStore.ReplaceAllTagColors(result.ToDictionary(x => x.Name, x => x.ColorHex, StringComparer.OrdinalIgnoreCase));
                _tagStore.Save();

                _itemsView.Refresh();
                UpdateStatusToVisibleCount();
            }
        }

        private void OpenViewerSettings()
        {
            using var ofd = new WF.OpenFileDialog
            {
                Title = "연결할 뷰어(exe)를 선택하세요",
                Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
                CheckFileExists = true
            };
            if (ofd.ShowDialog() == WF.DialogResult.OK)
            {
                _settings.ViewerPath = ofd.FileName;
                JsonStorage.Save(SettingsPath, _settings);
                MessageBox.Show($"뷰어 연결됨:\n{_settings.ViewerPath}");
            }
        }

        private void EditTags(ThumbnailItem? item)
        {
            if (item == null || item.IsFolder) return;

            var allOrdered = AllTags.Select(t => t.Name).ToList();
            var dlg = new TagEditDialog(allOrdered, item.Tags) { Owner = Application.Current.MainWindow };

            if (dlg.ShowDialog() == true)
            {
                using (_itemsView.DeferRefresh())
                {
                    item.Tags.Clear();
                    foreach (var t in dlg.ResultTags) item.Tags.Add(t);

                    foreach (var t in dlg.ResultTags)
                        if (!AllTags.Any(x => x.Name.Equals(t, StringComparison.OrdinalIgnoreCase)))
                            AllTags.Add(new TagItem(t));

                    _tagStore.SetTags(item.FilePath, item.Tags);
                    _tagStore.Save();
                }

                _itemsView.Refresh();
                UpdateStatusToVisibleCount();
            }
        }

        private void ClearItemTags(ThumbnailItem? item)
        {
            if (item == null || item.IsFolder) return;

            using (_itemsView.DeferRefresh())
            {
                item.Tags.Clear();
                _tagStore.SetTags(item.FilePath, item.Tags);
                _tagStore.Save();
            }

            _itemsView.Refresh();
            UpdateStatusToVisibleCount();
        }

        private void OpenFileLocation(ThumbnailItem? item)
        {
            if (item == null) return;
            try
            {
                var path = item.IsFolder ? item.FilePath : Path.GetDirectoryName(item.FilePath)!;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch { }
        }

        private void WireTagEvents()
        {
            if (_wiredTagEvents) return;
            _wiredTagEvents = true;

            foreach (var tg in AllTags)
                tg.PropertyChanged += Tag_PropertyChanged;

            AllTags.CollectionChanged += AllTags_CollectionChanged;
        }

        private void UnwireTagEvents()
        {
            if (!_wiredTagEvents) return;
            _wiredTagEvents = false;

            foreach (var tg in AllTags)
                tg.PropertyChanged -= Tag_PropertyChanged;

            AllTags.CollectionChanged -= AllTags_CollectionChanged;
        }

        private void AllTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (TagItem tg in e.NewItems)
                    tg.PropertyChanged += Tag_PropertyChanged;

            if (e.OldItems != null)
                foreach (TagItem tg in e.OldItems)
                    tg.PropertyChanged -= Tag_PropertyChanged;
        }

        private void Tag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TagItem.IsSelected))
            {
                _itemsView.Refresh();
                UpdateStatusToVisibleCount();
                RequestScrollTop?.Invoke();
            }
        }

        private void UpdateStatusToVisibleCount()
        {
            int visible = 0;
            if (_itemsView != null)
                visible = _itemsView.Cast<object>().Count();
            StatusCenter = $"표시된 목록 수: {visible}";
        }

        public void AdjustScaleOnWheel(bool ctrlPressed, int delta)
        {
            if (!ctrlPressed) return;
            Scale += (delta > 0 ? 0.05 : -0.05);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public void LoadWindowPlacement(Window w)
        {
            if (_settings.WindowWidth > 100) w.Width = _settings.WindowWidth;
            if (_settings.WindowHeight > 100) w.Height = _settings.WindowHeight;

            if (_settings.WindowTop.HasValue && _settings.WindowLeft.HasValue)
            {
                var rect = new System.Drawing.Rectangle(
                    (int)_settings.WindowLeft.Value,
                    (int)_settings.WindowTop.Value,
                    (int)_settings.WindowWidth,
                    (int)_settings.WindowHeight);

                if (IsOnAnyScreen(rect))
                {
                    w.Top = _settings.WindowTop.Value;
                    w.Left = _settings.WindowLeft.Value;
                }
                else
                {
                    w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            else
            {
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            w.Loaded += (_, __) =>
            {
                w.WindowState = _settings.WindowState switch
                {
                    "Maximized" => WindowState.Maximized,
                    "Minimized" => WindowState.Minimized,
                    _ => WindowState.Normal
                };
            };
        }

        public void SaveWindowPlacement(Window w)
        {
            // ① 중복·모순 제거
            var bounds = (w.WindowState == WindowState.Normal)
                ? new Rect(w.Left, w.Top, w.Width, w.Height)
                : w.RestoreBounds;

            _settings.WindowWidth = Math.Max(100, bounds.Width);
            _settings.WindowHeight = Math.Max(100, bounds.Height);
            _settings.WindowTop = bounds.Top;
            _settings.WindowLeft = bounds.Left;

            // 최소화 저장 방지: 최소화면 Normal로, 최대화면 Maximized로
            _settings.WindowState = w.WindowState == WindowState.Maximized ? "Maximized" : "Normal";

            _settings.ThumbnailScale = Scale;

            JsonStorage.Save(SettingsPath, _settings);
        }

        private static bool IsOnAnyScreen(System.Drawing.Rectangle rect)
        {
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
            {
                var wa = s.WorkingArea;
                if (wa.IntersectsWith(rect)) return true;
            }
            return false;
        }

        public void ApplyTagsToItem(ThumbnailItem item, IEnumerable<string> tags)
        {
            if (item == null || item.IsFolder) return;

            using (_itemsView.DeferRefresh())
            {
                var has = new HashSet<string>(item.Tags, StringComparer.OrdinalIgnoreCase);
                foreach (var t in tags)
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;

                    if (!AllTags.Any(x => string.Equals(x.Name, t, StringComparison.OrdinalIgnoreCase)))
                        AllTags.Add(new TagItem(t));

                    if (has.Add(t))
                        item.Tags.Add(t);
                }

                SortTagsByGlobalOrder(item);

                _tagStore.SetTags(item.FilePath, item.Tags);
                _tagStore.Save();
            }

            _itemsView.Refresh();
            UpdateStatusToVisibleCount();
        }

        public void ResortAllItemsByGlobalTagOrder()
        {
            foreach (var it in Items)
            {
                if (it.IsFolder) continue;
                SortTagsByGlobalOrder(it);
                _tagStore.SetTags(it.FilePath, it.Tags);
            }
            _tagStore.Save();
            _itemsView.Refresh();
        }

        private async Task ClearThumbnailCacheAsync()
        {
            var ask = MessageBox.Show(
                "썸네일 캐시를 모두 삭제할까요?\n(다시 생성되며 잠시 시간이 걸릴 수 있어요)",
                "캐시 초기화",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                StatusCenter = "캐시 삭제 중…";

                _thumbCts?.Cancel();

                var dir = ThumbnailService.CacheDir;
                await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                            const int maxRetry = 3;
                            for (int i = 0; i < maxRetry; i++)
                            {
                                try
                                {
                                    Directory.Delete(dir, true);
                                    break;
                                }
                                catch when (i < maxRetry - 1)
                                {
                                    Thread.Sleep(120);
                                }
                            }
                        }
                        Directory.CreateDirectory(dir);
                    }
                    catch { }
                });

                RunOnUI(() =>
                {
                    foreach (var it in Items.Where(x => !x.IsFolder))
                        it.Thumbnail = null;
                    _thumbFail.Clear();
                });

                StartLoadThumbnails();

                StatusCenter = "캐시가 초기화되었습니다.";
                MessageBox.Show("썸네일 캐시 초기화 완료!", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"캐시 초기화 중 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int TryCountEntriesAny(string path, bool isFolder)
        {
            try
            {
                if (isFolder)
                {
                    return Directory.EnumerateFiles(path).Count();
                }
                else
                {
                    return TryCountEntries(path);
                }
            }
            catch { return 0; }
        }

        private void SortTagsByGlobalOrder(ThumbnailItem item)
        {
            var order = AllTags
                .Select((t, idx) => new { t.Name, idx })
                .ToDictionary(x => x.Name, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var sorted = item.Tags
                .OrderBy(tag => order.TryGetValue(tag, out var idx) ? idx : int.MaxValue)
                .ThenBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = item.Tags.Count != sorted.Count || !item.Tags.SequenceEqual(sorted);
            if (changed)
            {
                item.Tags.Clear();
                foreach (var t in sorted) item.Tags.Add(t);
            }
        }

        private void OpenRootFolder()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = RootPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("열 수 있는 폴더가 없습니다. 먼저 경로를 설정하세요.", "알림",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더 열기 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
