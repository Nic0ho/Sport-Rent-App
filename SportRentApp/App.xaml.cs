using System;
using System.Threading.Tasks;
using System.Windows;

namespace SportRentApp
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                bool needsSetup = await Task.Run(() => ShopService.NeedsSetup());
                MainWindow = new AuthWindow();
                MainWindow.Show();

                if (needsSetup)
                {
                    var form = new ShopForm("Зареєструйте першого менеджера", "Створити менеджера")
                    {
                        Owner = MainWindow
                    };
                    form.Text("login", "Логін");
                    form.Password("password", "Пароль (від 8 символів)");
                    form.Text("name", "Повне ім’я");
                    form.Text("phone", "Телефон");
                    form.Password("passwordRepeat", "Повторіть пароль");
                    form.Submit = () => new ShopService().CompleteSetupAsync(form.Value("login"), form.Value("password"), form.Value("name"), form.Value("phone"));
                    if (form.ShowDialog() != true)
                    {
                        Shutdown();
                        return;
                    }
                }

                ShutdownMode = ShutdownMode.OnLastWindowClose;
            }
            catch (Exception)
            {
                MessageBox.Show("Не вдалося підготувати базу даних. Перевірте папку встановлення та резервну копію.", "Sport Drive");
                Shutdown();
            }
        }
    }
}
