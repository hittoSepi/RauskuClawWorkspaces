using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace RauskuClaw.GUI.Views
{
    public class GridLengthAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public IEasingFunction? EasingFunction { get; set; }

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore()
        {
            return new GridLengthAnimation();
        }

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            if (animationClock.CurrentProgress == null)
            {
                return From;
            }

            var progress = animationClock.CurrentProgress.Value;

            if (EasingFunction != null)
            {
                progress = EasingFunction.Ease(progress);
            }

            var fromValue = From.Value;
            var toValue = To.Value;
            var current = fromValue + ((toValue - fromValue) * progress);

            return new GridLength(current, GridUnitType.Pixel);
        }
    }
}

