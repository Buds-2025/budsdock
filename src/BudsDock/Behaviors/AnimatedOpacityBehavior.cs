using System.Windows;
using System.Windows.Media.Animation;

namespace BudsDock.Behaviors;

public static class AnimatedOpacityBehavior
{
    public static readonly DependencyProperty TargetOpacityProperty = DependencyProperty.RegisterAttached(
        "TargetOpacity",
        typeof(double),
        typeof(AnimatedOpacityBehavior),
        new PropertyMetadata(0d, OnTargetOpacityChanged));

    public static readonly DependencyProperty AnimationsEnabledProperty = DependencyProperty.RegisterAttached(
        "AnimationsEnabled",
        typeof(bool),
        typeof(AnimatedOpacityBehavior),
        new PropertyMetadata(true, OnAnimationsEnabledChanged));

    public static void SetTargetOpacity(DependencyObject element, double value) => element.SetValue(TargetOpacityProperty, value);
    public static double GetTargetOpacity(DependencyObject element) => (double)element.GetValue(TargetOpacityProperty);
    public static void SetAnimationsEnabled(DependencyObject element, bool value) => element.SetValue(AnimationsEnabledProperty, value);
    public static bool GetAnimationsEnabled(DependencyObject element) => (bool)element.GetValue(AnimationsEnabledProperty);

    private static void OnTargetOpacityChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is UIElement element && e.NewValue is double target)
        {
            ApplyOpacity(element, target);
        }
    }

    private static void OnAnimationsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is UIElement element)
        {
            ApplyOpacity(element, GetTargetOpacity(element));
        }
    }

    private static void ApplyOpacity(UIElement element, double target)
    {
        target = Math.Clamp(target, 0d, 1d);
        if (!GetAnimationsEnabled(element) || !SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = target;
            return;
        }

        var isAppearing = target > element.Opacity;
        var animation = new DoubleAnimation
        {
            From = element.Opacity,
            To = target,
            Duration = TimeSpan.FromMilliseconds(isAppearing ? 145 : 220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = target;
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
