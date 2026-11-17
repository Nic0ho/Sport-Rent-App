using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SportRentApp
{
    public sealed class TeamHub : UserControl
    {
        private readonly TeamService service = new TeamService();
        private readonly Window owner;
        private readonly User session;
        private readonly ContentControl body = new ContentControl();
        private readonly DispatcherTimer timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        private List<PublicProfile> staff = new List<PublicProfile>();
        private List<WorkTask> tasks = new List<WorkTask>();
        private readonly WrapPanel cards = new WrapPanel();
        private ScrollViewer taskScroll;
        private readonly Popup profilePopup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            VerticalOffset = 5
        };
        private readonly DispatcherTimer popupClose = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        private bool busy, manager, allTasks;
        private string section;
        private int? peer;
        private TextBox search, draft;
        private StackPanel contacts, messages;
        private TextBlock conversationTitle;
        private string conversationStamp;
        private string staffStamp, contactsStamp;
        private int Actor => session.Id;

        public TeamHub(Window owner, string section, int? peer = null)
        {
            this.owner = owner;
            this.section = section;
            this.peer = peer;
            session = UserSession.CurrentUser;
            FontFamily = new FontFamily("pack://application:,,,/SportRentApp;component/Resources/Nunito/#Nunito");
            FontSize = 16;
            Foreground = Brushes.Gainsboro;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SportRentApp;component/Styles/CatalogStyles.xaml", UriKind.Relative) });
            Content = body;
            Loaded += async (s, e) =>
            {
                owner.Deactivated += CloseProfilePopup;
                await Guard(RefreshAsync);
                timer.Start();
            };
            Unloaded += (s, e) =>
            {
                timer.Stop();
                popupClose.Stop();
                profilePopup.IsOpen = false;
                owner.Deactivated -= CloseProfilePopup;
            };
            popupClose.Tick += (s, e) =>
            {
                popupClose.Stop();
                if (profilePopup.PlacementTarget?.IsMouseOver != true && profilePopup.Child?.IsMouseOver != true)
                    profilePopup.IsOpen = false;
            };
            timer.Tick += async (s, e) =>
            {
                if (IsVisible && owner.IsActive && IsEnabled)
                    await Guard(RefreshAsync);
            };
            SizeChanged += (s, e) =>
            {
                foreach (FrameworkElement card in cards.Children)
                    card.Width = CardWidth();
            };
        }

        private async Task Guard(Func<Task> action)
        {
            if (busy)
                return;
            busy = true;
            try
            {
                await action();
                if (IsLoaded && ReferenceEquals(session, UserSession.CurrentUser))
                    timer.Start();
            }
            catch (Exception ex)
            {
                timer.Stop();
                if (owner.IsVisible && ReferenceEquals(session, UserSession.CurrentUser))
                    MessageBox.Show(owner, ShopForm.Friendly(ex), "Sport Drive", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                busy = false;
            }
        }

        private Button Button(string text, Func<Task> action, bool primary = false)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)FindResource(primary ? "CatalogPrimary" : "CatalogButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 7, 12, 7)
            };
            button.Click += async (s, e) => await Guard(action);
            return button;
        }

        private static TextBlock Text(string value, double size = 16) => new TextBlock
        {
            Text = value,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        private static ScrollViewer Scroll(UIElement content) => new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        private async Task RefreshAsync()
        {
            manager = (await new CatalogService().GetAccessAsync()).CanEditCatalog;
            if (!ReferenceEquals(session, UserSession.CurrentUser))
                return;
            staff = await service.StaffAsync();
            if (!ReferenceEquals(session, UserSession.CurrentUser))
                return;
            string snapshot = string.Join("|", staff.Select(p => p.Id + ":" + p.Name + ":" + p.PhotoPath + ":" + p.Role + ":" + p.Tags + ":" + p.IsOnShift));
            bool staffChanged = snapshot != staffStamp;
            staffStamp = snapshot;
            if (section == "Завдання")
            {
                var loaded = await service.TasksAsync();
                if (!ReferenceEquals(session, UserSession.CurrentUser))
                    return;
                string before = TaskStamp(tasks), after = TaskStamp(loaded);
                tasks = loaded;
                if (body.Content == null || staffChanged || before != after)
                    DrawTasks();
            }
            else
            {
                if (body.Content == null)
                    BuildMessenger();
                DrawContacts();
                await DrawConversationAsync();
            }
        }

        private static string TaskStamp(IEnumerable<WorkTask> rows) => string.Join("|", rows.Select(t => t.Id + ":" + t.Title + ":" + t.Description + ":" + t.Station + ":" + string.Join(",", t.Assignments.Select(a => a.Id + "/" + a.Status + "/" + a.IsMentioned))));
        private double CardWidth()
        {
            double width = Math.Max(300, ActualWidth);
            return Math.Max(280, width / Math.Max(1, (int)(width / 340)) - 16);
        }

        private void DrawTasks()
        {
            profilePopup.IsOpen = false;
            var root = new DockPanel();
            var toolbar = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            var mine = staff.SingleOrDefault(x => x.Id == Actor);
            if (mine != null)
                toolbar.Children.Add(WorkTags(this, owner, mine, StaffWork.Places, async updated => await Guard(RefreshAsync)));
            toolbar.Children.Add(Button(allTasks ? "Показати мої" : "Показати всі", () =>
            {
                allTasks = !allTasks;
                DrawTasks();
                return Task.CompletedTask;
            }));
            toolbar.Children.Add(Button("Створити завдання", async () =>
            {
                CreateTaskForm().ShowDialog();
                await RefreshAsync();
            }));
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            if (taskScroll != null)
                taskScroll.Content = null;
            cards.Children.Clear();
            foreach (var task in tasks.Where(t => allTasks || t.IsMine(Actor) || (manager && (t.Kind == "Rental" || t.Station == "Менеджер" || t.Assignments.Any(a => a.Status == "Pending")))))
            {
                var content = new StackPanel();
                content.Children.Add(Text(task.Title, 21));
                if (!string.IsNullOrEmpty(task.Description))
                    content.Children.Add(Text(task.Description));
                if (task.Kind == "Rental")
                {
                    var state = task.SourceKey.Split(':').Last();
                    bool allowed = manager || (mine != null && mine.IsOnShift && StaffWork.Has(mine.Tags, task.Station));
                    string caption = state == "Assembling" ? "Підтвердити готовність" : state == "Ready" ? "Видати клієнту" : state == "Active" ? "Прийняти повернення" : "Оформити оплату";
                    if (allowed)
                        content.Children.Add(Button(caption, async () =>
                        {
                            if (state == "Assembling")
                            {
                                var rental = (await new CatalogService().LoadRentalsAsync(task.EntityId)).SingleOrDefault();
                                if (rental == null || rental.Status != state)
                                {
                                    await RefreshAsync();
                                    return;
                                }

                                await new ShopService().AdvanceOrderAsync(rental.Id, rental.Version, "Ready");
                            }
                            else
                            {
                                await new WorkplaceView().OpenTaskAsync(owner, task);
                            }

                            await RefreshAsync();
                        }, true));
                    else
                        content.Children.Add(Text("Виконує " + task.Station.ToLower()));
                    cards.Children.Add(new Border {
                        Background = new SolidColorBrush(Color.FromRgb(80, 60, 59)),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(18),
                        Margin = new Thickness(0, 0, 16, 16),
                        Width = CardWidth(),
                        Child = content
                    });
                    continue;
                }

                content.Children.Add(Text(task.Kind == "Personal" ? "Особисте завдання" : task.Station));
                content.Children.Add(AvatarStack(task.Assignments.Where(a => a.Status == "Assigned").Select(a => staff.SingleOrDefault(p => p.Id == a.UserId)).Where(p => p != null).ToList()));
                var mentioned = task.Assignments.Where(a => a.IsMentioned).Select(a => staff.SingleOrDefault(p => p.Id == a.UserId)).Where(p => p != null).ToList();
                if (mentioned.Count > 0)
                {
                    content.Children.Add(Text("Відмічено"));
                    content.Children.Add(AvatarStack(mentioned));
                }

                foreach (var pending in task.Assignments.Where(a => a.Status == "Pending"))
                {
                    content.Children.Add(Text("Передача → " + (staff.SingleOrDefault(x => x.Id == pending.UserId)?.Name ?? "Працівник")));
                    if (manager || pending.UserId == Actor)
                    {
                        var decisions = new WrapPanel();
                        decisions.Children.Add(Button("Прийняти", async () =>
                        {
                            await service.DecideAssignmentAsync(pending.Id, true);
                            await RefreshAsync();
                        }, true));
                        decisions.Children.Add(Button("Відхилити", async () =>
                        {
                            await service.DecideAssignmentAsync(pending.Id, false);
                            await RefreshAsync();
                        }));
                        content.Children.Add(decisions);
                    }
                }

                var actions = new WrapPanel();
                if (task.IsManual && (manager || task.CreatedBy == Actor))
                    actions.Children.Add(Button("Редагувати", async () =>
                    {
                        CreateTaskForm(task).ShowDialog();
                        await RefreshAsync();
                    }));
                if (task.Kind != "Appeal" && task.Kind != "Personal" && (task.Station != "Менеджер" || manager))
                    actions.Children.Add(Button("Відкрити", async () =>
                    {
                        await new WorkplaceView().OpenTaskAsync(owner, task);
                        await RefreshAsync();
                    }, true));
                if (manager || task.Assignments.Any(a => a.UserId == Actor && a.Status == "Assigned"))
                    actions.Children.Add(Button("Передати", async () =>
                    {
                        await ReassignAsync(task);
                        await RefreshAsync();
                    }));
                if (task.IsManual && (manager || (task.Kind == "Personal" && (task.CreatedBy == Actor || task.Assignments.Any(a => a.UserId == Actor && a.Status == "Assigned")))))
                    actions.Children.Add(Button("Завершити", async () =>
                    {
                        await service.CloseRequestAsync(task.Id);
                        await RefreshAsync();
                    }));
                content.Children.Add(actions);
                cards.Children.Add(new Border {
                    Background = new SolidColorBrush(Color.FromRgb(80, 60, 59)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(18),
                    Margin = new Thickness(0, 0, 16, 16),
                    Width = CardWidth(),
                    Child = content
                });
            }

            if (cards.Children.Count == 0)
                cards.Children.Add(Text("Поки немає завдань", 21));
            taskScroll = Scroll(cards);
            root.Children.Add(taskScroll);
            body.Content = root;
        }

        private void CloseProfilePopup(object sender, EventArgs e)
        {
            profilePopup.IsOpen = false;
            popupClose.Stop();
        }

        private FrameworkElement AvatarStack(List<PublicProfile> people)
        {
            var canvas = new Canvas
            {
                Name = "AssigneeStack",
                HorizontalAlignment = HorizontalAlignment.Left,
                Height = 44,
                Width = Math.Max(40, people.Count * 48 - 8),
                Margin = new Thickness(0, 2, 0, 12)
            };
            bool expanded = false;
            foreach (var person in people)
            {
                int index = canvas.Children.Count;
                var avatar = new Button
                {
                    Content = Avatar(person, 40),
                    Style = (Style)FindResource("CatalogButton"),
                    Padding = new Thickness(0),
                    Width = 40,
                    Height = 40,
                    Background = Brushes.Transparent,
                    RenderTransform = new TranslateTransform(index * 24, 0)
                };
                System.Windows.Automation.AutomationProperties.SetName(avatar, person.Name);
                canvas.Children.Add(avatar);
                avatar.Click += (s, e) =>
                {
                    expanded = !expanded;
                    profilePopup.IsOpen = false;
                    for (int i = 0; i < canvas.Children.Count; i++)
                    {
                        var shift = (TranslateTransform)((FrameworkElement)canvas.Children[i]).RenderTransform;
                        double from = shift.X, to = i * (expanded ? 48 : 24);
                        shift.BeginAnimation(TranslateTransform.XProperty, null);
                        shift.X = to;
                        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    }
                };
                avatar.MouseEnter += (s, e) => ShowProfilePopup(avatar, person);
                avatar.MouseLeave += (s, e) =>
                {
                    popupClose.Stop();
                    popupClose.Start();
                };
                avatar.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Down)
                    {
                        ShowProfilePopup(avatar, person);
                        ((FrameworkElement)profilePopup.Child).MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                        e.Handled = true;
                    }
                };
            }

            return new ScrollViewer
            {
                Content = canvas,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private void ShowProfilePopup(Button anchor, PublicProfile person)
        {
            popupClose.Stop();
            var panel = new StackPanel();
            panel.Children.Add(Text(person.Name, 20));
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            var write = Button("Написати", async () =>
            {
                profilePopup.IsOpen = false;
                await OpenConversationAsync(person.Id);
            });
            write.IsEnabled = person.Id != Actor;
            actions.Children.Add(write);
            actions.Children.Add(Button("Профіль", async () =>
            {
                profilePopup.IsOpen = false;
                await ShowProfileAsync(owner, person.Id);
            }));
            panel.Children.Add(actions);
            var card = new Border
            {
                Child = panel,
                Background = new SolidColorBrush(Color.FromRgb(70, 53, 52)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                MaxWidth = 340
            };
            TextBlock.SetFontFamily(card, FontFamily);
            TextBlock.SetForeground(card, Foreground);
            card.MouseEnter += (s, e) => popupClose.Stop();
            card.MouseLeave += (s, e) =>
            {
                popupClose.Stop();
                popupClose.Start();
            };
            profilePopup.PlacementTarget = anchor;
            profilePopup.Child = card;
            profilePopup.IsOpen = true;
        }

        private ShopForm CreateTaskForm(WorkTask task = null)
        {
            var form = new ShopForm(task == null ? "Нове завдання" : "Редагувати завдання")
            {
                Owner = owner,
                Height = 680
            };
            var kinds = new Dictionary<string, string>
            {
                { "Особисте завдання", "Personal" },
                { "Звернення менеджеру", "Appeal" },
                { "Налаштування кіоску", "Kiosk" }
            };
            if (task == null)
                form.Choice("kind", "Тип", kinds.Keys);
            form.Text("title", "Назва", task?.Title);
            form.Rule("title", () => string.IsNullOrWhiteSpace(form.Value("title")) || form.Value("title").Trim().Length > 120 ? "Вкажіть назву: до 120 символів." : null);
            form.Text("message", "Опис", task?.Description, true);
            form.Note("Відмітити працівників");
            var people = new WrapPanel();
            foreach (var person in staff.Where(p => p.Id != (task?.CreatedBy ?? Actor)))
                people.Children.Add(new CheckBox {
                    Content = person.Name,
                    Tag = person.Id,
                    IsChecked = task?.Assignments.Any(a => a.UserId == person.Id && a.IsMentioned) == true,
                    Style = (Style)FindResource("CatalogTag")
                });
            form.ContentItem(people);
            form.Submit = () => service.SaveTaskAsync(task?.Id, task?.Kind ?? kinds[form.Value("kind")], form.Value("title"), form.Value("message"), people.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (int)c.Tag).ToArray());
            return form;
        }

        private async Task ReassignAsync(WorkTask task)
        {
            var candidates = await service.TransferCandidatesAsync(task.Id);
            if (candidates.Count == 0)
                throw new InvalidOperationException("Немає інших працівників на цьому робочому місці.");
            var names = candidates.ToDictionary(p => p.Name + " · #" + p.Id, p => p.Id);
            var form = new ShopForm("Передати завдання", manager ? "Призначити" : "Запросити підтвердження")
            {
                Owner = owner,
                Height = 340
            };
            form.Choice("worker", "Працівник", names.Keys);
            form.Submit = () => service.AssignAsync(task.Id, names[form.Value("worker")]);
            form.ShowDialog();
        }

        private async Task OpenConversationAsync(int id)
        {
            if (owner is MainWindow main)
                await main.OpenTeamAsync("Повідомлення", id);
        }

        private void BuildMessenger()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel
            {
                Margin = new Thickness(0, 0, 18, 0)
            };
            search = new TextBox
            {
                Style = (Style)FindResource("CatalogInput"),
                Margin = new Thickness(0, 0, 0, 12)
            };
            System.Windows.Automation.AutomationProperties.SetName(search, "Пошук працівника");
            var label = Text("Знайти працівника");
            DockPanel.SetDock(label, Dock.Top);
            left.Children.Add(label);
            DockPanel.SetDock(search, Dock.Top);
            left.Children.Add(search);
            contacts = new StackPanel();
            left.Children.Add(Scroll(contacts));
            search.TextChanged += (s, e) => DrawContacts();
            root.Children.Add(left);
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition());
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            var header = new WrapPanel();
            conversationTitle = Text("Оберіть співрозмовника", 21);
            conversationTitle.Margin = new Thickness(0, 0, 18, 10);
            header.Children.Add(conversationTitle);
            header.Children.Add(Button("Профіль", async () =>
            {
                if (peer.HasValue)
                    await ShowProfileAsync(owner, peer.Value);
            }));
            header.Children.Add(Button("Зберегти чат", async () =>
            {
                if (!peer.HasValue)
                    throw new InvalidOperationException("Оберіть співрозмовника.");
                await service.ArchiveConversationAsync(peer.Value);
                await DrawConversationAsync();
            }));
            right.Children.Add(header);
            messages = new StackPanel();
            var scroll = Scroll(messages);
            Grid.SetRow(scroll, 1);
            right.Children.Add(scroll);
            var composer = new DockPanel
            {
                Margin = new Thickness(0, 12, 0, 0),
                MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            composer.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = right });
            var send = Button("Надіслати", async () =>
            {
                if (!peer.HasValue)
                    throw new InvalidOperationException("Оберіть співрозмовника.");
                await service.SendAsync(peer.Value, draft.Text);
                draft.Clear();
                await DrawConversationAsync();
                scroll.ScrollToEnd();
            }, true);
            send.Height = 38;
            send.Padding = new Thickness(12, 0, 12, 0);
            send.Margin = new Thickness(0);
            send.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(send, Dock.Right);
            composer.Children.Add(send);
            draft = new TextBox
            {
                Style = (Style)FindResource("CatalogInput"),
                MaxLength = 2000,
                MinHeight = 38,
                MaxHeight = 104,
                MinLines = 1,
                MaxLines = 3,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 0)
            };
            System.Windows.Automation.AutomationProperties.SetName(draft, "Текст повідомлення");
            composer.Children.Add(draft);
            Grid.SetRow(composer, 2);
            right.Children.Add(composer);
            body.Content = root;
        }

        private void DrawContacts()
        {
            string snapshot = staffStamp + ":" + search.Text;
            if (snapshot == contactsStamp)
                return;
            contactsStamp = snapshot;
            contacts.Children.Clear();
            foreach (var person in staff.Where(p => p.Id != Actor && p.Name.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };
                row.Children.Add(Avatar(person, 32));
                row.Children.Add(new TextBlock {
                    Text = person.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 120,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var button = Button("", async () =>
                {
                    if (peer == person.Id)
                        return;
                    peer = person.Id;
                    draft.Clear();
                    conversationStamp = null;
                    await DrawConversationAsync();
                });
                button.Content = row;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                contacts.Children.Add(button);
            }
        }

        private async Task DrawConversationAsync()
        {
            if (!peer.HasValue)
                return;
            var person = staff.SingleOrDefault(p => p.Id == peer);
            conversationTitle.Text = person?.Name ?? "Працівник";
            var rows = await service.ConversationAsync(peer.Value, true);
            if (!ReferenceEquals(session, UserSession.CurrentUser))
                return;
            string stamp = peer + ":" + string.Join(",", rows.Select(m => m.Id + "/" + m.Archived));
            if (stamp == conversationStamp)
                return;
            conversationStamp = stamp;
            messages.Children.Clear();
            foreach (var row in rows)
            {
                var panel = new StackPanel();
                panel.Children.Add(Text(row.Body, 18));
                DateTime date;
                string time = DateTime.TryParse(row.CreatedAt, out date) ? date.ToLocalTime().ToString("dd.MM HH:mm") : row.CreatedAt;
                panel.Children.Add(Text((row.SenderId == Actor ? "Ви" : person?.Name) + " · " + time + (row.Archived ? " · архів" : "")));
                messages.Children.Add(new Border {
                    Child = panel,
                    Background = new SolidColorBrush(row.SenderId == Actor ? Color.FromRgb(87, 100, 81) : Color.FromRgb(80, 60, 59)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14),
                    Margin = new Thickness(row.SenderId == Actor ? 30 : 0, 0, row.SenderId == Actor ? 0 : 30, 12)
                });
            }

            if (rows.Count == 0)
                messages.Children.Add(Text("Тут починається розмова", 20));
        }

        public static FrameworkElement Avatar(PublicProfile profile, double size = 40)
        {
            if (profile?.Role == "Client" || profile?.Role == "Kiosk")
                return new Border
                {
                    Visibility = Visibility.Collapsed
                };
            var image = CatalogService.LoadPhoto(profile?.PhotoPath);
            var circle = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = image == null ? (Brush)new SolidColorBrush(Color.FromRgb(101, 115, 94)) : new ImageBrush(image)
                {
                    Stretch = Stretch.UniformToFill
                }
            };
            if (image == null)
                circle.Child = new TextBlock
                {
                    Text = string.Join("", (profile?.Name ?? "?").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x.Substring(0, 1))).ToUpperInvariant(),
                    FontSize = size * .38,
                    Foreground = Brushes.Gainsboro,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            return circle;
        }

        private static WrapPanel WorkTags(FrameworkElement host, Window owner, PublicProfile profile, IEnumerable<string> tags, Func<PublicProfile, Task> changed)
        {
            var service = new TeamService();
            var panel = new WrapPanel
            {
                Name = "WorkTags",
                IsEnabled = profile.Status == "Active",
                Margin = new Thickness(0, 0, 8, 0)
            };
            var shift = new Button
            {
                Style = (Style)host.FindResource("CatalogButton"),
                Content = "Почати зміну",
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 9, 12, 9)
            };
            Action redraw = () =>
            {
                foreach (var choice in panel.Children.OfType<CheckBox>())
                    choice.IsChecked = StaffWork.Has(profile.Tags, (string)choice.Content);
                shift.Visibility = !profile.IsOnShift && (profile.Role == "Admin" || StaffWork.Locations(profile.Tags).Length > 0) ? Visibility.Visible : Visibility.Collapsed;
            };
            Func<Func<Task<PublicProfile>>, Task> save = async update =>
            {
                panel.IsEnabled = false;
                try
                {
                    profile = await update();
                    redraw();
                    await changed(profile);
                }
                catch (Exception ex)
                {
                    redraw();
                    MessageBox.Show(owner, ShopForm.Friendly(ex), "Sport Drive", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    panel.IsEnabled = profile.Status == "Active";
                }
            };
            foreach (string tag in tags)
            {
                var choice = new CheckBox
                {
                    Content = tag,
                    IsChecked = StaffWork.Has(profile.Tags, tag),
                    Style = (Style)host.FindResource("CatalogTag")
                };
                choice.Click += async (s, e) => await save(() => service.SetStaffTagAsync(profile.Id, tag, choice.IsChecked == true));
                panel.Children.Add(choice);
            }

            shift.Click += async (s, e) => await save(() => service.StartShiftAsync(profile.Id));
            panel.Children.Add(shift);
            redraw();
            return panel;
        }

        public static async Task ShowProfileAsync(Window owner, int id)
        {
            var service = new TeamService();
            var profile = await service.ProfileAsync(id);
            var access = await new CatalogService().GetAccessAsync();
            if (profile.Role == "Client" || profile.Role == "Kiosk")
            {
                await new WorkplaceView().EditAccountAsync(owner, id);
                return;
            }

            bool own = id == UserSession.CurrentUser.Id, message = false, account = false;
            var form = new ShopForm("Профіль")
            {
                Owner = owner,
                Height = 600
            };
            form.ReadOnly();
            var heading = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };
            var picture = Avatar(profile, 80);
            picture.Margin = new Thickness(0, 0, 18, 0);
            heading.Children.Add(picture);
            var details = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            var name = Text(profile.Name, 25);
            name.MaxWidth = 360;
            details.Children.Add(name);
            string role = profile.Role == "Admin" ? "Менеджер" : profile.Role == "Employee" ? "Працівник" : profile.Role == "Kiosk" ? "Кіоск" : "Клієнт";
            details.Children.Add(Text(role + (!access.CanEditCatalog && !string.IsNullOrEmpty(profile.Tags) ? " · " + profile.Tags : "")));
            details.Children.Add(Text(profile.Status == "Active" ? "Активний" : profile.Status == "Pending" ? "Очікує погодження" : "Заблокований"));
            heading.Children.Add(details);
            form.ContentItem(heading);
            var bio = form.Note(profile.Bio ?? "");
            bio.Visibility = string.IsNullOrWhiteSpace(profile.Bio) ? Visibility.Collapsed : Visibility.Visible;
            var taskList = new StackPanel();
            Func<Task> drawTasks = async () =>
            {
                taskList.Children.Clear();
                taskList.Children.Add(Text(profile.IsOnShift ? "На зміні" : "Зміну завершено"));
                taskList.Children.Add(Text("Завдання", 21));
                var rows = (await service.TasksAsync()).Where(t => t.IsMine(id)).ToList();
                if (rows.Count == 0)
                    taskList.Children.Add(Text("Немає поточних завдань"));
                else
                    foreach (var task in rows)
                        taskList.Children.Add(Text(task.Title));
            };
            if (access.CanEditCatalog)
                form.ContentItem(WorkTags(form, form.DialogOwner ?? owner, profile, ShopService.TagNames, async updated =>
                {
                    profile = updated;
                    await drawTasks();
                }));
            var actions = new StackPanel
            {
                Name = "ProfileActions",
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 14, 0, 0)
            };
            form.ContentItem(actions);
            if (own || access.CanEditCatalog)
                form.Action("Редагувати", () =>
                {
                    var editor = new ShopForm("Редагувати профіль")
                    {
                        Owner = form,
                        Height = 600
                    };
                    string photo = null;
                    var preview = Avatar(profile, 64);
                    preview.HorizontalAlignment = HorizontalAlignment.Left;
                    editor.ContentItem(preview);
                    editor.Action("Обрати фото", () =>
                    {
                        var picker = new Microsoft.Win32.OpenFileDialog
                        {
                            Filter = "Зображення|*.png;*.jpg;*.jpeg"
                        };
                        if (picker.ShowDialog(editor.DialogOwner) != true)
                            return;
                        var image = CatalogService.LoadPhoto(picker.FileName);
                        if (image == null)
                        {
                            MessageBox.Show(editor.DialogOwner, "Оберіть зображення до 10 МБ.");
                            return;
                        }

                        photo = picker.FileName;
                        var border = (Border)preview;
                        border.Child = null;
                        border.Background = new ImageBrush(image)
                        {
                            Stretch = Stretch.UniformToFill
                        };
                    });
                    editor.Text("name", "Ім’я", profile.Name);
                    editor.Text("bio", "Про себе", profile.Bio, true);
                    editor.Submit = () => service.SaveProfileAsync(id, editor.Value("name"), editor.Value("bio"), photo);
                    if (editor.ShowDialog() != true)
                        return;
                    profile.Name = editor.Value("name").Trim();
                    profile.Bio = editor.Value("bio").Trim();
                    name.Text = profile.Name;
                    bio.Text = profile.Bio;
                    bio.Visibility = profile.Bio.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                    if (photo != null)
                        profile.PhotoPath = photo;
                    heading.Children.Remove(picture);
                    picture = Avatar(profile, 80);
                    picture.Margin = new Thickness(0, 0, 18, 0);
                    heading.Children.Insert(0, picture);
                }, actions);
            if (own)
                form.Action("Змінити пароль", () => ShopForm.ChangePassword(form, id, true), actions);
            if (access.CanEditCatalog)
            {
                var settings = form.Action("", () =>
                {
                    account = true;
                    form.Close();
                }, actions);
                settings.Width = 40;
                settings.Padding = new Thickness(9);
                settings.Content = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M9,1 L13,1 14,4 17,6 20,5 22,9 20,11 20,14 22,16 20,20 17,19 14,21 13,24 9,24 8,21 5,19 2,20 0,16 2,14 2,11 0,9 2,5 5,6 8,4 Z M11,8 A4.5,4.5 0 1 0 11,17 A4.5,4.5 0 1 0 11,8"),
                    Fill = Brushes.Gainsboro,
                    Stretch = Stretch.Uniform,
                    Width = 20,
                    Height = 20
                };
                System.Windows.Automation.AutomationProperties.SetName(settings, "Налаштування доступу");
            }

            if ((profile.Role == "Admin" || profile.Role == "Employee") && access.CanReadOperations)
            {
                if (!own)
                    form.Action("Написати", () =>
                    {
                        message = true;
                        form.Close();
                    }, actions);
                taskList.Margin = new Thickness(0, 20, 0, 0);
                form.ContentItem(taskList);
                await drawTasks();
                if (!own && !access.CanEditCatalog)
                    form.Action("Завдання", () => taskList.BringIntoView(), actions);
            }

            if (!owner.IsVisible)
            {
                form.Close();
                return;
            }

            form.ShowDialog();
            if (account)
                await new WorkplaceView().EditAccountAsync(owner, id);
            if (message && owner is MainWindow main)
                await main.OpenTeamAsync("Повідомлення", id);
        }
    }
}
