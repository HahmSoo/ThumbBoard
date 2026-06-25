// Models/TagItem.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter; // Color, Brush

namespace ArchiveThumbViewer.Models
{
    public class TagItem : INotifyPropertyChanged
    {
        public string Name { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        // ▼ 색상(ARGB Hex, #AARRGGBB). 기본: 약한 블루톤
        private string _colorHex = "#FF3A74D8";
        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (_colorHex != value)
                {
                    _colorHex = value;
                    _colorBrush = null; // 브러시 재생성
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }
        }

        private Brush? _colorBrush;
        public Brush ColorBrush
        {
            get
            {
                if (_colorBrush == null)
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(_colorHex)!;
                        _colorBrush = new SolidColorBrush(c);
                        (_colorBrush as SolidColorBrush)!.Freeze();
                    }
                    catch
                    {
                        _colorBrush = Brushes.SlateBlue; // fallback
                    }
                }
                return _colorBrush!;
            }
        }

        public TagItem(string name, string? colorHex = null)
        {
            Name = name;
            if (!string.IsNullOrWhiteSpace(colorHex))
                ColorHex = colorHex!;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
