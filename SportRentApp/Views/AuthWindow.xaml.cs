using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SportRentApp
{
    public partial class AuthWindow : Window
    {
        private enum AuthTab
        {
            Login,
            Kiosk
        }

        private AuthTab selectedTab = AuthTab.Login;
        public static readonly DependencyProperty OpeningTopProperty = DependencyProperty.Register("OpeningTop", typeof(double), typeof(AuthWindow), new PropertyMetadata(18.0, OnOpeningChanged));
        public static readonly DependencyProperty OpeningBottomProperty = DependencyProperty.Register("OpeningBottom", typeof(double), typeof(AuthWindow), new PropertyMetadata(245.0, OnOpeningChanged));
        public double OpeningTop { get => (double)GetValue(OpeningTopProperty); set => SetValue(OpeningTopProperty, value); }
        public double OpeningBottom { get => (double)GetValue(OpeningBottomProperty); set => SetValue(OpeningBottomProperty, value); }

        private static void OnOpeningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var w = d as AuthWindow;
            if (w != null && w.IsLoaded)
            {
                w.UpdateContour();
            }
        }

        private void ShowErrorBadge(Border badge, string message)
        {
            if (badge == null)
                return;
            badge.ToolTip = new ToolTip
            {
                Content = message,
                FontSize = 14,
                FontFamily = new FontFamily("pack://application:,,,/SportRentApp;component/Resources/Nunito/#Nunito")
            };
            badge.Visibility = Visibility.Visible;
            var trans = badge.RenderTransform as TranslateTransform;
            if (trans == null)
            {
                trans = new TranslateTransform();
                badge.RenderTransform = trans;
            }

            badge.BeginAnimation(UIElement.OpacityProperty, null);
            trans.BeginAnimation(TranslateTransform.XProperty, null);
            var slide = new DoubleAnimation
            {
                From = -18.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };
            var fade = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(220)
            };
            badge.BeginAnimation(UIElement.OpacityProperty, fade);
            trans.BeginAnimation(TranslateTransform.XProperty, slide);
        }

        private void HideErrorBadge(Border badge)
        {
            if (badge == null)
                return;
            badge.BeginAnimation(OpacityProperty, null);
            if (badge.RenderTransform is TranslateTransform shift)
                shift.BeginAnimation(TranslateTransform.XProperty, null);
            badge.Visibility = Visibility.Collapsed;
        }

        private void ResetFieldStyles()
        {
            var normalBg = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
            textBoxLogin.ToolTip = null;
            borderLogin.Background = normalBg;
            if (badgeLogin != null)
                badgeLogin.Visibility = Visibility.Collapsed;
            passwordBox.ToolTip = null;
            borderPassword.Background = normalBg;
            if (badgePassword != null)
                badgePassword.Visibility = Visibility.Collapsed;
        }

        public AuthWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateContour();
            SelectTab(AuthTab.Login, animate: false);
        }

        private Canvas GetPage(AuthTab tab)
        {
            return tab == AuthTab.Login ? pageAuth : pageKiosk;
        }

        private void SelectTab(AuthTab tab, bool animate = true)
        {
            if (animate && (tab == selectedTab || submitting))
                return;
            AuthTab previousTab = selectedTab;
            Canvas newPage = GetPage(tab);
            selectedTab = tab;
            double targetTop = tab == AuthTab.Login ? 18.0 : 260.0;
            double targetBottom = tab == AuthTab.Login ? 245.0 : 370.0;
            AnimateProperty(OpeningTopProperty, targetTop, animate);
            AnimateProperty(OpeningBottomProperty, targetBottom, animate);
            var tabs = new[] { tabBtnAuth, tabBtnKiosk };
            var tabTypes = new[] { AuthTab.Login, AuthTab.Kiosk };
            for (int i = 0; i < tabs.Length; i++)
            {
                AuthTab tabType = tabTypes[i];
                if (animate && tabType != tab && tabType != previousTab)
                    continue;
                bool isSelected = tabType == tab;
                double destination = isSelected ? 0 : 6;
                var tabButton = tabs[i];
                AnimateOpacity(tabButton, isSelected ? 1.0 : 0.5, animate);
                var shift = tabButton.RenderTransform as TranslateTransform;
                if (shift == null)
                    tabButton.RenderTransform = shift = new TranslateTransform();
                if (animate)
                    shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(destination, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else
                {
                    shift.BeginAnimation(TranslateTransform.XProperty, null);
                    shift.X = destination;
                }
            }

            foreach (var page in new[] { pageAuth, pageKiosk })
            {
                page.BeginAnimation(OpacityProperty, null);
                ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
                page.Opacity = 1;
                page.Visibility = page == newPage ? Visibility.Visible : Visibility.Collapsed;
                page.IsHitTestVisible = page == newPage;
                ((TranslateTransform)page.RenderTransform).X = 0;
            }

            if (animate)
            {
                newPage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
                ((TranslateTransform)newPage.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tab == AuthTab.Kiosk ? 35 : -35, 0, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }

            ResetFieldStyles();
            if (tab == AuthTab.Login)
                textBoxLogin.Focus();
            else
                kioskIdentifier.Focus();
        }

        private void Credentials_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter || (e.OriginalSource != passwordBox && e.OriginalSource != kioskCode))
                return;
            e.Handled = true;
            if (e.IsRepeat || submitting)
                return;
            if (selectedTab == AuthTab.Login)
                BtnSignIn_Click(btnSignIn, new RoutedEventArgs());
            else
                KioskMode_Click(btnOpenKiosk, new RoutedEventArgs());
        }

        private void SetSubmitting(bool value)
        {
            submitting = value;
            btnSignIn.IsEnabled = btnOpenKiosk.IsEnabled = !value;
        }

        private void AnimateProperty(DependencyProperty property, double destination, bool animate)
        {
            double current = (double)GetValue(property);
            BeginAnimation(property, null);
            SetValue(property, destination);
            if (!animate)
            {
                UpdateContour();
                return;
            }

            var anim = new DoubleAnimation
            {
                From = current,
                To = destination,
                Duration = TimeSpan.FromMilliseconds(240),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut
                },
                FillBehavior = FillBehavior.Stop
            };
            BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
        }

        private void AnimateOpacity(UIElement element, double targetOpacity, bool animate)
        {
            if (element == null)
                return;
            if (!animate)
            {
                element.Opacity = targetOpacity;
                return;
            }

            var anim = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(240)
            };
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void UpdateContour()
        {
            if (pathContentBg == null || shadowGrid == null || pathShadow == null || pathFrame == null)
                return;
            Geometry opening = BuildOpening(OpeningTop, OpeningBottom);
            pathContentBg.Data = opening;
            decoCanvas.Clip = opening;
            if (canvasFormHost != null)
                canvasFormHost.Clip = opening;
            shadowGrid.Clip = opening;
            pathShadow.Data = opening;
            var outer = new RectangleGeometry(new Rect(0, 0, 880, 590));
            pathFrame.Data = Geometry.Combine(outer, opening, GeometryCombineMode.Exclude, null);
        }

        private static double Clamp(double val, double min, double max)
        {
            if (val < min)
                return min;
            if (val > max)
                return max;
            return val;
        }

        private static Geometry BuildOpening(double selectedTop, double selectedBottom)
        {
            const double left = 20;
            const double top = 18;
            const double mainRight = 800;
            const double tabRight = 855;
            const double bottom = 570;
            const double corner = 17;
            double a = Clamp(selectedTop, top, bottom - 1);
            double b = Clamp(selectedBottom, a + 1, bottom);
            var points = new List<Point>
            { new Point(left, top) };
            if (a > top + 0.001)
            {
                points.Add(new Point(mainRight, top));
                points.Add(new Point(mainRight, a));
                points.Add(new Point(tabRight, a));
            }
            else
            {
                points.Add(new Point(tabRight, top));
            }

            if (b < bottom - 0.001)
            {
                points.Add(new Point(tabRight, b));
                points.Add(new Point(mainRight, b));
                points.Add(new Point(mainRight, bottom));
            }
            else
            {
                points.Add(new Point(tabRight, bottom));
            }

            points.Add(new Point(left, bottom));
            return RoundedPolygon(points.ToArray(), corner);
        }

        private static Geometry RoundedPolygon(Point[] points, double requestedRadius)
        {
            int count = points.Length;
            var entries = new Point[count];
            var exits = new Point[count];
            var radii = new double[count];
            var sweeps = new SweepDirection[count];
            for (int i = 0; i < count; i++)
            {
                Point previous = points[(i + count - 1) % count];
                Point current = points[i];
                Point next = points[(i + 1) % count];
                Vector towardPrevious = previous - current;
                Vector towardNext = next - current;
                double radius = Math.Min(requestedRadius, Math.Min(towardPrevious.Length, towardNext.Length) / 2.0);
                towardPrevious.Normalize();
                towardNext.Normalize();
                entries[i] = current + towardPrevious * radius;
                exits[i] = current + towardNext * radius;
                radii[i] = radius;
                Vector incoming = current - previous;
                Vector outgoing = next - current;
                double cross = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
                sweeps[i] = cross >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(exits[0], true, true);
                for (int step = 1; step <= count; step++)
                {
                    int i = step % count;
                    context.LineTo(entries[i], true, false);
                    if (radii[i] < 0.001)
                    {
                        context.LineTo(exits[i], true, false);
                    }
                    else
                    {
                        context.ArcTo(exits[i], new Size(radii[i], radii[i]), 0, false, sweeps[i], true, true);
                    }
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private void TextBoxLogin_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (hintLogin != null)
                hintLogin.Visibility = string.IsNullOrEmpty(textBoxLogin.Text) ? Visibility.Visible : Visibility.Collapsed;
            HideErrorBadge(badgeLogin);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (hintPassword != null)
                hintPassword.Visibility = string.IsNullOrEmpty(passwordBox.Password) ? Visibility.Visible : Visibility.Collapsed;
            HideErrorBadge(badgePassword);
        }

        private void KioskIdentifier_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (hintKioskIdentifier != null)
                hintKioskIdentifier.Visibility = string.IsNullOrEmpty(kioskIdentifier.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void KioskCode_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (hintKioskCode != null)
                hintKioskCode.Visibility = string.IsNullOrEmpty(kioskCode.Password) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TabAuth_Click(object sender, RoutedEventArgs e)
        {
            SelectTab(AuthTab.Login);
        }

        private void TabKiosk_Click(object sender, RoutedEventArgs e)
        {
            SelectTab(AuthTab.Kiosk);
        }

        private bool submitting;
        private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
        {
            if (submitting || selectedTab != AuthTab.Login)
                return;
            if (string.IsNullOrWhiteSpace(textBoxLogin.Text))
            {
                ShowErrorBadge(badgeLogin, "Введіть логін.");
                textBoxLogin.Focus();
                return;
            }

            if (string.IsNullOrEmpty(passwordBox.Password))
            {
                ShowErrorBadge(badgePassword, "Введіть пароль.");
                passwordBox.Focus();
                return;
            }

            SetSubmitting(true);
            try
            {
                var user = await new ShopService().SignInAsync(textBoxLogin.Text.Trim(), passwordBox.Password);
                if (user == null)
                {
                    const string message = "Невірний логін або пароль. Спробуйте ще раз.";
                    MessageBox.Show(this, message, "Вхід", MessageBoxButton.OK, MessageBoxImage.Information);
                    passwordBox.Clear();
                    passwordBox.Focus();
                    ShowErrorBadge(badgePassword, message);
                    return;
                }

                UserSession.CurrentUser = user;
                var main = new MainWindow();
                Application.Current.MainWindow = main;
                main.Show();
                Close();
            }
            catch (Exception ex)
            {
                string message = ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException ? ex.Message : "Не вдалося увійти. Перевірте доступ до бази даних.";
                ShowErrorBadge(badgePassword, message);
                MessageBox.Show(this, message, "Вхід", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                SetSubmitting(false);
            }
        }

        private async void KioskMode_Click(object sender, RoutedEventArgs e)
        {
            if (submitting)
                return;
            SetSubmitting(true);
            try
            {
                var user = await new ShopService().SignInAsync(kioskIdentifier.Text.Trim(), kioskCode.Password, true);
                if (user == null)
                {
                    MessageBox.Show(this, "Перевірте номер кіоску та код доступу.", "Кіоск", MessageBoxButton.OK, MessageBoxImage.Information);
                    kioskCode.Clear();
                    kioskCode.Focus();
                    return;
                }

                UserSession.CurrentUser = user;
                var main = new MainWindow();
                Application.Current.MainWindow = main;
                main.Show();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex is UnauthorizedAccessException ? ex.Message : "Не вдалося підключити термінал. Перевірте доступ до бази даних.", "Кіоск", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                SetSubmitting(false);
            }
        }
    }
}
