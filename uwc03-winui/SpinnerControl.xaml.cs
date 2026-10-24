using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace CapturaStudio;

public sealed partial class SpinnerControl : UserControl
{
    private readonly Storyboard _storyboard = new();

    public SpinnerControl()
    {
        InitializeComponent();

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(anim, ArcRotate);
        Storyboard.SetTargetProperty(anim, "Angle");
        _storyboard.Children.Add(anim);

        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => _storyboard.Stop();
    }

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(SpinnerControl),
        new PropertyMetadata(false, (d, _) => ((SpinnerControl)d).UpdateAnimation()));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private void UpdateAnimation()
    {
        if (IsActive)
        {
            _storyboard.Begin();
        }
        else
        {
            _storyboard.Stop();
            ArcRotate.Angle = 0;
        }
    }
}
