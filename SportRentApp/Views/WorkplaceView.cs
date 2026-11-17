using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SportRentApp
{
    public sealed class WorkplaceView : UserControl
    {
        private readonly ShopService shop = new ShopService();
        private readonly CatalogService catalog = new CatalogService();
        private readonly StackPanel navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        public FrameworkElement Navigation => navigation;

        private readonly WrapPanel cards = new WrapPanel();
        private readonly TextBlock status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Silver
        };
        private readonly TextBlock resultCount = new TextBlock
        {
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(207, 191, 184))
        };
        private readonly ComboBox database = new ComboBox();
        private readonly ComboBox filter = new ComboBox();
        private readonly TextBox search = new TextBox();
        private readonly Button add = new Button();
        private readonly ContentControl body = new ContentControl();
        private DataGrid table;
        private ScrollViewer cardScroll;
        private readonly ColumnDefinition databaseColumn = new ColumnDefinition
        {
            Width = new GridLength(130)
        };
        private AccessPolicy access;
        private string section = "База даних";
        private List<Entry> entries = new List<Entry>();
        private int refreshVersion;
        private bool ready;
        private bool actionPending;
        private bool selectingDatabase;
        private string ViewName => section == "База даних" ? (string)database.SelectedItem : section;
        private bool IsJournal => ViewName == "Архів чатів" || ViewName == "Аудит";

        private Window dialogOwner;
        public async Task<FrameworkElement> EmbedAsync(Window owner)
        {
            dialogOwner = owner;
            ready = true;
            await RefreshAsync();
            return this;
        }

        private static readonly Dictionary<string, string> orderStates = new Dictionary<string, string>
        {
            { "Pending", "Очікує" },
            { "Called", "Викликано" },
            { "Serving", "Обслуговується" },
            { "Assembling", "Готується" },
            { "Ready", "Готове" },
            { "Active", "В оренді" },
            { "Completed", "Завершене" },
            { "Cancelled", "Скасоване" }
        };
        private class Entry
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Info { get; set; }
            public string Tags { get; set; }
            public DateTime? Date { get; set; }
            public object Value { get; set; }
        }

        public WorkplaceView(string initialSection = "База даних")
        {
            section = initialSection;
            Background = Brushes.Transparent;
            Foreground = Brushes.Gainsboro;
            FontFamily = new FontFamily("pack://application:,,,/SportRentApp;component/Resources/Nunito/#Nunito");
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SportRentApp;component/Styles/CatalogStyles.xaml", UriKind.Relative) });
            var grid = new Grid();
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                grid.RowDefinitions.Add(new RowDefinition { Height = height });
            navigation.HorizontalAlignment = HorizontalAlignment.Right;
            var toolbar = new Grid
            {
                Height = 38,
                Margin = new Thickness(-14, 0, 0, 16)
            };
            toolbar.ColumnDefinitions.Add(databaseColumn);
            toolbar.ColumnDefinitions.Add(new ColumnDefinition());
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            database.Style = (Style)FindResource("CatalogSelect");
            database.ItemsSource = new[] { "Працівники", "Спорядження", "Замовлення" };
            database.SelectedIndex = 0;
            database.Margin = new Thickness(0, 0, 10, 0);
            toolbar.Children.Add(database);
            search.Style = (Style)FindResource("CatalogInput");
            var searchArea = new Grid
            {
                Margin = new Thickness(0, 0, 10, 0)
            };
            searchArea.Children.Add(search);
            var hint = new TextBlock
            {
                Text = "Пошук за ім’ям, назвою або тегом…",
                Foreground = new SolidColorBrush(Color.FromRgb(110, 100, 95)),
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            searchArea.Children.Add(hint);
            search.TextChanged += (s, e) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(searchArea, 1);
            toolbar.Children.Add(searchArea);
            var countBadge = new Border
            {
                Style = (Style)FindResource("ResultBadge"),
                Margin = new Thickness(0, 0, 10, 0),
                Child = resultCount
            };
            resultCount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(countBadge, 2);
            toolbar.Children.Add(countBadge);
            filter.Style = (Style)FindResource("CatalogSelect");
            filter.ItemsSource = new[] { "Усі", "Очікують", "Кіоск", "Стажер" };
            filter.SelectedIndex = 0;
            Grid.SetColumn(filter, 3);
            toolbar.Children.Add(filter);
            add.Content = "＋ Додати";
            add.Style = (Style)FindResource("CatalogToolbarPrimary");
            add.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(add, 4);
            toolbar.Children.Add(add);
            add.Click += async (s, e) => await Guard(AddAsync);
            database.SelectionChanged += async (s, e) =>
            {
                if (ready && !selectingDatabase)
                {
                    search.Clear();
                    await RefreshAsync();
                }
            };
            filter.SelectionChanged += (s, e) => Draw();
            search.TextChanged += (s, e) => Draw();
            Grid.SetRow(toolbar, 2);
            grid.Children.Add(toolbar);
            Grid.SetRow(body, 3);
            grid.Children.Add(body);
            Content = new Border
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(26),
                Background = new SolidColorBrush(Color.FromRgb(94, 73, 73)),
                Child = grid
            };
            body.SizeChanged += (s, e) =>
            {
                body.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, body.ActualWidth), Math.Max(0, body.ActualHeight)), 12, 12);
                foreach (var button in cards.Children.OfType<Button>())
                    button.Width = CardWidth();
            };
        }

        private double CardWidth()
        {
            double available = Math.Max(260, body.ActualWidth);
            int columns = Math.Max(1, (int)(available / 280));
            return Math.Max(220, Math.Floor(available / columns) - 16);
        }

        private Button Button(string label, Func<Task> action)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("CatalogButton"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            button.Click += async (s, e) => await Guard(action);
            return button;
        }

        private async Task Guard(Func<Task> work)
        {
            if (actionPending)
                return;
            actionPending = true;
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                actionPending = false;
            }
        }

        private async Task RefreshAsync()
        {
            int version = ++refreshVersion;
            var session = UserSession.CurrentUser;
            entries.Clear();
            Draw();
            status.Text = "";
            try
            {
                var policy = await catalog.GetAccessAsync();
                if (version != refreshVersion || !ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                access = policy;
                if (!access.CanReadOperations)
                {
                    body.Content = null;
                    throw new UnauthorizedAccessException("Робочий простір доступний лише персоналу.");
                }

                selectingDatabase = true;
                string selectedDatabase = database.SelectedItem as string;
                database.ItemsSource = access.CanEditCatalog ? new[] { "Працівники", "Спорядження", "Замовлення", "Архів чатів", "Аудит" }

                : new[] { "Працівники", "Спорядження", "Замовлення" };
                database.SelectedItem = selectedDatabase;
                if (database.SelectedIndex < 0)
                    database.SelectedIndex = 0;
                selectingDatabase = false;
                navigation.Children.Clear();
                navigation.Margin = new Thickness(0);
                navigation.Visibility = dialogOwner != null && (section == "Команда" || section == "Клієнти") ? Visibility.Collapsed : Visibility.Visible;
                foreach (var label in new[] { "База даних", "Команда", "Клієнти", "Замовлення", "Зміни товарів", "Транзакції" })
                {
                    if (dialogOwner != null && (label == "Команда" || label == "Клієнти" || label == "Замовлення"))
                        continue;
                    if (label == "Транзакції" && !access.CanEditCatalog)
                        continue;
                    var button = Button(label, async () =>
                    {
                        if (section == label)
                            return;
                        section = label;
                        search.Clear();
                        filter.SelectedIndex = 0;
                        await RefreshAsync();
                    });
                    if (label == section)
                        button.Background = new SolidColorBrush(Color.FromRgb(94, 107, 88));
                    navigation.Children.Add(button);
                }

                if (!access.CanEditCatalog && section == "Транзакції")
                    section = "База даних";
                database.Visibility = section == "База даних" ? Visibility.Visible : Visibility.Collapsed;
                databaseColumn.Width = section == "База даних" ? new GridLength(180) : new GridLength(0);
                string view = section == "База даних" ? (string)database.SelectedItem : section;
                string selectedFilter = filter.SelectedItem as string;
                filter.ItemsSource = IsJournal ? new[] { "Усі", "Сьогодні", "7 днів" }

                : view == "Транзакції" ? new[] { "Усі" }.Concat(ShopService.TransactionKinds).ToArray() : view == "Замовлення" ? new[] { "Усі", "Електронна черга", "У магазині", "Очікує", "В оренді", "Прострочене", "Завершене" }

                : view == "Зміни товарів" ? new[] { "Усі", "Очікують", "Прийнято", "Відхилено" }

                : view == "Спорядження" ? new[] { "Усі", "Доступний", "В оренді", "На обслуговуванні" }

                : view == "Клієнти" ? new[] { "Усі", "Порушення правил", "Бан" }

                : new[] { "Усі", "Очікують", "Кіоск", "Стажер" };
                filter.SelectedItem = selectedFilter;
                if (filter.SelectedIndex < 0)
                    filter.SelectedIndex = 0;
                List<Entry> result;
                if (view == "Працівники" || view == "Команда" || view == "Клієнти")
                    result = (await shop.AccountsAsync(view == "Клієнти")).Select(x => new Entry {
                        Id = x.Id,
                        Name = x.FullName,
                        Info = x.Role == "Kiosk" ? "Кіоск № " + x.Id + " · Магазин № " + x.ShopCode : x.Role == "Client" ? (x.Phone ?? "") : x.PrivateVisible ? x.Username + " · " + x.Phone : "",
                        Tags = RoleLabel(x.Role) + ", " + StateLabel(x.Status) + (string.IsNullOrWhiteSpace(x.Tags) ? "" : ", " + x.Tags),
                        Value = x
                    }).ToList();
                else if (view == "Спорядження")
                    result = (await catalog.LoadInventoryAsync()).Select(x => new Entry { Id = x.Id, Name = x.Name, Info = x.PriceLabel, Tags = x.Category + ", " + x.StatusLabel, Value = x }).ToList();
                else if (view == "Замовлення")
                {
                    result = (await catalog.LoadRentalsAsync()).Select(x => new Entry {
                        Id = x.Id,
                        Name = "Замовлення " + (x.OrderNumber ?? x.Id.ToString()),
                        Info = (x.User?.FullName ?? "Гість") + " · " + (x.Equipment?.Name ?? "Талон") + " · " + x.TotalPrice.ToString("0.00") + " грн",
                        Tags = (orderStates.ContainsKey(x.Status) ? orderStates[x.Status] : x.Status) + ", " + (x.QueueType == "Online" ? "Електронна черга" : "У магазині") + (x.IsOverdue ? ", Прострочене" : "") + (x.RequiresAdmin == 1 ? ", Потребує менеджера" : ""),
                        Value = x
                    }).ToList();
                }
                else if (view == "Зміни товарів")
                    result = (await shop.RequestsAsync()).Select(x => new Entry {
                        Id = x.Id,
                        Name = "Спорядження #" + x.EquipmentId,
                        Info = x.Message,
                        Tags = StateLabel(x.Status) + ", Автор #" + x.RequesterId,
                        Value = x
                    }).ToList();
                else if (view == "Архів чатів")
                    result = (await new JournalService().ChatsAsync()).Select(x => new Entry {
                        Name = x.Participants,
                        Info = x.MessageCount + " повідомлень",
                        Date = LocalDate(x.LastMessageAt),
                        Value = x
                    }).ToList();
                else if (view == "Аудит")
                    result = (await new JournalService().AuditAsync()).Select(x => new Entry {
                        Id = x.Id,
                        Name = x.Action,
                        Info = x.ActorName + " · " + x.Target + (string.IsNullOrWhiteSpace(x.Details) ? "" : " · " + x.Details),
                        Date = LocalDate(x.CreatedAt),
                        Value = x
                    }).ToList();
                else
                {
                    var names = (await shop.AccountsAsync(true)).ToDictionary(x => x.Id, x => x.FullName);
                    result = (await shop.TransactionsAsync()).Select(x => new Entry {
                        Id = x.Id,
                        Name = x.Kind + " · " + (x.AmountMinor / 100m).ToString("0.00") + " грн",
                        Info = (names.ContainsKey(x.ClientId) ? names[x.ClientId] : "Клієнт #" + x.ClientId) + " · " + (x.OrderId.HasValue ? "Замовлення #" + x.OrderId : "Без замовлення") + " · " + x.Notes,
                        Tags = "Демонстрація, " + x.Kind,
                        Value = x
                    }).ToList();
                }

                if (version != refreshVersion || !ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                entries = result;
                Draw();
                var currentStaff = await new TeamService().ProfileAsync(session.Id);
                if (version != refreshVersion || !ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                bool cashier = access.CanEditCatalog || (currentStaff.IsOnShift && StaffWork.Has(currentStaff.Tags, "Каса"));
                add.Visibility = (view == "Клієнти" || view == "Замовлення" ? cashier : access.CanEditCatalog) && !IsJournal && view != "Зміни товарів" ? Visibility.Visible : Visibility.Collapsed;
                status.Text = "";
                status.Margin = string.IsNullOrEmpty(status.Text) ? new Thickness(0) : new Thickness(0, 15, 0, 0);
            }
            catch (Exception ex)
            {
                if (version == refreshVersion && ReferenceEquals(session, UserSession.CurrentUser))
                {
                    entries.Clear();
                    Draw();
                    ShowError(ex);
                }
            }
        }

        private void ShowError(Exception error)
        {
            status.Text = ShopForm.Friendly(error);
            var owner = dialogOwner ?? Window.GetWindow(this);
            if (owner != null)
                MessageBox.Show(owner, status.Text, "Не вдалося виконати дію", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Draw()
        {
            if (!ready)
                return;
            string query = search.Text.Trim();
            string tag = filter.SelectedItem as string;
            var filtered = entries.Where(x => (x.Name + " " + x.Info + " " + x.Tags).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 && (tag == "Усі" || string.IsNullOrEmpty(tag) || (IsJournal ? x.Date >= DateTime.Today.AddDays(tag == "Сьогодні" ? 0 : -6) : (x.Tags ?? "").Split(',').Any(xTag => xTag.Trim() == (tag == "Очікують" ? "Очікує" : tag))))).ToList();
            resultCount.Text = filtered.Count + " знайдено";
            if (section == "База даних")
            {
                if (table == null)
                {
                    table = new DataGrid
                    {
                        Style = (Style)FindResource("CatalogTable")
                    };
                    table.Columns.Add(new DataGridTextColumn {
                        Header = "Назва / ім’я",
                        Binding = new System.Windows.Data.Binding("Name"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                    });
                    table.Columns.Add(new DataGridTextColumn {
                        Header = "Інформація",
                        Binding = new System.Windows.Data.Binding("Info"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                    });
                    table.Columns.Add(new DataGridTextColumn { Header = "Теги", Binding = new System.Windows.Data.Binding("Tags"), Width = 230 });
                    var cellText = new Style(typeof(TextBlock));
                    cellText.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
                    foreach (DataGridTextColumn column in table.Columns)
                        column.ElementStyle = cellText;
                    table.MouseDoubleClick += async (s, e) =>
                    {
                        if (table.SelectedItem is Entry entry)
                            await Guard(() => OpenAsync(entry));
                    };
                    table.KeyDown += async (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Enter && table.SelectedItem is Entry entry)
                        {
                            e.Handled = true;
                            await Guard(() => OpenAsync(entry));
                        }
                    };
                }

                table.Columns[0].Header = ViewName == "Аудит" ? "Дія" : ViewName == "Архів чатів" ? "Учасники" : "Назва / ім’я";
                table.Columns[1].Header = ViewName == "Аудит" ? "Автор / об’єкт" : "Інформація";
                table.Columns[2].Header = IsJournal ? "Дата" : "Теги";
                ((DataGridTextColumn)table.Columns[2]).Binding = IsJournal ? new System.Windows.Data.Binding("Date")
                {
                    StringFormat = "dd.MM.yyyy HH:mm"
                }

                : new System.Windows.Data.Binding("Tags");
                table.ItemsSource = filtered;
                body.Content = table;
                return;
            }

            cards.Children.Clear();
            if (cardScroll == null)
                cardScroll = new ScrollViewer
                {
                    Content = cards,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
            body.Content = cardScroll;
            if (filtered.Count == 0)
                cards.Children.Add(new TextBlock { Text = "Немає записів за цим фільтром.", Margin = new Thickness(12, 20, 0, 0) });
            foreach (var entry in filtered)
            {
                var panel = new StackPanel
                {
                    MinHeight = 116
                };
                panel.Children.Add(new TextBlock { Text = entry.Name, FontSize = 20, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock {
                    Text = entry.Info,
                    Foreground = Brushes.Silver,
                    Margin = new Thickness(0, 7, 0, 9),
                    TextWrapping = TextWrapping.Wrap
                });
                var chips = new WrapPanel();
                foreach (var text in entry.Tags.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct())
                {
                    var color = text == "Стажер" ? "#795F3E" : text == "Кіоск" ? "#465F72" : text == "Очікує" ? "#80514D" : "#52634B";
                    chips.Children.Add(new Border { Background = (Brush)new BrushConverter().ConvertFromString(color), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 7, 5), Child = new TextBlock { Text = text, FontSize = 16, Foreground = Brushes.White } });
                }

                panel.Children.Add(chips);
                var button = Button("", () => OpenAsync(entry));
                button.Style = (Style)FindResource("CatalogRowButton");
                button.Content = panel;
                button.Background = new SolidColorBrush(Color.FromRgb(80, 60, 59));
                button.Margin = new Thickness(5, 8, 11, 8);
                button.Width = CardWidth();
                cards.Children.Add(button);
            }
        }

        private async Task AddAsync()
        {
            if (ViewName == "Клієнти")
            {
                RegisterClientForm();
                await RefreshAsync();
            }
            else if (section == "Транзакції")
                await TransactionFormAsync();
            else if (section == "Замовлення" || (section == "База даних" && (string)database.SelectedItem == "Замовлення"))
            {
                await OrderFormAsync(null);
                await RefreshAsync();
            }
            else if (section == "База даних" && (string)database.SelectedItem == "Спорядження")
            {
                new EquipmentEditor
                {
                    Owner = dialogOwner ?? Window.GetWindow(this)
                }.ShowDialog();
                await RefreshAsync();
            }
            else
            {
                var choice = new ShopForm("Новий обліковий запис")
                {
                    Owner = dialogOwner ?? Window.GetWindow(this)
                };
                choice.Action("Працівник", () =>
                {
                    choice.Close();
                    AccountForm(new AccountView { Role = "Employee", Status = "Active", PrivateVisible = true });
                });
                choice.Action("Термінал / кіоск", () =>
                {
                    choice.Close();
                    TerminalForm();
                });
                choice.ReadOnly();
                choice.ShowDialog();
                await RefreshAsync();
            }
        }

        private AccountView RegisterClientForm()
        {
            var form = new ShopForm("Новий клієнт", "Зареєструвати")
            {
                Owner = dialogOwner ?? Window.GetWindow(this),
                Height = 410,
                MinHeight = 350
            };
            form.Text("name", "Ім’я клієнта");
            form.Text("phone", "Номер телефону");
            AccountView client = null;
            form.Submit = async () =>
            {
                int id = await shop.RegisterClientAsync(form.Value("name"), form.Value("phone"));
                client = new AccountView
                {
                    Id = id,
                    FullName = form.Value("name").Trim(),
                    Role = "Client",
                    Status = "Active"
                };
            };
            return form.ShowDialog() == true ? client : null;
        }

        public async Task OpenTaskAsync(Window owner, WorkTask task)
        {
            dialogOwner = owner;
            access = await catalog.GetAccessAsync();
            if (task.Kind == "Rental" || task.Kind == "Return")
            {
                var order = (await catalog.LoadRentalsAsync(task.EntityId)).SingleOrDefault(x => x.Id == task.EntityId);
                if (order == null)
                    throw new InvalidOperationException("Замовлення недоступне.");
                await OrderFormAsync(order);
            }
            else if (task.Kind == "Account")
            {
                AccountForm(await shop.AccountAsync(task.EntityId.Value));
            }
            else if (task.Kind == "Request")
            {
                var request = (await shop.RequestsAsync()).Single(x => x.Id == task.EntityId);
                await OpenAsync(new Entry { Value = request });
            }
            else if (task.Kind == "Kiosk")
                TerminalForm();
        }

        private async Task OpenAsync(Entry entry)
        {
            if (entry.Value is ArchivedChat chat)
            {
                var rows = await new JournalService().MessagesAsync(chat.FirstUserId, chat.SecondUserId);
                var form = new ShopForm("Архів чату")
                {
                    Owner = dialogOwner ?? Window.GetWindow(this)
                };
                form.ReadOnly();
                form.Note(chat.Participants).FontSize = 21;
                foreach (var row in rows)
                {
                    form.Note(row.Sender + " · " + LocalDate(row.CreatedAt)?.ToString("dd.MM.yyyy HH:mm"));
                    form.Note(row.Body).FontSize = 18;
                }

                form.ShowDialog();
            }
            else if (entry.Value is AuditEntry audit)
            {
                (await catalog.GetAccessAsync()).RequireEdit();
                var form = new ShopForm(audit.Action)
                {
                    Owner = dialogOwner ?? Window.GetWindow(this),
                    Height = 480
                };
                form.ReadOnly();
                form.Note(audit.ActorName + " · " + LocalDate(audit.CreatedAt)?.ToString("dd.MM.yyyy HH:mm"));
                form.Note(audit.Target);
                if (!string.IsNullOrWhiteSpace(audit.Details))
                    form.Note(audit.Details);
                form.ShowDialog();
            }
            else if (entry.Value is AccountView client && client.Role == "Client")
            {
                await ShowCustomerAsync(client);
            }
            else if (entry.Value is AccountView account)
            {
                await TeamHub.ShowProfileAsync(dialogOwner ?? Window.GetWindow(this), account.Id);
            }
            else if (entry.Value is CatalogItem equipment)
            {
                if (access.CanEditCatalog)
                    new EquipmentEditor(equipment.Record)
                    {
                        Owner = dialogOwner ?? Window.GetWindow(this)
                    }.ShowDialog();
                else
                {
                    var form = new ShopForm("Запит на зміну спорядження", "Надіслати менеджеру")
                    {
                        Owner = dialogOwner ?? Window.GetWindow(this)
                    };
                    form.Note(equipment.Name);
                    form.Text("message", "Що потрібно змінити?", "", true);
                    form.Submit = () => shop.RequestEquipmentChangeAsync(equipment.Id, form.Value("message"));
                    form.ShowDialog();
                }
            }
            else if (entry.Value is Rental order)
                await OrderFormAsync(order);
            else if (entry.Value is EquipmentRequest request)
            {
                var form = new ShopForm("Заявка #" + request.Id, "Надіслати рішення")
                {
                    Owner = dialogOwner ?? Window.GetWindow(this)
                };
                form.Note(request.Message);
                if (access.CanEditCatalog && request.Status == "Pending")
                {
                    form.Choice("decision", "Рішення", new[] { "Прийняти до роботи", "Відхилити" });
                    form.Text("note", "Пояснення", "", true);
                    form.Submit = () => shop.ReviewRequestAsync(request.Id, request.Version, form.Value("decision") == "Прийняти до роботи", form.Value("note"));
                }
                else
                {
                    form.Note(request.Resolution ?? "Ще не розглянуто");
                    form.ReadOnly();
                }

                form.ShowDialog();
            }
            else if (entry.Value is ShopTransaction payment)
            {
                var form = new ShopForm("Демонстраційна операція #" + payment.Id)
                {
                    Owner = dialogOwner ?? Window.GetWindow(this)
                };
                form.Note(entry.Name);
                form.Note(entry.Info);
                form.Note("Дата UTC: " + payment.CreatedAt + "\nАвтор #" + payment.CreatedBy + "\nПов’язана операція: " + payment.RelatedTransactionId);
                form.ReadOnly();
                form.ShowDialog();
            }

            await RefreshAsync();
        }

        private async Task ShowCustomerAsync(AccountView client)
        {
            var history = await shop.OrdersForClientAsync(client.Id);
            var owner = dialogOwner ?? Window.GetWindow(this);
            var form = new ShopForm(client.FullName)
            {
                Owner = owner,
                Height = 610
            };
            form.ReadOnly();
            form.Note("Замовлення клієнта").FontSize = 20;
            if (history.Count == 0)
                form.Note("Замовлень ще немає.");
            int? chosen = null;
            bool settings = false;
            foreach (var order in history)
            {
                form.Action(order.Label, () =>
                {
                    chosen = order.Id;
                    form.Close();
                });
                form.Note(order.Summary);
            }

            if (access.CanEditCatalog)
                form.Action("Картка клієнта", () =>
                {
                    settings = true;
                    form.Close();
                }, form.FooterActions);
            form.ShowDialog();
            if (settings)
                await EditAccountAsync(owner, client.Id);
            if (chosen.HasValue)
            {
                var rental = (await catalog.LoadRentalsAsync(chosen)).SingleOrDefault();
                if (rental != null && rental.UserId == client.Id)
                    await OrderFormAsync(rental);
            }
        }

        public async Task EditAccountAsync(Window owner, int id)
        {
            dialogOwner = owner;
            access = await catalog.GetAccessAsync();
            AccountForm(await shop.AccountAsync(id));
        }

        private void AccountForm(AccountView row, List<ShopTransaction> history = null) => CreateAccountForm(row, history).ShowDialog();
        private ShopForm CreateAccountForm(AccountView row, List<ShopTransaction> history = null)
        {
            bool edit = access.CanEditCatalog, kiosk = row.Role == "Kiosk";
            bool approval = row.Id != 0 && row.Role == "Employee" && row.Status == "Pending";
            var form = new ShopForm(approval ? "Погодити працівника" : kiosk ? "Термінал" : row.Role == "Client" ? "Картка клієнта" : row.Id == 0 ? "Новий працівник" : "Керування доступом", approval ? "Зберегти рішення" : "Зберегти")
            {
                Owner = dialogOwner ?? Window.GetWindow(this),
                Height = kiosk ? 600 : 720
            };
            if (edit && kiosk && row.Id != 0)
                form.Action("Новий код доступу", () => TerminalForm(row));
            form.Text("name", kiosk ? "Назва термінала" : "Ім’я", row.FullName, readOnly: !edit);
            if (!kiosk)
            {
                if (edit)
                    form.Tags("tags", row.Tags, ShopService.AllowedTags(row.Role));
                else if (!string.IsNullOrWhiteSpace(row.Tags))
                    form.Note(row.Tags);
            }

            if (history != null && history.Count > 0)
                form.Note(string.Join("\n", history.Take(8).Select(x => "#" + x.Id + " · " + x.Kind + " · " + (x.AmountMinor / 100m).ToString("0.00") + " грн")));
            if (row.PrivateVisible)
            {
                if (kiosk)
                    form.Note("Кіоск № " + row.Id);
                else
                    form.Text("login", "Логін", row.Username, readOnly: !edit);
                if (!kiosk)
                {
                    form.Text("phone", "Телефон", row.Phone, readOnly: !edit);
                    form.Text("notes", "Примітки", row.Notes, true, !edit);
                }
            }

            if (kiosk)
            {
                form.Text("shop", "Номер магазину", row.ShopCode, readOnly: !edit);
                form.Rule("shop", () => InputRules.StoreNumber(form.Value("shop"), out _) ? null : "Введіть додатне ціле число.");
            }

            if (edit)
            {
                if (!kiosk)
                    form.Choice("role", "Рівень доступу", (row.Id == 0 ? new[] { "Employee" } : new[] { "Client", "Employee", "Admin" }).Select(RoleLabel), RoleLabel(row.Role));
                form.Choice("state", "Стан", ShopService.AccountStates.Select(StateLabel), StateLabel(approval ? "Active" : row.Status));
                if (!kiosk)
                {
                    bool syncing = false;
                    Func<bool> client = () => form.Value("role") == RoleLabel("Client");
                    form.Changed("role", () =>
                    {
                        form.TagOptions("tags", ShopService.AllowedTags(ShopService.AccountRoles.Single(x => RoleLabel(x) == form.Value("role"))));
                        if (client())
                            form.SetTag("tags", "Бан", form.Value("state") == StateLabel("Blocked"));
                        else if (row.Role == "Client" && form.Value("state") == StateLabel("Pending"))
                            form.SetValue("state", StateLabel("Active"));
                    });
                    form.TagsChanged("tags", () =>
                    {
                        if (syncing || !client())
                            return;
                        syncing = true;
                        if (form.Value("tags").Split(',').Any(x => x.Trim() == "Бан"))
                            form.SetValue("state", StateLabel("Blocked"));
                        else if (form.Value("state") == StateLabel("Blocked"))
                            form.SetValue("state", StateLabel("Active"));
                        syncing = false;
                    });
                    form.Changed("state", () =>
                    {
                        if (syncing || !client())
                            return;
                        syncing = true;
                        form.SetTag("tags", "Бан", form.Value("state") == StateLabel("Blocked"));
                        syncing = false;
                    });
                }

                if (row.Id == 0)
                {
                    form.Password("password", kiosk ? "Код доступу" : "Початковий пароль");
                    form.Password("passwordRepeat", kiosk ? "Повторіть код" : "Повторіть пароль");
                }

                form.Submit = () => shop.SaveAccountAsync(new AccountView {
                    Id = row.Id,
                    Version = row.Version,
                    FullName = form.Value("name"),
                    Username = kiosk ? row.Username : form.Value("login"),
                    Phone = kiosk ? "" : form.Value("phone"),
                    Notes = kiosk ? null : form.Value("notes"),
                    Role = kiosk ? "Kiosk" : ShopService.AccountRoles.Single(x => RoleLabel(x) == form.Value("role")),
                    Status = ShopService.AccountStates.Single(x => StateLabel(x) == form.Value("state")),
                    Tags = kiosk ? "Кіоск" : form.Value("tags"),
                    ShopCode = kiosk ? form.Value("shop") : null
                }, row.Id == 0 ? form.ConfirmedPassword() : "");
            }
            else
                form.ReadOnly();
            if (edit && row.Id != 0 && row.Role == "Employee")
                form.DeleteAction(() => shop.DeleteEmployeeAsync(row.Id, row.Version));
            if (!kiosk && row.Id != 0 && (row.Id == UserSession.CurrentUser.Id || (edit && row.Role == "Employee")))
                form.Action("Змінити пароль", () => ShopForm.ChangePassword(form, row.Id, row.Id == UserSession.CurrentUser.Id), form.FooterActions);
            return form;
        }

        private void TerminalForm(AccountView terminal = null)
        {
            var form = new ShopForm(terminal == null ? "Новий термінал" : "Новий код термінала")
            {
                Owner = dialogOwner ?? Window.GetWindow(this)
            };
            if (terminal != null)
                form.Note("Кіоск № " + terminal.Id);
            form.Text("shop", "Номер магазину", terminal?.ShopCode ?? "1", readOnly: terminal != null);
            form.Rule("shop", () => InputRules.StoreNumber(form.Value("shop"), out _) ? null : "Введіть додатне ціле число.");
            form.Text("code", "Код доступу", ShopService.GenerateTerminalCode());
            form.GenerateValue("code", ShopService.GenerateTerminalCode);
            int created = 0;
            form.Rule("code", () => form.Value("code").Length < 8 ? "Код: щонайменше 8 символів." : null);
            form.Submit = async () =>
            {
                if (terminal == null)
                    created = await shop.SaveAccountAsync(new AccountView {
                        Username = "terminal-" + Guid.NewGuid().ToString("N"),
                        FullName = "Кіоск магазину № " + form.Value("shop"),
                        Phone = "",
                        ShopCode = form.Value("shop"),
                        Role = "Kiosk",
                        Status = "Active",
                        Tags = "Кіоск"
                    }, form.Value("code"));
                else
                    await shop.ChangePasswordAsync(terminal.Id, "", form.Value("code"));
            };
            if (form.ShowDialog() == true && created > 0)
                MessageBox.Show(dialogOwner ?? Window.GetWindow(this), "Номер для входу: " + created, "Кіоск створено");
        }

        private async Task OrderFormAsync(Rental row)
        {
            if (row == null)
            {
                await EditOrderAsync(null);
                return;
            }

            if (!row.EquipmentId.HasValue && (row.Status == "Pending" || row.Status == "Called" || row.Status == "Serving"))
            {
                await EditOrderAsync(row);
                return;
            }

            string next = RentalRules.NextStatus(row.Status);
            string action = next == "Ready" ? "Готово до видачі" : next == "Active" ? "Видати" : next == "Completed" ? "Підтвердити повернення" : "Прийняти оплату й передати на склад";
            var owner = dialogOwner ?? Window.GetWindow(this);
            var form = new ShopForm(row.OrderNumber ?? ("Замовлення № " + row.Id), action)
            {
                Owner = owner,
                Height = 510
            };
            form.Note((row.Equipment?.Name ?? "Спорядження") + " · " + (row.User?.FullName ?? "Гість")).FontSize = 21;
            form.Note(Rental.StatusLabel(row.Status) + " · " + row.RentDate + " — " + row.ReturnDate);
            if (next == null)
            {
                form.Note("Прокат: " + row.RentalPrice.ToString("0.##") + " грн");
                if (row.PenaltyFee > 0)
                    form.Note("Штраф: " + row.PenaltyFee.ToString("0.##") + " грн");
                form.ReadOnly();
                form.ShowDialog();
                return;
            }

            var staff = await new TeamService().ProfileAsync(UserSession.CurrentUser.Id);
            string station = next == "Ready" ? "Склад" : "Каса";
            bool allowed = access.CanEditCatalog || (staff.IsOnShift && StaffWork.Has(staff.Tags, station));
            bool cashier = access.CanEditCatalog || (staff.IsOnShift && StaffWork.Has(staff.Tags, "Каса"));
            bool cancel = false;
            if (RentalRules.CanCancel(row.Status) && cashier)
                form.Action("Скасувати замовлення", () =>
                {
                    cancel = true;
                    form.Close();
                });
            if (row.RequiresAdmin == 1)
            {
                form.Note("Повернення потребує перевірки менеджера. " + row.ConditionNotes);
                allowed = access.CanEditCatalog;
            }

            if (!allowed)
            {
                form.Note("Робоче місце: «" + (row.RequiresAdmin == 1 ? "Менеджер" : station) + "»");
                form.ReadOnly();
                form.ShowDialog();
                if (cancel)
                    await CancelOrderAsync(row);
                return;
            }

            bool edit = false, exception = false, review = false;
            if (next == "Assembling" || next == "Active" || next == "Completed")
            {
                var ledger = await shop.OrderLedgerAsync(row.Id);
                decimal paid = ShopService.Balance(ledger, "Оплата", "Повернення коштів") / 100m;
                decimal held = ShopService.Balance(ledger, "Застава", "Повернення застави") / 100m;
                decimal outstandingFee = (decimal)row.PenaltyFee - ShopService.Balance(ledger, "Штраф") / 100m;
                if (next == "Completed")
                {
                    form.Note("Повернути заставу: " + held.ToString("0.##") + " грн").FontSize = 22;
                    if (row.IsOverdue)
                        form.Note("Прострочено з " + row.ReturnDate);
                    if (outstandingFee > 0)
                        form.Note("Прийняти штраф окремо: " + outstandingFee.ToString("0.##") + " грн");
                    if (access.CanEditCatalog)
                        form.Action("Пошкодження або штраф", () =>
                        {
                            exception = true;
                            form.Close();
                        });
                    else
                        form.Action("Передати менеджеру", () =>
                        {
                            review = true;
                            form.Close();
                        });
                }
                else
                {
                    if (next == "Assembling")
                    {
                        form.Text("deposit", "Застава, грн", row.Deposit.ToString("0.##"));
                        form.Action("Змінити умови", () =>
                        {
                            edit = true;
                            form.Close();
                        });
                    }

                    var amount = form.Note("");
                    amount.FontSize = 22;
                    Action update = () =>
                    {
                        decimal required = (decimal)row.Deposit;
                        if (next == "Assembling" && !InputRules.TryMoney(form.Value("deposit"), out required))
                        {
                            amount.Text = "Перевірте суму застави";
                            return;
                        }

                        decimal due = (decimal)row.RentalPrice - paid + required - held;
                        amount.Text = due == 0 ? "Оплачено" : "До сплати: " + due.ToString("0.##") + " грн";
                    };
                    form.Note("Прокат: " + row.RentalPrice.ToString("0.##") + " грн");
                    if (next == "Assembling")
                        form.Changed("deposit", update);
                    update();
                }
            }

            form.Submit = () => shop.AdvanceOrderAsync(row.Id, row.Version, next, null, next == "Assembling" ? (double?)Amount(form.Value("deposit")) : null);
            form.ShowDialog();
            if (edit)
            {
                await EditOrderAsync(row);
                return;
            }

            if (cancel)
                await CancelOrderAsync(row);
            if (review)
            {
                var request = new ShopForm("Перевірка повернення", "Передати менеджеру")
                {
                    Owner = owner,
                    Height = 440
                };
                request.Text("message", "Пошкодження, втрата або інша причина", "", true);
                request.Submit = () => shop.FlagReturnAsync(row.Id, row.Version, request.Value("message"));
                request.ShowDialog();
            }

            if (exception)
            {
                var damage = new ShopForm("Повернення з перевіркою", "Прийняти повернення")
                {
                    Owner = owner,
                    Height = 450
                };
                damage.Choice("condition", "Стан", new[] { "Справне", "Пошкоджено", "Втрачено" });
                damage.Text("penalty", "Штраф окремим платежем, грн", row.PenaltyFee.ToString("0.##"));
                damage.Submit = () => shop.AdvanceOrderAsync(row.Id, row.Version, "Completed", Amount(damage.Value("penalty")), null, damage.Value("condition") == "Справне" ? null : damage.Value("condition"));
                damage.ShowDialog();
            }
        }

        private async Task CancelOrderAsync(Rental row)
        {
            var owner = dialogOwner ?? Window.GetWindow(this);
            if (MessageBox.Show(owner, "Скасувати " + row.OrderNumber + " і повернути внесені кошти?", "Скасування", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            await shop.AdvanceOrderAsync(row.Id, row.Version, "Cancelled");
        }

        private async Task EditOrderAsync(Rental row)
        {
            var edit = await CreateOrderEditorAsync(row);
            bool cancel = false;
            if (row != null && RentalRules.CanCancel(row.Status))
                edit.Action("Скасувати замовлення", () =>
                {
                    cancel = true;
                    edit.Close();
                });
            edit.ShowDialog();
            if (cancel)
                await CancelOrderAsync(row);
        }

        private async Task<ShopForm> CreateOrderEditorAsync(Rental row)
        {
            if (!access.CanReadOperations)
                throw new UnauthorizedAccessException("Дані замовлення заповнює персонал.");
            var clients = (await shop.AccountsAsync(true)).Where(x => x.Status == "Active").ToList();
            var equipment = await catalog.LoadInventoryAsync();
            if (equipment.Count == 0)
                throw new InvalidOperationException("Спочатку додайте спорядження до каталогу.");
            var edit = new ShopForm(row == null ? "Нове замовлення" : "Дані " + row.OrderNumber)
            {
                Owner = dialogOwner ?? Window.GetWindow(this)
            };
            edit.Choice("client", "Клієнт", clients.Select(x => x.Id + " · " + x.FullName), clients.Where(x => x.Id == row?.UserId).Select(x => x.Id + " · " + x.FullName).FirstOrDefault());
            edit.Action("Новий клієнт", () =>
            {
                AccountView client = RegisterClientForm();
                if (client == null)
                    return;
                clients.Add(client);
                edit.ChoiceOptions("client", clients.Select(x => x.Id + " · " + x.FullName), client.Id + " · " + client.FullName);
            });
            edit.Choice("equipment", "Спорядження", equipment.Select(x => x.Id + " · " + x.Name), equipment.Where(x => x.Id == row?.EquipmentId).Select(x => x.Id + " · " + x.Name).FirstOrDefault());
            edit.DateRange(row?.RentDate ?? DateTime.Today.ToString("yyyy-MM-dd"), row?.ReturnDate ?? DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"));
            edit.Text("deposit", "Застава, грн", (row?.Deposit ?? 0).ToString("0.00"));
            var preview = edit.Note("");
            Action updatePrice = () =>
            {
                DateTime start, end;
                if (!DateTime.TryParseExact(edit.Value("start"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out start) || !DateTime.TryParseExact(edit.Value("end"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out end) || end < start)
                {
                    preview.Text = "Вкажіть коректний період для розрахунку.";
                    return;
                }

                var item = equipment.Single(x => x.Id == int.Parse(edit.Value("equipment").Split(' ')[0]));
                int days = Math.Max(1, (end - start).Days);
                decimal price;
                try
                {
                    price = (decimal)RentalRules.Price(item.Record.PricePerDay, edit.Value("start"), edit.Value("end"));
                }
                catch (ArgumentException ex)
                {
                    preview.Text = ex.Message;
                    return;
                }

                preview.Text = days + " діб × " + item.Record.PricePerDay.ToString("0.00") + " грн = " + price.ToString("0.00") + " грн";
            };
            foreach (var key in new[] { "equipment", "start", "end" })
                edit.Changed(key, updatePrice);
            updatePrice();
            edit.Submit = () =>
            {
                if (string.IsNullOrWhiteSpace(edit.Value("client")))
                    throw new ArgumentException("Оберіть клієнта або зареєструйте нового.");
                return shop.SaveOrderAsync(new Rental {
                    Id = row?.Id ?? 0,
                    UserId = int.Parse(edit.Value("client").Split(' ')[0]),
                    EquipmentId = int.Parse(edit.Value("equipment").Split(' ')[0]),
                    QueueType = row?.QueueType ?? "WalkIn",
                    RentDate = edit.Value("start"),
                    ReturnDate = edit.Value("end"),
                    Deposit = Amount(edit.Value("deposit")),
                    PenaltyFee = row?.PenaltyFee ?? 0,
                    AdminNotes = row?.AdminNotes,
                    ConditionNotes = row?.ConditionNotes,
                    RequiresAdmin = row?.RequiresAdmin ?? 0
                }, row);
            };
            return edit;
        }

        private static double Amount(string value) => (double)InputRules.Money(value);
        private async Task TransactionFormAsync()
        {
            var clients = await shop.AccountsAsync(true);
            if (clients.Count == 0)
                throw new InvalidOperationException("Спочатку зареєструйте клієнта.");
            var form = new ShopForm("Нова демонстраційна транзакція", "Додати приклад")
            {
                Owner = dialogOwner ?? Window.GetWindow(this)
            };
            form.Choice("client", "Клієнт", clients.Select(x => x.Id + " · " + x.FullName));
            form.Choice("kind", "Тип", ShopService.TransactionKinds);
            form.Text("amount", "Сума, грн");
            form.Text("order", "Номер запису замовлення (необов’язково)");
            form.Text("parent", "ID початкового платежу (для повернення)");
            form.Text("notes", "Примітка", "", true);
            string key = Guid.NewGuid().ToString();
            form.Submit = () =>
            {
                decimal amount = InputRules.Money(form.Value("amount"), true);
                return shop.AddDemoTransactionAsync(new ShopTransaction {
                    ClientId = int.Parse(form.Value("client").Split(' ')[0]),
                    Kind = form.Value("kind"),
                    AmountMinor = (long)(amount * 100),
                    OrderId = OptionalId(form.Value("order")),
                    RelatedTransactionId = OptionalId(form.Value("parent")),
                    Notes = form.Value("notes"),
                    RequestKey = key
                });
            };
            form.ShowDialog();
            await RefreshAsync();
        }

        private static int? OptionalId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            int id;
            if (!int.TryParse(value, out id) || id <= 0)
                throw new ArgumentException("ID має бути додатним цілим числом.");
            return id;
        }

        private static string RoleLabel(string role) => role == "Admin" ? "Менеджер" : role == "Employee" ? "Працівник" : role == "Kiosk" ? "Кіоск" : role == "Client" ? "Клієнт" : "Невідома роль";
        private static DateTime? LocalDate(string value)
        {
            DateTime date;
            return DateTime.TryParse(value, out date) ? date.ToLocalTime() : (DateTime?)null;
        }

        private static string StateLabel(string state) => state == "Pending" ? "Очікує" : state == "Active" ? "Активний" : state == "Rejected" ? "Відхилено" : state == "Accepted" ? "Прийнято" : "Заблоковано";
    }
}
