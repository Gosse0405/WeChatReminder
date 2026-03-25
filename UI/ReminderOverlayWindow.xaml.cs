using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WeChatReminder.Models;

namespace WeChatReminder.UI;

public partial class ReminderOverlayWindow : Window
{
    private static readonly IEasingFunction CardEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction ContentEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

    private ScaleTransform? _cardScaleTransform;
    private TranslateTransform? _cardTranslateTransform;
    private DropShadowEffect? _cardShadowEffect;
    private UIElement? _headerRegion;
    private TranslateTransform? _headerTranslateTransform;
    private UIElement? _actionsRegion;
    private TranslateTransform? _actionsTranslateTransform;
    private TranslateTransform? _messageTranslateTransform;
    private bool _allowImmediateClose;
    private bool _isCloseAnimationRunning;

    public event EventHandler<ReminderAction>? ActionSelected;

    public ReminderOverlayWindow(string title, string message)
    {
        InitializeComponent();
        Opacity = 0;

        TitleText.Text = title;
        MessageText.Text = message;

        Loaded += ReminderOverlayWindow_Loaded;
        Closing += ReminderOverlayWindow_Closing;
        SizeChanged += ReminderOverlayWindow_SizeChanged;
        PreviewKeyDown += ReminderOverlayWindow_PreviewKeyDown;
    }

    private void ReminderOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;

        InitializeAnimationTargets();
        PrepareOpenState();
        UpdateCardClip();
        Dispatcher.BeginInvoke(BeginOpenAnimation, DispatcherPriority.Render);

        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            Focus();
            PrimaryButton.Focus();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void ReminderOverlayWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCardClip();
    }

    private void UpdateCardClip()
    {
        if (CardBorder.ActualWidth <= 0 || CardBorder.ActualHeight <= 0)
            return;

        CardBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, CardBorder.ActualWidth, CardBorder.ActualHeight),
            28,
            28);
    }

    private void ReminderOverlayWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ReminderOverlayWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowImmediateClose || _isCloseAnimationRunning)
            return;

        if (!IsLoaded || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        e.Cancel = true;
        BeginCloseAnimation();
    }

    private void OpenNow_Click(object sender, RoutedEventArgs e)
    {
        ActionSelected?.Invoke(this, ReminderAction.OpenNow);
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        ActionSelected?.Invoke(this, ReminderAction.Snooze10Minutes);
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        ActionSelected?.Invoke(this, ReminderAction.Snooze1Hour);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void InitializeAnimationTargets()
    {
        if (_cardScaleTransform != null && _cardTranslateTransform != null && _messageTranslateTransform != null)
            return;

        (var cardScale, var cardTranslate) = EnsureTransforms(CardBorder, includeScale: true);
        _cardScaleTransform = cardScale;
        _cardTranslateTransform = cardTranslate;
        _cardShadowEffect = CardBorder.Effect as DropShadowEffect;
        CardBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

        _headerRegion = FindAncestor<Grid>(TitleText);
        if (_headerRegion != null)
        {
            (_, _headerTranslateTransform) = EnsureTransforms(_headerRegion, includeScale: false);
        }

        (_, _messageTranslateTransform) = EnsureTransforms(MessageText, includeScale: false);

        _actionsRegion = FindAncestor<Grid>(PrimaryButton);
        if (_actionsRegion != null)
        {
            (_, _actionsTranslateTransform) = EnsureTransforms(_actionsRegion, includeScale: false);
        }
    }

    private void PrepareOpenState()
    {
        Opacity = 0;

        if (_cardScaleTransform != null)
        {
            _cardScaleTransform.ScaleX = 0.22;
            _cardScaleTransform.ScaleY = 0.22;
        }

        if (_cardTranslateTransform != null)
        {
            _cardTranslateTransform.Y = 12;
        }

        if (_cardShadowEffect != null)
        {
            _cardShadowEffect.BlurRadius = 14;
            _cardShadowEffect.Opacity = 0.03;
        }

        if (_headerRegion != null)
        {
            _headerRegion.Opacity = 0;
        }

        if (_headerTranslateTransform != null)
        {
            _headerTranslateTransform.Y = 14;
        }

        MessageText.Opacity = 0;
        if (_messageTranslateTransform != null)
        {
            _messageTranslateTransform.Y = 16;
        }

        if (_actionsRegion != null)
        {
            _actionsRegion.Opacity = 0;
        }

        if (_actionsTranslateTransform != null)
        {
            _actionsTranslateTransform.Y = 18;
        }
    }

    private void BeginOpenAnimation()
    {
        if (_isCloseAnimationRunning)
            return;

        Opacity = 1;

        if (_cardScaleTransform != null)
        {
            _cardScaleTransform.ScaleX = 1;
            _cardScaleTransform.ScaleY = 1;
            StartKeyFrameAnimation(
                _cardScaleTransform,
                ScaleTransform.ScaleXProperty,
                (0, 0.22),
                (130, 0.7),
                (255, 1.028),
                (390, 0.992),
                (530, 1.0));

            StartKeyFrameAnimation(
                _cardScaleTransform,
                ScaleTransform.ScaleYProperty,
                (0, 0.22),
                (130, 0.62),
                (248, 1.042),
                (384, 0.988),
                (522, 1.0));
        }

        if (_cardTranslateTransform != null)
        {
            _cardTranslateTransform.Y = 0;
            StartKeyFrameAnimation(
                _cardTranslateTransform,
                TranslateTransform.YProperty,
                (0, 12),
                (145, 3.5),
                (270, -1.4),
                (405, 0.35),
                (530, 0));
        }

        if (_cardShadowEffect != null)
        {
            _cardShadowEffect.BlurRadius = 28;
            _cardShadowEffect.Opacity = 0.12;
            StartKeyFrameAnimation(
                _cardShadowEffect,
                DropShadowEffect.BlurRadiusProperty,
                (0, 14),
                (210, 32),
                (390, 27),
                (560, 28));

            StartKeyFrameAnimation(
                _cardShadowEffect,
                DropShadowEffect.OpacityProperty,
                (0, 0.03),
                (210, 0.145),
                (390, 0.11),
                (560, 0.12));
        }

        StartAnimation(this, OpacityProperty, 0, 1, 145, 0, ContentEase);

        if (_headerRegion != null)
        {
            _headerRegion.Opacity = 1;
            StartAnimation(_headerRegion, UIElement.OpacityProperty, 0, 1, 170, 155, ContentEase);
        }

        if (_headerTranslateTransform != null)
        {
            _headerTranslateTransform.Y = 0;
            StartAnimation(_headerTranslateTransform, TranslateTransform.YProperty, 14, 0, 210, 155, CardEase);
        }

        MessageText.Opacity = 1;
        StartAnimation(MessageText, UIElement.OpacityProperty, 0, 1, 175, 215, ContentEase);

        if (_messageTranslateTransform != null)
        {
            _messageTranslateTransform.Y = 0;
            StartAnimation(_messageTranslateTransform, TranslateTransform.YProperty, 16, 0, 205, 215, CardEase);
        }

        if (_actionsRegion != null)
        {
            _actionsRegion.Opacity = 1;
            StartAnimation(_actionsRegion, UIElement.OpacityProperty, 0, 1, 170, 270, ContentEase);
        }

        if (_actionsTranslateTransform != null)
        {
            _actionsTranslateTransform.Y = 0;
            StartAnimation(_actionsTranslateTransform, TranslateTransform.YProperty, 18, 0, 200, 270, CardEase);
        }
    }

    private void BeginCloseAnimation()
    {
        if (_isCloseAnimationRunning)
            return;

        _isCloseAnimationRunning = true;
        IsHitTestVisible = false;

        double currentOpacity = Opacity;
        double currentCardScaleX = _cardScaleTransform?.ScaleX ?? 1;
        double currentCardScaleY = _cardScaleTransform?.ScaleY ?? 1;
        double currentCardOffset = _cardTranslateTransform?.Y ?? 0;
        double currentShadowBlur = _cardShadowEffect?.BlurRadius ?? 28;
        double currentShadowOpacity = _cardShadowEffect?.Opacity ?? 0.12;
        double currentHeaderOpacity = _headerRegion?.Opacity ?? 1;
        double currentHeaderOffset = _headerTranslateTransform?.Y ?? 0;
        double currentMessageOpacity = MessageText.Opacity;
        double currentMessageOffset = _messageTranslateTransform?.Y ?? 0;
        double currentActionsOpacity = _actionsRegion?.Opacity ?? 1;
        double currentActionsOffset = _actionsTranslateTransform?.Y ?? 0;

        Opacity = 0;

        if (_cardScaleTransform != null)
        {
            _cardScaleTransform.ScaleX = 0.955;
            _cardScaleTransform.ScaleY = 0.955;
            StartAnimation(_cardScaleTransform, ScaleTransform.ScaleXProperty, currentCardScaleX, 0.955, 210, 0, ContentEase);
            StartAnimation(_cardScaleTransform, ScaleTransform.ScaleYProperty, currentCardScaleY, 0.955, 210, 0, ContentEase);
        }

        if (_cardTranslateTransform != null)
        {
            _cardTranslateTransform.Y = 6;
            StartAnimation(_cardTranslateTransform, TranslateTransform.YProperty, currentCardOffset, 6, 210, 0, ContentEase);
        }

        if (_cardShadowEffect != null)
        {
            _cardShadowEffect.BlurRadius = 12;
            _cardShadowEffect.Opacity = 0.02;
            StartAnimation(_cardShadowEffect, DropShadowEffect.BlurRadiusProperty, currentShadowBlur, 12, 200, 0, ContentEase);
            StartAnimation(_cardShadowEffect, DropShadowEffect.OpacityProperty, currentShadowOpacity, 0.02, 180, 0, ContentEase);
        }

        var closeAnimation = CreateAnimation(currentOpacity, 0, 210, 0, ContentEase);
        closeAnimation.Completed += (_, _) =>
        {
            _allowImmediateClose = true;
            Close();
        };
        BeginAnimation(OpacityProperty, closeAnimation, HandoffBehavior.SnapshotAndReplace);

        if (_headerRegion != null)
        {
            _headerRegion.Opacity = 0;
            StartAnimation(_headerRegion, UIElement.OpacityProperty, currentHeaderOpacity, 0, 125, 0, ContentEase);
        }

        if (_headerTranslateTransform != null)
        {
            _headerTranslateTransform.Y = 6;
            StartAnimation(_headerTranslateTransform, TranslateTransform.YProperty, currentHeaderOffset, 6, 125, 0, ContentEase);
        }

        MessageText.Opacity = 0;
        StartAnimation(MessageText, UIElement.OpacityProperty, currentMessageOpacity, 0, 135, 0, ContentEase);

        if (_messageTranslateTransform != null)
        {
            _messageTranslateTransform.Y = 6;
            StartAnimation(_messageTranslateTransform, TranslateTransform.YProperty, currentMessageOffset, 6, 135, 0, ContentEase);
        }

        if (_actionsRegion != null)
        {
            _actionsRegion.Opacity = 0;
            StartAnimation(_actionsRegion, UIElement.OpacityProperty, currentActionsOpacity, 0, 120, 0, ContentEase);
        }

        if (_actionsTranslateTransform != null)
        {
            _actionsTranslateTransform.Y = 6;
            StartAnimation(_actionsTranslateTransform, TranslateTransform.YProperty, currentActionsOffset, 6, 120, 0, ContentEase);
        }
    }

    private static void StartAnimation(
        IAnimatable target,
        DependencyProperty property,
        double from,
        double to,
        int durationMs,
        int beginTimeMs,
        IEasingFunction easing)
    {
        target.BeginAnimation(
            property,
            CreateAnimation(from, to, durationMs, beginTimeMs, easing),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateAnimation(
        double from,
        double to,
        int durationMs,
        int beginTimeMs,
        IEasingFunction easing)
    {
        return new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(beginTimeMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
    }

    private static void StartKeyFrameAnimation(
        IAnimatable target,
        DependencyProperty property,
        params (int TimeMs, double Value)[] keyFrames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop
        };

        for (int i = 0; i < keyFrames.Length; i++)
        {
            var (timeMs, value) = keyFrames[i];

            if (i == 0)
            {
                animation.KeyFrames.Add(
                    new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(timeMs))));
                continue;
            }

            animation.KeyFrames.Add(
                new EasingDoubleKeyFrame(
                    value,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(timeMs)),
                    CardEase));
        }

        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static (ScaleTransform? Scale, TranslateTransform Translate) EnsureTransforms(UIElement element, bool includeScale)
    {
        TransformGroup group;
        if (element.RenderTransform is TransformGroup existingGroup)
        {
            group = existingGroup;
        }
        else
        {
            group = new TransformGroup();

            bool shouldPreserveExistingTransform = element.RenderTransform switch
            {
                null => false,
                MatrixTransform matrixTransform => !matrixTransform.Matrix.IsIdentity,
                _ => true
            };

            if (shouldPreserveExistingTransform)
            {
                group.Children.Add(element.RenderTransform);
            }

            element.RenderTransform = group;
        }

        ScaleTransform? scaleTransform = null;
        TranslateTransform? translateTransform = null;

        foreach (var child in group.Children)
        {
            if (includeScale && scaleTransform == null && child is ScaleTransform scale)
            {
                scaleTransform = scale;
                continue;
            }

            if (translateTransform == null && child is TranslateTransform translate)
            {
                translateTransform = translate;
            }
        }

        if (includeScale && scaleTransform == null)
        {
            scaleTransform = new ScaleTransform(1, 1);
            group.Children.Insert(0, scaleTransform);
        }

        if (translateTransform == null)
        {
            translateTransform = new TranslateTransform();
            group.Children.Add(translateTransform);
        }

        return (scaleTransform, translateTransform);
    }

    private static T? FindAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = child;

        while (current != null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is T matched)
                return matched;
        }

        return null;
    }
}
