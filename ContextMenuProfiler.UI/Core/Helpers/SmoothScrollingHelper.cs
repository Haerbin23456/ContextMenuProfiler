using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ContextMenuProfiler.UI.Core.Helpers
{
    public static class SmoothScrollingHelper
    {
        public static readonly DependencyProperty IsSmoothScrollingEnabledProperty =
            DependencyProperty.RegisterAttached("IsSmoothScrollingEnabled", typeof(bool), typeof(SmoothScrollingHelper), new PropertyMetadata(false, OnIsSmoothScrollingEnabledChanged));

        public static bool GetIsSmoothScrollingEnabled(DependencyObject obj) => (bool)obj.GetValue(IsSmoothScrollingEnabledProperty);
        public static void SetIsSmoothScrollingEnabled(DependencyObject obj, bool value) => obj.SetValue(IsSmoothScrollingEnabledProperty, value);

        private static void OnIsSmoothScrollingEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                    element.PreviewMouseWheel += Element_PreviewMouseWheel;
                else
                    element.PreviewMouseWheel -= Element_PreviewMouseWheel;
            }
        }

        private static void Element_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            var uiElement = sender as UIElement;
            var scrollViewer = FindParentScrollViewer(uiElement);
            if (scrollViewer == null) return;

            e.Handled = true; // 拦截所有事件，统一由我们的引擎分发

            // 无论鼠标还是触摸板，统一推送到平滑引擎
            // 引擎内部会根据 Delta 大小自动适配平滑度
            GetSmoother(scrollViewer).DoScroll(e.Delta);
        }

        private static ScrollViewer? FindParentScrollViewer(DependencyObject? child)
        {
            if (child == null) return null;
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is ScrollViewer))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as ScrollViewer;
        }

        private static ScrollSmoother GetSmoother(ScrollViewer sv)
        {
            var smoother = sv.GetValue(SmootherProperty) as ScrollSmoother;
            if (smoother == null)
            {
                smoother = new ScrollSmoother(sv);
                sv.SetValue(SmootherProperty, smoother);
            }
            return smoother;
        }

        private static readonly DependencyProperty SmootherProperty =
            DependencyProperty.RegisterAttached("Smoother", typeof(ScrollSmoother), typeof(SmoothScrollingHelper));

        private class ScrollSmoother
        {
            private readonly ScrollViewer _sv;
            private double _targetOffset;
            private bool _isRunning;

            public ScrollSmoother(ScrollViewer sv) { _sv = sv; }

            public void DoScroll(int delta)
            {
                // 只有在引擎没运行（即第一下滚动）时，才同步起始点
                // 或者当位置发生极其巨大的跳变（用户拽了滑块）时才同步
                if (!_isRunning || Math.Abs(_sv.VerticalOffset - _targetOffset) > 1000)
                {
                    _targetOffset = _sv.VerticalOffset;
                }

                _targetOffset -= delta;
                _targetOffset = Math.Max(0, Math.Min(_targetOffset, _sv.ScrollableHeight));

                if (!_isRunning)
                {
                    _isRunning = true;
                    CompositionTarget.Rendering += OnRendering;
                }
            }

            private void OnRendering(object? sender, EventArgs e)
            {
                double current = _sv.VerticalOffset;
                double diff = _targetOffset - current;
                
                // 使用自适应 Lerp 系数
                // 如果距离很近（触摸板微调），系数大一点，提高跟手度
                // 如果距离很远（滚轮大跳），系数小一点，提高平滑度
                double lerpFactor = Math.Abs(diff) < 10 ? 0.4 : 0.2;

                if (Math.Abs(diff) < 0.2)
                {
                    _sv.ScrollToVerticalOffset(_targetOffset);
                    CompositionTarget.Rendering -= OnRendering;
                    _isRunning = false;
                }
                else
                {
                    _sv.ScrollToVerticalOffset(current + diff * lerpFactor);
                }
            }
        }
    }
}
