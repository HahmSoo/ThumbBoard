using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace ArchiveThumbViewer.Views
{
    public partial class TagEditDialog : Window
    {
        public class TagOption
        {
            public string Name { get; set; } = "";
            public bool IsChecked { get; set; }
        }

        // XAML: ItemsControl ItemsSource="{Binding TagOptions}"
        public ObservableCollection<TagOption> TagOptions { get; } = new();

        // 선택 결과
        public IReadOnlyList<string> ResultTags =>
            TagOptions.Where(x => x.IsChecked).Select(x => x.Name).ToList();

        /// <summary>
        /// allTagsInOrder: 순서가 이미 정해진 전체 태그 목록(그대로 표시)
        /// selected: 현재 아이템에 체크되어 있어야 하는 태그들
        /// </summary>
        public TagEditDialog(IEnumerable<string> allTagsInOrder, IEnumerable<string> selected)
        {
            InitializeComponent();
            DataContext = this;

            var selectedSet = new HashSet<string>(selected ?? Enumerable.Empty<string>(),
                                                  StringComparer.OrdinalIgnoreCase);

            // ★ 전달받은 순서를 그대로 보존 (정렬 X)
            foreach (var name in allTagsInOrder ?? Enumerable.Empty<string>())
            {
                TagOptions.Add(new TagOption
                {
                    Name = name,
                    IsChecked = selectedSet.Contains(name)
                });
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in TagOptions) t.IsChecked = false;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 우클릭 팝업(컨텍스트메뉴) 뜨는 것 방지
            e.Handled = true;

            // 지금까지 체크한 내용 그대로 적용하고 닫기 (확인과 동일)
            this.DialogResult = true;
            this.Close();
        }


    }
}
