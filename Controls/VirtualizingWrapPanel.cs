// Controls/VirtualizingWrapPanel.cs
namespace ArchiveThumbViewer.Controls
{
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Media;

    /// <summary>
    /// 가상화 WrapPanel (세로 스크롤 고정)
    /// - 아이템 사이 간격 + 좌/우 끝까지 균등 분배(Justify)
    /// - IScrollInfo 구현으로 부드러운 스크롤/가상화
    /// - 안정적인 Extent/Measure 계약으로 키보드 이동 중 점프 방지
    /// </summary>
    public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        // ====== Dependency Properties ======
        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>
        /// 최소 가로/세로 간격. 가로는 남는 폭을 [좌끝 + 사이들 + 우끝]에 균등 분배하며, 이 값보다 작아지지 않음.
        /// </summary>
        public static readonly DependencyProperty ItemGapProperty =
            DependencyProperty.Register(nameof(ItemGap), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
        public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
        public double ItemGap { get => (double)GetValue(ItemGapProperty); set => SetValue(ItemGapProperty, value); }

        // ====== IScrollInfo ======
        public bool CanVerticallyScroll { get; set; } = true;
        public bool CanHorizontallyScroll { get; set; } = false;

        public double ExtentWidth => _extent.Width;
        public double ExtentHeight => _extent.Height;
        public double ViewportWidth => _viewport.Width;
        public double ViewportHeight => _viewport.Height;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;

        public ScrollViewer? ScrollOwner { get; set; }

        public void LineUp() => ScrollByRows(-1);
        public void LineDown() => ScrollByRows(+1);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void MouseWheelUp() => LineUp();
        public void MouseWheelDown() => LineDown();

        public void LineLeft() { }
        public void LineRight() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }

        public void SetHorizontalOffset(double offset) { /* 가로 스크롤 미사용 */ }

        public void SetVerticalOffset(double offset)
        {
            var max = Math.Max(0, ExtentHeight - ViewportHeight);
            var newOffset = Clamp(offset, 0, max);
            if (!AreClose(newOffset, _offset.Y))
            {
                _offset.Y = newOffset;
                InvalidateMeasure();
                ScrollOwner?.InvalidateScrollInfo();
            }
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (visual == null || !IsAncestorOf(visual)) return Rect.Empty;

            // 인덱스 추출 (Tag → 컨테이너 폴백)
            int? indexOpt = null;
            if (visual is FrameworkElement fe)
            {
                if (fe.Tag is int tIndex && tIndex >= 0)
                {
                    indexOpt = tIndex;
                }
                else
                {
                    DependencyObject? c = fe;
                    while (c != null && c is not ListBoxItem)
                        c = VisualTreeHelper.GetParent(c);
                    if (c != null && _owner != null)
                    {
                        int idx = _owner.ItemContainerGenerator.IndexFromContainer(c);
                        if (idx >= 0) indexOpt = idx;
                    }
                }
            }

            if (!indexOpt.HasValue) return Rect.Empty;

            // 대상 아이템의 이론적 위치
            var (cols, stepW, stepH, gapX) = ComputeGrid(ViewportWidth > 0 ? ViewportWidth : ActualWidth);
            cols = Math.Max(1, cols);

            int index = indexOpt.Value;
            int row = index / cols;
            int col = index % cols;

            double x = gapX + col * stepW;
            double y = row * stepH;

            // 필요한 만큼만 스크롤
            if (y < VerticalOffset)
                SetVerticalOffset(y);
            else if (y + ItemHeight > VerticalOffset + ViewportHeight)
                SetVerticalOffset(y + ItemHeight - ViewportHeight);

            // 뷰포트 좌표계 기준 사각형 반환
            return new Rect(
                x - HorizontalOffset,
                y - VerticalOffset,
                ItemWidth,
                ItemHeight
            );
        }

        // ====== 내부 상태 ======
        private Size _extent;    // 전체 스크롤 영역
        private Size _viewport;  // 뷰포트 크기
        private Point _offset;   // 스크롤 오프셋

        private ItemsControl? _owner;
        private IItemContainerGenerator? _generator;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            EnsureOwner();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureOwner();

            // 유한한 뷰포트 산출 (ScrollViewer가 ∞를 줄 수 있음)
            double viewportW = CoerceFinite(availableSize.Width, ActualWidth, 1);
            double viewportH = CoerceFinite(availableSize.Height, ActualHeight, 1);

            int itemCount = _owner?.HasItems == true ? _owner.Items.Count : 0;

            var (cols, stepW, stepH, gapX) = ComputeGrid(viewportW);
            cols = Math.Max(1, cols);
            int rows = (int)Math.Ceiling(itemCount / (double)cols);

            // Extent/Viewport 갱신
            double extentW = viewportW; // 가로 스크롤 사용 안 함
            double minGap = Math.Max(0.0, ItemGap);
            // 안정 공식: 총 높이 = rows * (ih + minGap) - minGap (rows==0이면 0)
            double extentH = rows > 0 ? Math.Max(rows * stepH - minGap, 0) : 0;

            _extent = new Size(extentW, extentH);
            _viewport = new Size(viewportW, viewportH);

            // 점프 방지: 새 최대치로 즉시 클램프
            var maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
            if (_offset.Y > maxOffset) _offset.Y = maxOffset;

            ScrollOwner ??= FindScrollViewer();
            ScrollOwner?.InvalidateScrollInfo();

            // 가시 범위 인덱스 계산
            int firstRow = (int)Math.Floor(VerticalOffset / Math.Max(1.0, stepH));
            int visibleRows = (int)Math.Ceiling(_viewport.Height / Math.Max(1.0, stepH)) + 1;

            int startIndex = Math.Max(0, firstRow * cols);
            int endIndex = Math.Min(itemCount - 1, ((firstRow + visibleRows) * cols) - 1);

            // 안정 버전: 전부 비우고, 필요한 것만 0부터 다시 생성
            if (InternalChildren.Count > 0)
                RemoveInternalChildRange(0, InternalChildren.Count);

            var gen = _generator;
            if (itemCount > 0 && startIndex <= endIndex && gen != null)
            {
                var startPos = gen.GeneratorPositionFromIndex(startIndex);
                int childIndex = 0;

                using (gen.StartAt(startPos, GeneratorDirection.Forward, true))
                {
                    for (int itemIndex = startIndex; itemIndex <= endIndex; itemIndex++, childIndex++)
                    {
                        bool newlyRealized;
                        var child = gen.GenerateNext(out newlyRealized) as UIElement;
                        if (child == null) continue;

                        if (newlyRealized)
                        {
                            InsertInternalChild(childIndex, child);
                            gen.PrepareItemContainer(child);
                        }
                        else
                        {
                            if (childIndex >= InternalChildren.Count || !ReferenceEquals(InternalChildren[childIndex], child))
                            {
                                int existing = InternalChildren.IndexOf(child);
                                if (existing >= 0) RemoveInternalChildRange(existing, 1);
                                InsertInternalChild(childIndex, child);
                            }
                        }

                        if (child is FrameworkElement fe) fe.Tag = itemIndex;
                        child.Measure(new Size(ItemWidth, ItemHeight)); // 반드시 유한
                    }
                }
            }

            // ScrollViewer 계약: 패널은 "화면에 보여줄 크기"를 요청해야 함
            return new Size(
                CoerceFinite(_viewport.Width, 0, 1),
                CoerceFinite(_viewport.Height, 0, 1)
            );
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            EnsureOwner();

            int itemCount = _owner?.HasItems == true ? _owner.Items.Count : 0;
            if (itemCount == 0 || InternalChildren.Count == 0)
            {
                return new Size(
                    CoerceFinite(finalSize.Width, 0, 1),
                    CoerceFinite(finalSize.Height, 0, 1));
            }

            var (cols, stepW, stepH, gapX) = ComputeGrid(finalSize.Width);
            cols = Math.Max(1, cols);

            foreach (UIElement child in InternalChildren)
            {
                if (child is FrameworkElement fe && fe.Tag is int index && index >= 0 && index < itemCount)
                {
                    int row = index / cols;
                    int col = index % cols;

                    double x = gapX + col * stepW;        // 좌/우 끝 포함 균등 분배 → 좌측 여백 = gapX
                    double y = row * stepH - VerticalOffset;

                    child.Arrange(new Rect(new Point(x, y), new Size(ItemWidth, ItemHeight)));
                }
                else
                {
                    child.Arrange(new Rect(0, 0, 0, 0));
                }
            }

            return new Size(
                CoerceFinite(finalSize.Width, 0, 1),
                CoerceFinite(finalSize.Height, 0, 1));
        }

        // VirtualizingPanel 올바른 시그니처
        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            InvalidateMeasure();
        }

        protected override void OnClearChildren()
        {
            base.OnClearChildren();
            InvalidateMeasure();
        }

        // ====== 헬퍼 ======

        /// <summary>
        /// 최소 간격(ItemGap)을 보장하면서, 남는 폭을
        /// [왼쪽 끝 + 아이템 사이들 + 오른쪽 끝] (간격 개수 = cols+1)에 균등 분배.
        /// </summary>
        private (int cols, double stepW, double stepH, double gapX) ComputeGrid(double viewportW)
        {
            double minGap = Math.Max(0.0, ItemGap);
            double iw = Math.Max(1.0, ItemWidth);
            double ih = Math.Max(1.0, ItemHeight);

            // (cols * iw) + (cols + 1) * minGap <= viewportW 를 만족하는 최대 cols
            // => cols <= floor((viewportW - minGap)/(iw + minGap))
            int cols = Math.Max(1, (int)Math.Floor((viewportW - minGap) / (iw + minGap)));

            // 매우 좁은 뷰포트 보호
            if (double.IsNaN(viewportW) || viewportW <= 0)
                viewportW = iw + 2 * minGap;

            // 정확한 균등 간격 계산 (양끝 포함)
            double rawGap = (viewportW - cols * iw) / (cols + 1);
            double gapX = Math.Max(0.0, Math.Max(minGap, rawGap));

            double stepW = iw + gapX;           // 칸 폭 = 아이템 + 균등 간격
            double stepH = ih + minGap;         // 세로는 줄 간 최소 간격만 유지

            return (cols, stepW, stepH, gapX);
        }

        private void EnsureOwner()
        {
            _owner ??= ItemsControl.GetItemsOwner(this);
            _generator ??= ItemContainerGenerator;
            ScrollOwner ??= FindScrollViewer();
        }

        private ScrollViewer? FindScrollViewer()
        {
            DependencyObject? p = this;
            while (p != null && p is not ScrollViewer)
                p = VisualTreeHelper.GetParent(p);
            return p as ScrollViewer;
        }

        private void ScrollByRows(int dir)
        {
            var (_, _, stepH, _) = ComputeGrid(ViewportWidth);
            SetVerticalOffset(VerticalOffset + dir * stepH);
        }

        private static bool AreClose(double a, double b) => Math.Abs(a - b) < 0.1;

        /// <summary>NaN/∞ 방지 + 최소 fallback</summary>
        private static double CoerceFinite(double value, double fallbackFromActual, double min)
        {
            double v = (double.IsNaN(value) || double.IsInfinity(value)) ? fallbackFromActual : value;
            if (double.IsNaN(v) || v <= 0) v = min;
            return v;
        }

        private static double Clamp(double v, double min, double max) =>
            v < min ? min : (v > max ? max : v);

        

    }


}
