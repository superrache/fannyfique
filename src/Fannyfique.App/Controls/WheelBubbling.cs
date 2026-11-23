using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fannyfique.App.Controls;

/// <summary>
/// Un ScrollViewer horizontal intercepte la molette même s'il ne défile pas verticalement : on la
/// renvoie au parent pour que la liste des doublons défile normalement.
/// </summary>
public static class WheelBubbling
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WheelBubbling), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
            if ((bool)e.NewValue)
            {
                element.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject d)
        {
            return;
        }

        // Maj + molette : défilement horizontal de la ligne.
        if (Keyboard.Modifiers == ModifierKeys.Shift && sender is ScrollViewer sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        if (VisualTreeHelper.GetParent(d) is UIElement parent)
        {
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender,
            });
        }
    }
}
