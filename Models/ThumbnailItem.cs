using System;
using System.Collections.Generic;
using System.ComponentModel;           // ★ 추가
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;            // ★ 추가
using System.Collections.ObjectModel;

namespace ArchiveThumbViewer.Models
{
    public class ThumbnailItem : INotifyPropertyChanged   // ★ 인터페이스 추가
    {
        public string FilePath { get; init; } = "";
        public bool IsFolder { get; init; }

        public string RawName => Path.GetFileNameWithoutExtension(FilePath);

        private static readonly Regex _namePattern =
            new(@"^\s*(?<author>.+?)\s*-\s*(?<title>.+?)\s*$", RegexOptions.Compiled);

        public string Author
        {
            get
            {
                var m = _namePattern.Match(RawName);
                if (m.Success) return m.Groups["author"].Value.Trim();
                return "";
            }
        }

        public string Title
        {
            get
            {
                var m = _namePattern.Match(RawName);
                if (m.Success) return m.Groups["title"].Value.Trim();
                return RawName;
            }
        }

        public ObservableCollection<string> Tags { get; } = new();

        public DateTime DateAdded { get; set; }
        public DateTime? ReleaseDate { get; set; }

        // ★ 썸네일 속성에 변경 알림 추가
        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (!ReferenceEquals(_thumbnail, value))
                {
                    _thumbnail = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
                }
            }
        }

        private int? _entryCount; // zip/폴더 내부 파일 개수 (알 수 없으면 null)
        public int? EntryCount
        {
            get => _entryCount;
            set
            {
                if (_entryCount!= value)
                {
                    _entryCount= value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryCount)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;


    }
}
