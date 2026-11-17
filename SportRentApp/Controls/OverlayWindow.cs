using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SportRentApp
{
    public class OverlayWindow : Window
    {
        private DispatcherFrame frame;
        private bool? result;
        public new Window Owner { get; set; }
        public Window DialogOwner => Owner is OverlayWindow form ? form.DialogOwner : Owner ?? Application.Current.MainWindow;

        public new bool? DialogResult
        {
            get => frame == null ? base.DialogResult : result;
            set
            {
                if (frame == null)
                    base.DialogResult = value;
                else
                {
                    result = value;
                    Close();
                }
            }
        }

        public new void Close()
        {
            if (frame == null)
            {
                base.Close();
                return;
            }

            var args = new CancelEventArgs();
            OnClosing(args);
            if (!args.Cancel)
                frame.Continue = false;
        }

        public new bool? ShowDialog()
        {
            var owner = DialogOwner;
            if (owner == null || owner == this || !owner.IsVisible)
                return base.ShowDialog();
            var background = (UIElement)owner.Content;
            var content = (FrameworkElement)Content;
            var effect = background.Effect;
            bool enabled = background.IsEnabled;
            var focus = Keyboard.FocusedElement;
            Content = null;
            owner.Content = null;
            var root = new Grid();
            root.Children.Add(background);
            var shell = new Grid
            {
                Width = Width,
                Height = Height,
                Resources = Resources
            };
            TextElement.SetFontFamily(shell, FontFamily);
            TextElement.SetFontSize(shell, FontSize);
            TextElement.SetForeground(shell, Foreground);
            shell.Children.Add(content);
            var shade = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(85, 0, 0, 0))
            };
            shade.Children.Add(new Viewbox {
                Child = shell,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Margin = new Thickness(12)
            });
            KeyboardNavigation.SetTabNavigation(shade, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(shade, KeyboardNavigationMode.Cycle);
            root.Children.Add(shade);
            owner.Content = root;
            background.Effect = new BlurEffect
            {
                Radius = 9
            };
            background.IsEnabled = false;
            frame = new DispatcherFrame();
            CancelEventHandler closing = (s, e) =>
            {
                Close();
                if (frame.Continue)
                    e.Cancel = true;
            };
            KeyEventHandler escape = (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
            owner.Closing += closing;
            shade.PreviewKeyDown += escape;
            try
            {
                shade.Dispatcher.BeginInvoke(new Action(() => shade.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))), DispatcherPriority.Input);
                Dispatcher.PushFrame(frame);
                return result;
            }
            finally
            {
                owner.Closing -= closing;
                shade.PreviewKeyDown -= escape;
                shell.Children.Remove(content);
                root.Children.Remove(background);
                owner.Content = background;
                background.Effect = effect;
                background.IsEnabled = enabled;
                Content = content;
                frame = null;
                focus?.Focus();
                base.Close();
            }
        }
    }
}
