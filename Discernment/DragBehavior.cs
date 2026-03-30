using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Discernment
{
    /// <summary>
    /// Attached behavior that enables mouse drag functionality for any UIElement.
    /// This handles MouseLeftButtonDown, MouseMove, and MouseLeftButtonUp events
    /// to implement drag-to-move behavior.
    /// </summary>
    public static class DragBehavior
    {
        private static Point _startPoint;
        private static double _startX;
        private static double _startY;
        private static bool _isDragging;
        private static bool _hasMoved;
        private static InsightNodeViewModel? _draggedNode;
        private static Canvas? _canvas;
        private const double DragThreshold = 5.0; // Minimum distance to start dragging

        public static readonly DependencyProperty EnableDragProperty =
            DependencyProperty.RegisterAttached(
                "EnableDrag",
                typeof(bool),
                typeof(DragBehavior),
                new PropertyMetadata(false, OnEnableDragChanged));

        public static bool GetEnableDrag(DependencyObject obj) => (bool)obj.GetValue(EnableDragProperty);
        public static void SetEnableDrag(DependencyObject obj, bool value) => obj.SetValue(EnableDragProperty, value);

        private static void OnEnableDragChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.MouseLeftButtonDown += Element_MouseLeftButtonDown;
                    element.MouseMove += Element_MouseMove;
                    element.MouseLeftButtonUp += Element_MouseLeftButtonUp;
                }
                else
                {
                    element.MouseLeftButtonDown -= Element_MouseLeftButtonDown;
                    element.MouseMove -= Element_MouseMove;
                    element.MouseLeftButtonUp -= Element_MouseLeftButtonUp;
                }
            }
        }

        private static void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var node = FindNodeViewModel(element);
                if (node != null)
                {
                    // Find the Canvas ancestor
                    _canvas = FindAncestor<Canvas>(element);
                    if (_canvas != null)
                    {
                        _isDragging = false;
                        _hasMoved = false;
                        _draggedNode = node;
                        // Get position relative to the Canvas
                        _startPoint = e.GetPosition(_canvas);
                        _startX = node.X;
                        _startY = node.Y;
                        element.CaptureMouse();
                        // Don't set e.Handled here - allow click to go through if no drag occurs
                    }
                }
            }
        }

        private static void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedNode != null && _canvas != null && sender is FrameworkElement element)
            {
                // Get current position relative to the Canvas
                var currentPoint = e.GetPosition(_canvas);
                var deltaX = currentPoint.X - _startPoint.X;
                var deltaY = currentPoint.Y - _startPoint.Y;
                var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                
                // Start dragging if mouse moved beyond threshold
                if (!_isDragging && distance > DragThreshold)
                {
                    _isDragging = true;
                    _hasMoved = true;
                }
                
                if (_isDragging)
                {
                    // Update node position (allow negative values as canvas can be scrolled)
                    _draggedNode.X = _startX + deltaX;
                    _draggedNode.Y = _startY + deltaY;
                }
            }
        }

        private static void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedNode != null && sender is FrameworkElement element)
            {
                // Only handle the event if we actually dragged
                if (_hasMoved)
                {
                    e.Handled = true;
                }
                
                _isDragging = false;
                _hasMoved = false;
                _draggedNode = null;
                _canvas = null;
                element.ReleaseMouseCapture();
            }
        }

        private static InsightNodeViewModel? FindNodeViewModel(FrameworkElement element)
        {
            var current = element as DependencyObject;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.DataContext is InsightNodeViewModel node)
                {
                    return node;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}

