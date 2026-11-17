using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SportRentApp
{
    public partial class MainWindow : Window
    {
        private readonly CatalogService service = new CatalogService();
        private List<CatalogItem> items = new List<CatalogItem>();
        private CatalogItem selected;
        private bool loading;
        private int navigationVersion;
        private int selectedSideTab = -1;
        private string activeSection;
        private FrameworkElement currentPanel;
        private int transitionVersion;
        private AccessPolicy access = new AccessPolicy(UserRole.Guest);
        private readonly System.Windows.Threading.DispatcherTimer visitorIdle = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        private readonly System.Windows.Threading.DispatcherTimer accessWatch = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        private bool checkingAccess;
        private KioskService.Visitor visitor;
        private readonly KioskService kiosk = new KioskService();
        private ShopForm kioskForm;
        private bool kioskSubmitting;
        public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register("CardWidth", typeof(double), typeof(MainWindow), new PropertyMetadata(245.0));
        public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }

        public MainWindow()
        {
            InitializeComponent();
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            ProfileAvatar.Content = TeamHub.Avatar(new PublicProfile { Name = UserSession.CurrentUser?.FullName });
            ApplyAccess(access);
            UpdateTabs(0);
            visitorIdle.Tick += (s, e) =>
            {
                if (!kioskSubmitting)
                    ResetVisitor();
            };
            PreviewMouseDown += (s, e) => KeepVisitorAlive();
            PreviewKeyDown += (s, e) => KeepVisitorAlive();
            accessWatch.Tick += async (s, e) => await RefreshAccessAsync();
            Activated += async (s, e) =>
            {
                if (IsLoaded)
                    await RefreshAccessAsync();
            };
            Closed += (s, e) =>
            {
                visitorIdle.Stop();
                accessWatch.Stop();
                navigationVersion++;
            };
        }

        private void ApplyAccess(AccessPolicy policy)
        {
            if (access.CanReadOperations && !policy.CanReadOperations)
            {
                items.Clear();
                ProductList.ItemsSource = null;
                CategoryBox.ItemsSource = null;
            }

            access = policy;
            if (policy.CanBrowse && UserSession.CurrentUser != null)
                UserSession.CurrentUser.Role = policy.Role.ToString();
            if (policy.Role != UserRole.Kiosk)
                visitor = null;
            if (policy.Role != UserRole.Kiosk)
                UserLabel.Text = UserSession.CurrentUser?.FullName ?? "Без доступу";
            else
                VisitorLabel.Text = visitor?.Name ?? ("Магазин № " + UserSession.CurrentUser?.ShopCode);
            VisitorLabel.Visibility = policy.Role == UserRole.Kiosk ? Visibility.Visible : Visibility.Collapsed;
            ProfileButton.Visibility = policy.Role == UserRole.Kiosk ? Visibility.Collapsed : Visibility.Visible;
            MyOrdersButton.Visibility = policy.Role == UserRole.Kiosk && visitor != null ? Visibility.Visible : Visibility.Collapsed;
            AddButton.Visibility = EditButton.Visibility = DeleteButton.Visibility = policy.CanEditCatalog ? Visibility.Visible : Visibility.Collapsed;
            RestoreButton.Visibility = policy.CanEditCatalog && selected != null && (selected.Record.Status == "Maintenance" || selected.Record.Status == "Lost") ? Visibility.Visible : Visibility.Collapsed;
            TabStrip.Visibility = InventoryTab.Visibility = RentalsTab.Visibility = policy.CanReadOperations ? Visibility.Visible : Visibility.Collapsed;
            Surface.ShowTabs = policy.CanReadOperations;
            AvatarButton.Visibility = policy.CanReadOperations ? Visibility.Visible : Visibility.Collapsed;
            VisitButton.Visibility = RentButton.Visibility = policy.Role == UserRole.Kiosk ? Visibility.Visible : Visibility.Collapsed;
            LogoutButton.Content = policy.Role == UserRole.Kiosk ? "Закрити кіоск" : "Вийти";
            LogoutButton.Visibility = policy.Role == UserRole.Kiosk && visitor != null ? Visibility.Collapsed : Visibility.Visible;
            ProfileButton.IsEnabled = policy.Role == UserRole.Admin || policy.Role == UserRole.Employee || policy.Role == UserRole.Client;
            ContentArea.Margin = new Thickness(46, 30, policy.CanReadOperations ? 86 : 46, 30);
            if (!policy.CanReadOperations)
            {
                WorkplacePanel.Content = null;
                WorkplaceNavigation.Content = null;
                selected = null;
                DetailPanel.DataContext = null;
                ShowPanel(CatalogPanel, 0);
            }
        }

        private void DenyAccess(Exception error)
        {
            items.Clear();
            ProductList.ItemsSource = null;
            selected = null;
            DetailPanel.DataContext = null;
            ApplyAccess(new AccessPolicy(UserRole.Guest));
            SetStatus(error.Message);
            RetryButton.Visibility = Visibility.Visible;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
            if (access.Role == UserRole.Kiosk)
                StartVisit();
            else if (access.CanReadOperations)
            {
                await RefreshProfileAsync();
                await OpenTeamAsync("Завдання");
            }
        }

        private void KeepVisitorAlive()
        {
            if (access.Role == UserRole.Kiosk && (visitor != null || kioskForm != null))
            {
                visitorIdle.Stop();
                visitorIdle.Start();
            }
        }

        private void ResetVisitor()
        {
            if (kioskSubmitting)
                return;
            var open = kioskForm;
            kioskForm = null;
            open?.Close();
            visitorIdle.Stop();
            visitor = null;
            MyOrdersButton.Visibility = Visibility.Collapsed;
            selected = null;
            DetailPanel.DataContext = null;
            SearchBox.Clear();
            CategoryBox.SelectedIndex = 0;
            LogoutButton.Visibility = Visibility.Visible;
            VisitorLabel.Text = "Магазин № " + UserSession.CurrentUser?.ShopCode;
            UserLabel.ToolTip = null;
            VisitButton.Content = "Увійти за номером";
            ShowPanel(CatalogPanel, 0);
        }

        private void Visit_Click(object sender, RoutedEventArgs e)
        {
            if (visitor != null)
                ResetVisitor();
            else
                StartVisit();
        }

        private async Task RefreshProfileAsync()
        {
            try
            {
                var profile = await new TeamService().ProfileAsync(UserSession.CurrentUser.Id);
                UserLabel.Text = profile.Name;
                UserSession.CurrentUser.FullName = profile.Name;
                ProfileAvatar.Content = TeamHub.Avatar(profile);
            }
            catch (Exception ex)
            {
                SetStatus(ShopForm.Friendly(ex));
            }
        }

        private async Task RefreshAccessAsync()
        {
            if (checkingAccess || loading || !IsLoaded || !IsVisible || !ContentArea.IsEnabled)
                return;
            var session = UserSession.CurrentUser;
            if (session == null)
                return;
            checkingAccess = true;
            try
            {
                var policy = await service.GetAccessAsync();
                if (!IsVisible || !ReferenceEquals(session, UserSession.CurrentUser) || policy.Role == access.Role)
                    return;
                navigationVersion++;
                pendingSection = null;
                WorkplacePanel.Content = null;
                WorkplaceNavigation.Content = null;
                selected = null;
                DetailPanel.DataContext = null;
                ApplyAccess(policy);
                ShowPanel(CatalogPanel, 0);
                await LoadAsync();
                await RefreshProfileAsync();
            }
            catch (UnauthorizedAccessException ex)
            {
                if (IsVisible && ReferenceEquals(session, UserSession.CurrentUser))
                {
                    accessWatch.Stop();
                    DenyAccess(ex);
                }
            }
            catch (Exception)
            {
                if (IsVisible)
                    RetryButton.Visibility = Visibility.Visible;
            }
            finally
            {
                checkingAccess = false;
            }
        }

        private async void Profile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await TeamHub.ShowProfileAsync(this, UserSession.CurrentUser.Id);
                await RefreshProfileAsync();
            }
            catch (Exception ex)
            {
                SetStatus(ShopForm.Friendly(ex));
            }
        }

        private void AvatarMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                FontFamily = FontFamily,
                FontSize = 16,
                Background = new SolidColorBrush(Color.FromRgb(80, 60, 59)),
                Foreground = Brushes.Gainsboro
            };
            Action<string, Func<Task>> add = (label, action) =>
            {
                var item = new MenuItem
                {
                    Header = label
                };
                item.Click += async (s, args) =>
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception ex)
                    {
                        SetStatus(ShopForm.Friendly(ex));
                    }
                };
                menu.Items.Add(item);
            };
            add("Мій профіль", async () =>
            {
                await TeamHub.ShowProfileAsync(this, UserSession.CurrentUser.Id);
                await RefreshProfileAsync();
            });
            add("Змінити пароль", () =>
            {
                ShopForm.ChangePassword(this, UserSession.CurrentUser.Id, true);
                return Task.CompletedTask;
            });
            if (access.CanReadOperations)
            {
                add("Завдання", () => OpenTeamAsync("Завдання"));
                add("Повідомлення", () => OpenTeamAsync("Повідомлення"));
                add("Завершити зміну", async () =>
                {
                    var summary = await new TeamService().EndShiftAsync();
                    SetStatus(summary);
                    await OpenTeamAsync("Завдання", null, true);
                });
            }

            menu.PlacementTarget = AvatarButton;
            menu.IsOpen = true;
        }

        private async void Tasks_Click(object sender, RoutedEventArgs e)
        {
            await OpenTeamAsync("Завдання");
        }

        private async void Messages_Click(object sender, RoutedEventArgs e)
        {
            await OpenTeamAsync("Повідомлення");
        }

        public async Task OpenTeamAsync(string section, int? peer = null, bool refresh = false)
        {
            string key = "hub:" + section + ":" + peer;
            if (!refresh && currentPanel == WorkplacePanel && activeSection == key)
                return;
            int version = ++navigationVersion;
            pendingSection = null;
            try
            {
                var policy = await service.GetAccessAsync();
                if (version != navigationVersion)
                    return;
                ApplyAccess(policy);
                if (!policy.CanReadOperations)
                    return;
                WorkplacePanel.Content = new TeamHub(this, section, peer);
                WorkplaceNavigation.Content = null;
                activeSection = key;
                if (currentPanel == WorkplacePanel)
                    currentPanel = null;
                ShowPanel(WorkplacePanel, section == "Завдання" ? 4 : 5);
            }
            catch (Exception ex)
            {
                SetStatus(ShopForm.Friendly(ex));
            }
        }

        private void StartVisit()
        {
            if (access.Role != UserRole.Kiosk || kioskSubmitting)
                return;
            ResetVisitor();
            var phoneForm = CreateVisitForm();
            if (!ShowKioskForm(phoneForm))
                return;
            if (visitor == null)
            {
                string phone = phoneForm.Value("phone");
                var register = new ShopForm("Зареєструватися", "Зареєструватися")
                {
                    Owner = this,
                    Height = 350,
                    MinHeight = 320
                };
                register.Note("Новий номер " + phone);
                register.Text("name", "Як вас звати?");
                register.Submit = async () =>
                {
                    kioskSubmitting = true;
                    try
                    {
                        visitor = await kiosk.RegisterAsync(phone, register.Value("name"));
                    }
                    finally
                    {
                        kioskSubmitting = false;
                    }
                };
                if (!ShowKioskForm(register))
                    return;
            }

            VisitorLabel.Text = visitor.Name;
            VisitButton.Content = "Вийти";
            LogoutButton.Visibility = Visibility.Collapsed;
            MyOrdersButton.Visibility = Visibility.Visible;
            KeepVisitorAlive();
        }

        private ShopForm CreateVisitForm()
        {
            var form = new ShopForm("Вхід за номером телефону", "Продовжити")
            {
                Owner = this,
                Height = 310,
                MinHeight = 280
            };
            form.Text("phone", "Номер телефону");
            form.Submit = async () =>
            {
                kioskSubmitting = true;
                try
                {
                    visitor = await kiosk.IdentifyAsync(form.Value("phone"));
                }
                finally
                {
                    kioskSubmitting = false;
                }
            };
            return form;
        }

        private async void MyOrders_Click(object sender, RoutedEventArgs e)
        {
            var current = visitor;
            if (current == null || kioskForm != null)
                return;
            try
            {
                var orders = await kiosk.MyOrdersAsync(current);
                if (visitor != current || kioskForm != null || !IsVisible)
                    return;
                var form = new ShopForm("Мої замовлення")
                {
                    Owner = this,
                    Height = 560
                };
                form.ReadOnly();
                if (orders.Count == 0)
                    form.Note("Замовлень ще немає.");
                int? returnId = null;
                foreach (var order in orders)
                {
                    form.Note(order.Label).FontSize = 20;
                    form.Note(order.Summary);
                    if (order.ReturnRequested)
                        form.Note("Звернення про повернення передано на касу.");
                    else if (order.Status == "Active")
                        form.Action("Повідомити про повернення", () =>
                        {
                            returnId = order.Id;
                            form.Close();
                        });
                }

                ShowKioskForm(form);
                if (returnId.HasValue && visitor == current)
                {
                    kioskSubmitting = true;
                    try
                    {
                        await kiosk.RequestReturnAsync(current, returnId.Value);
                    }
                    finally
                    {
                        kioskSubmitting = false;
                    }

                    if (visitor != current || !IsVisible)
                        return;
                    var receipt = new ShopForm("Звернення прийнято")
                    {
                        Owner = this,
                        Height = 220,
                        MinHeight = 200
                    };
                    receipt.ReadOnly();
                    ShowKioskForm(receipt);
                }
            }
            catch (Exception ex)
            {
                if (visitor == current)
                    SetStatus(ShopForm.Friendly(ex));
            }
        }

        private bool ShowKioskForm(ShopForm form)
        {
            kioskForm = form;
            KeepVisitorAlive();
            try
            {
                return form.ShowDialog() == true;
            }
            finally
            {
                if (kioskForm == form)
                    kioskForm = null;
                visitorIdle.Stop();
                KeepVisitorAlive();
            }
        }

        private ShopForm CreateKioskOrderForm(CatalogItem item)
        {
            var form = new ShopForm("Оформити оренду", "Підтвердити")
            {
                Owner = this,
                Height = 540
            };
            form.Note(item.Name).FontSize = 22;
            DateTime start = item.AvailableFrom ?? DateTime.Today;
            form.DateRange(start.ToString("yyyy-MM-dd"), start.AddDays(1).ToString("yyyy-MM-dd"), DateTime.Today);
            form.Note(item.AvailabilityLabel);
            var total = form.Note("");
            total.FontSize = 21;
            Action update = () =>
            {
                try
                {
                    total.Text = KioskService.Price(item.Record.PricePerDay, form.Value("start"), form.Value("end")).ToString("0.##") + " грн";
                }
                catch (ArgumentException)
                {
                    total.Text = "Перевірте дати оренди";
                }
            };
            form.Changed("start", update);
            form.Changed("end", update);
            update();
            form.Rule("start", () =>
            {
                try
                {
                    KioskService.Price(item.Record.PricePerDay, form.Value("start"), form.Value("end"));
                    return null;
                }
                catch (ArgumentException ex)
                {
                    return ex.Message;
                }
            });
            var current = visitor;
            string key = Guid.NewGuid().ToString();
            form.Submit = async () =>
            {
                if (current == null || current != visitor)
                    throw new InvalidOperationException("Візит завершено. Введіть номер телефону ще раз.");
                kioskSubmitting = true;
                try
                {
                    var ticket = await kiosk.OrderAsync(current, key, item.Id, form.Value("start"), form.Value("end"), item.Record.PricePerDay);
                    form.Tag = ticket;
                }
                finally
                {
                    kioskSubmitting = false;
                }
            };
            return form;
        }

        private async void Rent_Click(object sender, RoutedEventArgs e)
        {
            var item = selected;
            if (access.Role != UserRole.Kiosk || item == null || kioskSubmitting)
                return;
            if (visitor == null)
                StartVisit();
            if (visitor == null)
                return;
            var form = CreateKioskOrderForm(item);
            if (!ShowKioskForm(form))
            {
                await LoadAsync();
                return;
            }

            var ticket = (KioskTicket)form.Tag;
            var receipt = new ShopForm("Замовлення оформлено")
            {
                Owner = this,
                Height = 360,
                MinHeight = 300
            };
            receipt.ReadOnly();
            receipt.Note(ticket.OrderNumber).FontSize = 30;
            receipt.Note("Вартість прокату: " + ticket.RentalPrice.ToString("0.##") + " грн");
            ShowKioskForm(receipt);
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (loading)
                return;
            loading = true;
            RetryButton.Visibility = Visibility.Collapsed;
            try
            {
                var session = UserSession.CurrentUser;
                var policy = await service.GetAccessAsync();
                if (!ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                ApplyAccess(policy);
                var loaded = await service.LoadAsync();
                if (!ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                items = loaded;
                string category = CategoryBox.SelectedItem as string;
                CategoryBox.ItemsSource = new[] { "Усі категорії" }.Concat(items.Select(x => x.Category).Distinct().OrderBy(x => x)).ToList();
                CategoryBox.SelectedItem = category != null && CategoryBox.Items.Contains(category) ? category : "Усі категорії";
                Filter();
                accessWatch.Start();
            }
            catch (UnauthorizedAccessException ex)
            {
                DenyAccess(ex);
            }
            catch (Exception)
            {
                SetStatus("Не вдалося завантажити каталог. Перевірте доступ до бази даних.");
                RetryButton.Visibility = Visibility.Visible;
            }
            finally
            {
                loading = false;
            }
        }

        private void Filter()
        {
            if (ProductList == null || CategoryBox == null || SearchBox == null)
                return;
            string query = SearchBox.Text.Trim(), category = CategoryBox.SelectedItem as string;
            SearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var filtered = items.Where(x => (string.IsNullOrEmpty(category) || category == "Усі категорії" || x.Category == category) && (x.Name ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            CatalogCount.Text = filtered.Count + " знайдено";
            ProductList.ItemsSource = filtered;
            EmptyLabel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetStatus(string message)
        {
            MessageBox.Show(this, message, "Sport Drive", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            Filter();
        }

        private void Category_Changed(object sender, SelectionChangedEventArgs e)
        {
            Filter();
        }

        private void CatalogScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CatalogScroll.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, CatalogScroll.ActualWidth), Math.Max(0, CatalogScroll.ActualHeight)), 12, 12);
            double available = Math.Max(1, CatalogScroll.ActualWidth - 28);
            int columns = Math.Max(1, (int)(available / 250));
            CardWidth = Math.Max(1, Math.Floor(available / columns) - 16);
        }

        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
                return;
            double scale = Math.Min(1, Math.Min(e.NewSize.Width / 800, e.NewSize.Height / 620));
            LayoutRoot.Width = e.NewSize.Width / scale;
            LayoutRoot.Height = e.NewSize.Height / scale;
        }

        private void ShowPanel(FrameworkElement panel, int tab)
        {
            if (currentPanel == panel && Surface.SelectedTab == tab)
                return;
            var outgoing = currentPanel;
            int transition = ++transitionVersion;
            CatalogPanel.Visibility = DetailPanel.Visibility = WorkplacePanel.Visibility = Visibility.Collapsed;
            panel.Visibility = Visibility.Visible;
            Surface.SelectedTab = tab;
            currentPanel = panel;
            WorkplaceNavigation.Visibility = panel == WorkplacePanel ? Visibility.Visible : Visibility.Collapsed;
            AddButton.Visibility = access.CanEditCatalog && panel == CatalogPanel ? Visibility.Visible : Visibility.Collapsed;
            UpdateTabs(tab);
            if (IsLoaded && outgoing != panel)
            {
                if (outgoing != null)
                {
                    outgoing.Visibility = Visibility.Visible;
                    outgoing.IsHitTestVisible = false;
                    var exit = new TranslateTransform();
                    outgoing.RenderTransform = exit;
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
                    fade.Completed += (s, e) =>
                    {
                        if (transition == transitionVersion && currentPanel != outgoing)
                            outgoing.Visibility = Visibility.Collapsed;
                    };
                    outgoing.BeginAnimation(OpacityProperty, fade);
                    exit.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, panel == CatalogPanel ? 50 : -50, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                }

                panel.IsHitTestVisible = true;
                var movement = new TranslateTransform();
                panel.RenderTransform = movement;
                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
                movement.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(panel == CatalogPanel ? -50 : 50, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        private void UpdateTabs(int selectedTab)
        {
            if (IsLoaded && selectedSideTab == selectedTab)
                return;
            var tabs = new[] { CatalogTab, RentalsTab, TeamTab, InventoryTab, TasksTab, MessagesTab };
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                if (IsLoaded && i != selectedTab && i != selectedSideTab)
                    continue;
                var shift = tab.RenderTransform as TranslateTransform;
                if (shift == null)
                {
                    shift = new TranslateTransform();
                    tab.RenderTransform = shift;
                }

                double x = i == selectedTab ? 0 : 6;
                double opacity = i == selectedTab ? 1 : .5;
                if (!IsLoaded)
                {
                    shift.X = x;
                    tab.Opacity = opacity;
                    continue;
                }

                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                tab.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(240)));
            }

            selectedSideTab = selectedTab;
        }

        private void OpenProduct(CatalogItem item)
        {
            if (item == null)
                return;
            selected = item;
            DetailPanel.DataContext = item;
            RentButton.IsEnabled = item.CanReserve;
            RestoreButton.Visibility = access.CanEditCatalog && (item.Record.Status == "Maintenance" || item.Record.Status == "Lost") ? Visibility.Visible : Visibility.Collapsed;
            ShowPanel(DetailPanel, 0);
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (selected == null || !access.CanEditCatalog)
                return;
            if (MessageBox.Show(this, "Спорядження перевірене та готове до наступної оренди?", "Відновлення доступності", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                await service.RestoreAsync(selected.Record);
                await LoadAsync();
                ShowPanel(CatalogPanel, 0);
            }
            catch (Exception ex)
            {
                SetStatus(ShopForm.Friendly(ex));
            }
        }

        private void Product_Click(object sender, RoutedEventArgs e)
        {
            OpenProduct(((Button)sender).Tag as CatalogItem);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(CatalogPanel, 0);
        }

        private async void Catalog_Click(object sender, RoutedEventArgs e)
        {
            if (currentPanel == CatalogPanel)
            {
                navigationVersion++;
                pendingSection = null;
                return;
            }

            navigationVersion++;
            pendingSection = null;
            ShowPanel(CatalogPanel, 0);
            await LoadAsync();
        }

        private async void Team_Click(object sender, RoutedEventArgs e)
        {
            await OpenWorkplaceAsync("Команда");
        }

        private async void Inventory_Click(object sender, RoutedEventArgs e)
        {
            await OpenWorkplaceAsync("База даних");
        }

        private async void Rentals_Click(object sender, RoutedEventArgs e)
        {
            await OpenWorkplaceAsync("Клієнти");
        }

        private string pendingSection;
        private async Task OpenWorkplaceAsync(string section)
        {
            int tab = section == "Клієнти" ? 1 : section == "Команда" ? 2 : 3;
            if (pendingSection == section)
                return;
            if (currentPanel == WorkplacePanel && activeSection == section)
            {
                navigationVersion++;
                pendingSection = null;
                return;
            }

            pendingSection = section;
            int version = ++navigationVersion;
            var session = UserSession.CurrentUser;
            try
            {
                var policy = await service.GetAccessAsync();
                if (version != navigationVersion || !ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                ApplyAccess(policy);
                if (!policy.CanReadOperations)
                    return;
                var view = new WorkplaceView(section);
                var page = await view.EmbedAsync(this);
                if (version != navigationVersion || !ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                WorkplacePanel.Content = page;
                WorkplaceNavigation.Content = view.Navigation;
                activeSection = section;
                if (currentPanel == WorkplacePanel)
                    currentPanel = null;
                ShowPanel(WorkplacePanel, section == "Клієнти" ? 1 : section == "Команда" ? 2 : 3);
            }
            catch (UnauthorizedAccessException ex)
            {
                if (version == navigationVersion && ReferenceEquals(session, UserSession.CurrentUser))
                    DenyAccess(ex);
            }
            catch (Exception)
            {
                if (version == navigationVersion && ReferenceEquals(session, UserSession.CurrentUser))
                    SetStatus("Не вдалося відкрити робочий простір. Спробуйте ще раз.");
            }
            finally
            {
                if (version == navigationVersion)
                    pendingSection = null;
            }
        }

        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            await EditAsync(null);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var item = selected;
            if (item == null || !access.CanEditCatalog)
                return;
            if (MessageBox.Show(this, "Видалити «" + item.Name + "» з каталогу? Історія оренд збережеться, заявки на зміну товару закриються.", "Видалити товар", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            DeleteButton.IsEnabled = false;
            try
            {
                await service.DeleteAsync(item.Record);
                selected = null;
                DetailPanel.DataContext = null;
                ShowPanel(CatalogPanel, 0);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                SetStatus(ShopForm.Friendly(ex));
            }
            finally
            {
                DeleteButton.IsEnabled = true;
            }
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (selected != null)
                await EditAsync(selected.Record);
        }

        private async Task EditAsync(Equipment record)
        {
            if (!access.CanEditCatalog)
                return;
            try
            {
                var policy = await service.GetAccessAsync();
                ApplyAccess(policy);
                if (!policy.CanEditCatalog)
                {
                    SetStatus("Редагування доступне лише менеджеру.");
                    return;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                DenyAccess(ex);
                return;
            }
            catch (Exception)
            {
                SetStatus("Не вдалося перевірити права. Спробуйте ще раз.");
                return;
            }

            var editor = new EquipmentEditor(record)
            {
                Owner = this
            };
            if (editor.ShowDialog() == true)
            {
                await LoadAsync();
                OpenProduct(items.FirstOrDefault(x => x.Id == editor.SavedId));
            }
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            UserSession.CurrentUser = null;
            var auth = Application.Current.Windows.OfType<AuthWindow>().FirstOrDefault();
            if (auth == null)
                auth = new AuthWindow();
            auth.Show();
            Application.Current.MainWindow = auth;
            Close();
        }
    }
}
