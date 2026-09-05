using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BudsDock.Behaviors;

public static class AnimatedScaleBehavior
{
    private static readonly DependencyProperty AnimatedTransformProperty = DependencyProperty.RegisterAttached(
        "AnimatedTransform",
        typeof(ScaleTransform),
        typeof(AnimatedScaleBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.RegisterAttached(
        "AnimatedOffset",
        typeof(TranslateTransform),
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

    public static readonly DependencyProperty TargetOffsetXProperty = DependencyProperty.RegisterAttached(
        "TargetOffsetX",
        typeof(double),
        typeof(AnimatedScaleBehavior),
        new PropertyMetadata(0d, OnTargetOffsetChanged));

    public static readonly DependencyProperty TargetOffsetYProperty = DependencyProperty.RegisterAttached(
        "TargetOffsetY",
        typeof(double),
        typeof(AnimatedScaleBehavior),
        new PropertyMetadata(0d, OnTargetOffsetChanged));

    public static void SetTargetScale(DependencyObject element, double value) => element.SetValue(TargetScaleProperty, value);
    public static double GetTargetScale(DependencyObject element) => (double)element.GetValue(TargetScaleProperty);
    public static void SetAnimationsEnabled(DependencyObject element, bool value) => element.SetValue(AnimationsEnabledProperty, value);
    public static bool GetAnimationsEnabled(DependencyObject element) => (bool)element.GetValue(AnimationsEnabledProperty);
    public static void SetTargetOffsetX(DependencyObject element, double value) => element.SetValue(TargetOffsetXProperty, value);
    public static double GetTargetOffsetX(DependencyObject element) => (double)element.GetValue(TargetOffsetXProperty);
    public static void SetTargetOffsetY(DependencyObject element, double value) => element.SetValue(TargetOffsetYProperty, value);
    public static double GetTargetOffsetY(DependencyObject element) => (double)element.GetValue(TargetOffsetYProperty);

    private static void OnTargetScaleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element && e.NewValue is double target)
        {
            ApplyTransform(element, target, GetTargetOffsetX(element), GetTargetOffsetY(element));
        }
    }

    private static void OnTargetOffsetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element)
        {
            ApplyTransform(element, GetTargetScale(element), GetTargetOffsetX(element), GetTargetOffsetY(element));
        }
    }

    private static void OnAnimationsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element)
        {
            ApplyTransform(element, GetTargetScale(element), GetTargetOffsetX(element), GetTargetOffsetY(element));
        }
    }

    private static void ApplyTransform(FrameworkElement element, double targetScale, double targetX, double targetY)
    {
        var scale = element.GetValue(AnimatedTransformProperty) as ScaleTransform;
        var offset = element.GetValue(AnimatedOffsetProperty) as TranslateTransform;
        if (scale is null || offset is null)
        {
            scale = new ScaleTransform(1, 1);
            offset = new TranslateTransform();
            var existing = element.RenderTransform;
            var group = new TransformGroup();
            if (existing is not null && !existing.Value.IsIdentity)
            {
                group.Children.Add(existing.CloneCurrentValue());
            }
            group.Children.Add(scale);
            group.Children.Add(offset);
            element.RenderTransform = group;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.SetValue(AnimatedTransformProperty, scale);
            element.SetValue(AnimatedOffsetProperty, offset);
        }

        if (!GetAnimationsEnabled(element) || !SystemParameters.ClientAreaAnimation)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            offset.BeginAnimation(TranslateTransform.YProperty, null);
            scale.ScaleX = targetScale;
            scale.ScaleY = targetScale;
            offset.X = targetX;
            offset.Y = targetY;
            return;
        }

        var current = scale.ScaleX;
        var isGrowing = targetScale > current;
        var duration = TimeSpan.FromMilliseconds(isGrowing ? 170 : 230);
        var animation = new DoubleAnimation
        {
            From = current,
            To = targetScale,
            Duration = duration,
            EasingFunction = isGrowing
                ? new QuarticEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        scale.ScaleX = targetScale;
        scale.ScaleY = targetScale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone(), HandoffBehavior.SnapshotAndReplace);
        AnimateOffset(offset, TranslateTransform.XProperty, offset.X, targetX, duration);
        AnimateOffset(offset, TranslateTransform.YProperty, offset.Y, targetY, duration);
    }

    private static void AnimateOffset(
        TranslateTransform transform,
        DependencyProperty property,
        double current,
        double target,
        TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        transform.SetValue(property, target);
        transform.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
