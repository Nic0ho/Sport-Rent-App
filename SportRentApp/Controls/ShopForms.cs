using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SportRentApp
{
    public sealed class InputNotice : Button
    {
        public InputNotice()
        {
            Content = "!";
            Width = 38;
            Height = 38;
            FontSize = 26;
            Foreground = Brushes.Firebrick;
            Background = Brushes.Gainsboro;
            Margin = new Thickness(8, 0, 0, 0);
            VerticalAlignment = VerticalAlignment.Top;
            Visibility = Visibility.Collapsed;
            SetResourceReference(StyleProperty, "CatalogButton");
            Padding = new Thickness(0);
            Click += (s, e) =>
            {
                var owner = Window.GetWindow(this);
                if (owner != null)
                    MessageBox.Show(owner, ToolTip as string ?? "Перевірте введені дані.", "Перевірте дані", MessageBoxButton.OK, MessageBoxImage.Information);
            };
        }

        public void Show(string message)
        {
            ToolTip = message;
            System.Windows.Automation.AutomationProperties.SetName(this, message);
            Visibility = Visibility.Visible;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        public void Clear()
        {
            Visibility = Visibility.Collapsed;
            ToolTip = null;
        }

        public static InputNotice Attach(Control input)
        {
            var parent = (Panel)input.Parent;
            int index = parent.Children.IndexOf(input);
            parent.Children.Remove(input);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new InputNotice();
            row.Children.Add(input);
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            parent.Children.Insert(index, row);
            if (input is TextBox text)
                text.TextChanged += (s, e) => badge.Clear();
            if (input is PasswordBox password)
                password.PasswordChanged += (s, e) => badge.Clear();
            if (input is ComboBox choice)
                choice.SelectionChanged += (s, e) => badge.Clear();
            if (input is DatePicker date)
                date.SelectedDateChanged += (s, e) => badge.Clear();
            return badge;
        }
    }

    public sealed class ShopForm : OverlayWindow
    {
        private readonly StackPanel fields = new StackPanel();
        private readonly Dictionary<string, InputNotice> notices = new Dictionary<string, InputNotice>();
        private readonly Dictionary<string, Func<string>> rules = new Dictionary<string, Func<string>>();
        public void Rule(string key, Func<string> validate)
        {
            rules[key] = validate;
        }

        public bool ValidateInputs()
        {
            Control first = null;
            foreach (var pair in rules)
            {
                notices[pair.Key].Clear();
                string message = pair.Value();
                if (message == null)
                    continue;
                notices[pair.Key].Show(message);
                if (first == null)
                    first = inputs[pair.Key];
            }

            if (first != null)
            {
                first.BringIntoView();
                first.Focus();
            }

            return first == null;
        }

        private readonly Button save;
        private readonly Dictionary<string, Control> inputs = new Dictionary<string, Control>();
        private readonly Dictionary<string, List<CheckBox>> tagInputs = new Dictionary<string, List<CheckBox>>();
        private readonly Dictionary<string, Action> tagChanges = new Dictionary<string, Action>();
        private bool busy;
        public WrapPanel FooterActions { get; } = new WrapPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Visibility = Visibility.Collapsed
        };
        public Func<Task> Submit { get; set; }

        public ShopForm(string title, string action = "Зберегти")
        {
            ResizeMode = ResizeMode.NoResize;
            Title = "Sport Drive — " + title;
            Width = 600;
            Height = 720;
            MinWidth = 500;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(49, 44, 44));
            Foreground = Brushes.Gainsboro;
            FontFamily = new FontFamily("pack://application:,,,/SportRentApp;component/Resources/Nunito/#Nunito");
            FontSize = 16;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SportRentApp;component/Styles/CatalogStyles.xaml", UriKind.Relative) });
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 18) });
            var scroll = new ScrollViewer
            {
                Content = fields,
                ClipToBounds = true,
                Style = null,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Padding = new Thickness(0, 0, 12, 0)
            };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);
            Grid.SetRow(FooterActions, 2);
            grid.Children.Add(FooterActions);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = new Button
            {
                Content = "Закрити",
                Style = (Style)FindResource("CatalogButton"),
                Margin = new Thickness(0, 0, 10, 0)
            };
            cancel.Click += (s, e) => Close();
            actions.Children.Add(cancel);
            save = new Button
            {
                Content = action,
                Style = (Style)FindResource("CatalogPrimary")
            };
            actions.Children.Add(save);
            save.Click += async (s, e) =>
            {
                if (busy || Submit == null || !ValidateInputs())
                    return;
                busy = true;
                save.IsEnabled = cancel.IsEnabled = fields.IsEnabled = FooterActions.IsEnabled = false;
                save.Content = "Зберігаємо…";
                try
                {
                    await Submit();
                    busy = false;
                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(DialogOwner ?? this, Friendly(ex), "Не вдалося зберегти", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    busy = false;
                    save.Content = action;
                    save.IsEnabled = cancel.IsEnabled = fields.IsEnabled = FooterActions.IsEnabled = true;
                }
            };
            Grid.SetRow(actions, 3);
            grid.Children.Add(actions);
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(94, 73, 73)),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(26),
                Margin = new Thickness(20),
                Child = grid
            };
            Closing += (s, e) =>
            {
                if (busy)
                    e.Cancel = true;
            };
        }

        public static string Friendly(Exception ex) => ex is ArgumentException || ex is UnauthorizedAccessException || ex is InvalidOperationException ? ex.Message : "Операцію не виконано. Оновіть дані та спробуйте ще раз.";
        public TextBlock Note(string text)
        {
            var note = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Silver,
                Margin = new Thickness(0, 4, 0, 14)
            };
            fields.Children.Add(note);
            return note;
        }

        public void Changed(string key, Action changed)
        {
            if (inputs[key] is TextBox text)
                text.TextChanged += (s, e) => changed();
            if (inputs[key] is ComboBox choice)
                choice.SelectionChanged += (s, e) => changed();
            if (inputs[key] is DatePicker date)
                date.SelectedDateChanged += (s, e) => changed();
        }

        public void SetValue(string key, string value)
        {
            if (inputs[key] is ComboBox choice)
                choice.SelectedItem = value;
            else if (inputs[key] is TextBox text)
                text.Text = value;
            else if (inputs[key] is DatePicker date)
                date.SelectedDate = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : (DateTime?)null;
        }

        public void ChoiceOptions(string key, IEnumerable<string> values, string selected)
        {
            ComboBox choice = (ComboBox)inputs[key];
            choice.ItemsSource = values.ToList();
            choice.SelectedItem = selected;
        }

        public void TagsChanged(string key, Action changed)
        {
            tagChanges[key] = changed;
        }

        public void SetTag(string key, string tag, bool selected)
        {
            tagInputs[key].Single(x => (string)x.Content == tag).IsChecked = selected;
        }

        private void WatchTags(string key, IEnumerable<CheckBox> choices)
        {
            foreach (var choice in choices)
            {
                RoutedEventHandler changed = (s, e) =>
                {
                    Action callback;
                    if (tagChanges.TryGetValue(key, out callback))
                        callback();
                };
                choice.Checked += changed;
                choice.Unchecked += changed;
            }
        }

        private void Label(string text) => fields.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("CatalogLabel") });
        public void Text(string key, string label, string value = "", bool multiline = false, bool readOnly = false)
        {
            Label(label);
            var input = new TextBox
            {
                Text = value ?? "",
                Style = (Style)FindResource("CatalogInput"),
                IsReadOnly = readOnly,
                MaxLength = multiline ? 4000 : 160
            };
            if (multiline)
            {
                input.AcceptsReturn = true;
                input.TextWrapping = TextWrapping.Wrap;
                input.MinHeight = 90;
                input.VerticalContentAlignment = VerticalAlignment.Top;
            }

            inputs[key] = input;
            fields.Children.Add(input);
            notices[key] = InputNotice.Attach(input);
            if (!readOnly)
                Rule(key, () => ValidateText(key));
        }

        public void GenerateValue(string key, Func<string> generate)
        {
            var input = (TextBox)inputs[key];
            var row = (Grid)input.Parent;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var button = new Button
            {
                Content = "Згенерувати",
                Style = (Style)FindResource("CatalogButton"),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 6, 12, 6),
                VerticalAlignment = VerticalAlignment.Top,
                Height = 38
            };
            Grid.SetColumn(button, row.ColumnDefinitions.Count - 1);
            button.Click += (s, e) => input.Text = generate();
            row.Children.Add(button);
        }

        private DatePicker Date(string key, string label, string value, DateTime? earliest)
        {
            Label(label);
            var input = new DatePicker
            {
                Style = (Style)FindResource("CatalogDate"),
                Language = System.Windows.Markup.XmlLanguage.GetLanguage("uk-UA"),
                FirstDayOfWeek = DayOfWeek.Monday,
                DisplayDateStart = earliest
            };
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            input.PreviewKeyDown += (s, e) =>
            {
                if (!input.IsDropDownOpen && (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space))
                {
                    input.IsDropDownOpen = true;
                    e.Handled = true;
                }
            };
            inputs[key] = input;
            fields.Children.Add(input);
            notices[key] = InputNotice.Attach(input);
            Rule(key, () => input.SelectedDate.HasValue ? null : "Оберіть дату в календарі.");
            SetValue(key, value);
            return input;
        }

        public void DateRange(string start, string end, DateTime? earliest = null)
        {
            var from = Date("start", "Початок оренди", start, earliest);
            var to = Date("end", "Повернення", end, earliest);
            Action limit = () =>
            {
                var min = from.SelectedDate ?? earliest;
                if (min.HasValue && to.SelectedDate < min)
                    to.SelectedDate = min;
                to.DisplayDateStart = min;
                if (min.HasValue && to.DisplayDate < min)
                    to.DisplayDate = min.Value;
            };
            from.SelectedDateChanged += (s, e) => limit();
            limit();
        }

        public void Choice(string key, string label, IEnumerable<string> options, string value = null)
        {
            Label(label);
            var input = new ComboBox
            {
                ItemsSource = options,
                Style = (Style)FindResource("CatalogSelect")
            };
            input.SelectedItem = value;
            if (input.SelectedIndex < 0)
                input.SelectedIndex = 0;
            inputs[key] = input;
            fields.Children.Add(input);
            notices[key] = InputNotice.Attach(input);
            Rule(key, () => string.IsNullOrEmpty(Value(key)) ? "Оберіть значення." : null);
        }

        public void Password(string key, string label)
        {
            Label(label);
            var input = new PasswordBox
            {
                Style = (Style)FindResource("CatalogPassword"),
                MaxLength = 256
            };
            inputs[key] = input;
            fields.Children.Add(input);
            notices[key] = InputNotice.Attach(input);
            if (key.EndsWith("Repeat"))
            {
                string original = key.Substring(0, key.Length - 6);
                Rule(original, () => Value(original).Length < 8 ? "Пароль: щонайменше 8 символів." : null);
                Rule(key, () => Value(key) != Value(original) ? "Паролі не збігаються." : null);
            }
        }

        public void Tags(string key, string value, IEnumerable<string> options = null)
        {
            Label("Позначки");
            var selected = (value ?? "").Split(',').Select(x => x.Trim()).ToArray();
            var panel = new WrapPanel();
            var choices = (options ?? ShopService.TagNames).Select(tag => new CheckBox { Content = tag, IsChecked = selected.Contains(tag), Style = (Style)FindResource("CatalogTag") }).ToList();
            foreach (var choice in choices)
                panel.Children.Add(choice);
            tagInputs[key] = choices;
            fields.Children.Add(panel);
            WatchTags(key, choices);
        }

        public void TagOptions(string key, IEnumerable<string> options)
        {
            var current = tagInputs[key];
            var panel = (Panel)current[0].Parent;
            string value = Value(key);
            panel.Children.Clear();
            var selected = value.Split(',').Select(x => x.Trim()).ToArray();
            var choices = options.Select(tag => new CheckBox { Content = tag, IsChecked = selected.Contains(tag), Style = (Style)FindResource("CatalogTag") }).ToList();
            foreach (var choice in choices)
                panel.Children.Add(choice);
            tagInputs[key] = choices;
            WatchTags(key, choices);
        }

        public void ContentItem(UIElement element)
        {
            fields.Children.Add(element);
        }

        public Button Action(string label, Action action, Panel row = null)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("CatalogButton"),
                Margin = row == null ? new Thickness(0, 16, 0, 0) : new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += (s, e) => action();
            (row ?? fields).Children.Add(button);
            if (row == FooterActions)
                FooterActions.Visibility = Visibility.Visible;
            return button;
        }

        public string ConfirmedPassword(string key = "password")
        {
            if (Value(key) != Value(key + "Repeat"))
                throw new ArgumentException("Паролі не збігаються.");
            return Value(key);
        }

        public void DeleteAction(Func<Task> remove)
        {
            var button = new Button
            {
                Content = "Видалити працівника",
                Style = (Style)FindResource("CatalogButton"),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            FooterActions.Children.Add(button);
            FooterActions.Visibility = Visibility.Visible;
            button.Click += async (s, e) =>
            {
                if (busy || MessageBox.Show(DialogOwner ?? this, "Видалити працівника? Вхід буде закритий, історія операцій збережеться.", "Видалення працівника", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                busy = true;
                fields.IsEnabled = save.IsEnabled = FooterActions.IsEnabled = false;
                try
                {
                    await remove();
                    busy = false;
                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(DialogOwner ?? this, Friendly(ex), "Не вдалося видалити", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    busy = false;
                    fields.IsEnabled = save.IsEnabled = FooterActions.IsEnabled = true;
                }
            };
        }

        private string ValidateText(string key)
        {
            string value = Value(key).Trim();
            if (key == "name")
                return InputRules.NameError(value);
            if (key == "login")
                return InputRules.LoginError(value);
            if (key == "phone")
            {
                try
                {
                    ShopService.NormalizePhone(value);
                    return null;
                }
                catch (ArgumentException ex)
                {
                    return ex.Message;
                }
            }

            if (key == "message")
                return value.Length < 5 || value.Length > 2000 ? "Опишіть зміну: 5–2000 символів." : null;
            if (key == "note")
                return value.Length == 0 ? "Додайте пояснення рішення." : null;
            if (key == "shop")
                return InputRules.StoreNumber(value, out _) ? null : "Введіть номер магазину.";
            if (new[] { "price", "deposit", "penalty", "total", "amount" }.Contains(key))
            {
                decimal amount;
                return InputRules.TryMoney(value, out amount, key == "amount") ? null : "Сума до 1 000 000 грн, максимум 2 знаки після коми" + (key == "amount" ? ", більша за нуль." : ".");
            }

            if (new[] { "order", "visit", "parent" }.Contains(key))
            {
                int id;
                return value.Length > 0 && (!int.TryParse(value, out id) || id <= 0) ? "ID: додатне ціле число." : null;
            }

            if (new[] { "start", "end", "actual" }.Contains(key))
            {
                if (key == "actual" && value.Length == 0)
                    return null;
                DateTime date, start;
                if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    return "Дата у форматі РРРР-ММ-ДД, наприклад 2026-11-01.";
                if (key != "start" && DateTime.TryParseExact(Value("start"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) && date < start)
                    return "Повернення не може бути раніше початку оренди.";
            }

            return null;
        }

        public static void ChangePassword(Window owner, int accountId, bool own)
        {
            var form = new ShopForm(own ? "Змінити пароль" : "Скинути пароль", "Зберегти пароль")
            {
                Owner = owner,
                MinWidth = 440,
                MinHeight = 360,
                Width = 520,
                Height = own ? 500 : 420
            };
            if (own)
                form.Password("current", "Поточний пароль");
            form.Password("password", "Новий пароль (від 8 символів)");
            form.Password("passwordRepeat", "Повторіть новий пароль");
            if (own)
                form.Rule("current", () => form.Value("current").Length == 0 ? "Введіть поточний пароль." : null);
            form.Rule("password", () => form.Value("password").Length < 8 ? "Пароль: щонайменше 8 символів." : null);
            form.Rule("passwordRepeat", () => form.Value("password") != form.Value("passwordRepeat") ? "Паролі не збігаються." : null);
            form.Submit = () => new ShopService().ChangePasswordAsync(accountId, form.Value("current"), form.ConfirmedPassword());
            form.ShowDialog();
        }

        public string Value(string key)
        {
            if (tagInputs.ContainsKey(key))
                return string.Join(", ", tagInputs[key].Where(x => x.IsChecked == true).Select(x => (string)x.Content));
            if (!inputs.ContainsKey(key))
                return "";
            var input = inputs[key];
            return input is TextBox ? ((TextBox)input).Text : input is PasswordBox ? ((PasswordBox)input).Password : input is DatePicker ? ((DatePicker)input).SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "" : ((ComboBox)input).SelectedItem as string;
        }

        public void ReadOnly()
        {
            save.Visibility = Visibility.Collapsed;
        }
    }
}
