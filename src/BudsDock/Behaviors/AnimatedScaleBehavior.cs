using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BudsDock.Behaviors;

public static class AnimatedScaleBehavior
{
    private static readonly DependencyProperty ScaleTransformProperty = DependencyProperty.RegisterAttached(
        "ScaleTransform",
        typeof(ScaleTransform),
        typeof(AnimatedScaleBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty TargetScaleProperty = DependencyProperty.RegisterAttached(
        "TargetScale",
        typeof(double),
        typeof(AnimatedScaleBehavior),
        new PropertyMetadata(1d, OnTargetScaleChanged));

    public static readonly DependencyProperty AnimationsEnabledProperty = DependencyProperty.RegisterAttached(
        "AnimationsEnabled",
        typeof(bool),
        typeof(AnimatedScaleBehavior),
        new PropertyMetadata(true, OnAnimationsEnabledChanged));

    public static void SetTargetScale(DependencyObject element, double value) => element.SetValue(TargetScaleProperty, value);
    public static double GetTargetScale(DependencyObject element) => (double)element.GetValue(TargetScaleProperty);
    public static void SetAnimationsEnabled(DependencyObject element, bool value) => element.SetValue(AnimationsEnabledProperty, value);
    public static bool GetAnimationsEnabled(DependencyObject element) => (bool)element.GetValue(AnimationsEnabledProperty);

    private static void OnTargetScaleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element && e.NewValue is double target)
        {
            ApplyScale(element, target);
        }
    }

    private static void OnAnimationsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element)
        {
            ApplyScale(element, GetTargetScale(element));
        }
    }

    private static void ApplyScale(FrameworkElement element, double target)
    {
        var transform = element.GetValue(ScaleTransformProperty) as ScaleTransform;
        if (transform is null)
        {
            transform = new ScaleTransform(1, 1);
            var existing = element.LayoutTransform;
            if (existing is null || existing.Value.IsIdentity)
            {
                element.LayoutTransform = transform;
            }
            else
            {
                var group = new TransformGroup();
                group.Children.Add(existing.CloneCurrentValue());
                group.Children.Add(transform);
                element.LayoutTransform = group;
            }
            element.SetValue(ScaleTransformProperty, transform);
        }

        if (!GetAnimationsEnabled(element) || !SystemParameters.ClientAreaAnimation)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleX = target;
            transform.ScaleY = target;
            return;
        }

        var current = transform.ScaleX;
        var isGrowing = target > current;
        var duration = TimeSpan.FromMilliseconds(isGrowing ? 155 : 210);
        var animation = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = duration,
            EasingFunction = isGrowing
                ? new QuinticEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleX = target;
            transform.ScaleY = target;
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone(), HandoffBehavior.SnapshotAndReplace);
    }
}
