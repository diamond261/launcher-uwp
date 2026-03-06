using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Flarial.Launcher.Styles
{
    /// <summary>
    /// Interaction logic for MessageBox.xaml
    /// </summary>
    public partial class MessageBox : UserControl
    {
        public string Text { get; set; }
        public bool ShowFlarialLogo { get; set; }
        bool _closing;

        public MessageBox()
        {
            InitializeComponent();
            this.DataContext = this;
            Text = "temp";
        }

        private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
            => await CloseAsync();

        private async void MessageBox_OnLoaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(3000);
            await CloseAsync();
        }

        async Task CloseAsync()
        {
            if (_closing)
                return;

            _closing = true;
            var sb = new Storyboard();

            var an1 = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                To = 0,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var an2 = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                To = 0,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var an3 = new ThicknessAnimation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                To = new Thickness(0, 25, 0, -25),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(an1, this);
            Storyboard.SetTargetProperty(an1, new PropertyPath("RenderTransform.ScaleX"));
            Storyboard.SetTarget(an2, this);
            Storyboard.SetTargetProperty(an2, new PropertyPath("RenderTransform.ScaleY"));
            Storyboard.SetTarget(an3, this);
            Storyboard.SetTargetProperty(an3, new PropertyPath(MarginProperty));

            sb.Children.Add(an1);
            sb.Children.Add(an2);
            sb.Children.Add(an3);

            sb.Begin(this);

            await Task.Delay(200);
            RemoveFromVisualTree();
        }

        void RemoveFromVisualTree()
        {
            if (Parent is Panel directPanel)
            {
                directPanel.Children.Remove(this);
                return;
            }

            DependencyObject current = this;
            while (current is not null)
            {
                if (current is Panel panel)
                {
                    panel.Children.Remove(this);
                    return;
                }

                if (current is ContentControl contentControl && ReferenceEquals(contentControl.Content, this))
                {
                    contentControl.Content = null;
                    return;
                }

                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current) as DependencyObject;
            }
        }
    }
}
