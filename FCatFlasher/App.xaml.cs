using FCatFlasher.Models;
using FCatFlasher.Services;
using System.Windows;

namespace FCatFlasher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var settingsService = new SettingsService();
            AppSettings settings = settingsService.Load();
            var apiService = new ApiService(settings.ApiBaseUrl);
            var localDatabaseService = new LocalDatabaseService();

            localDatabaseService.Initialize();

            var loginWindow = new LoginWindow(
                new AuthenticationService(apiService, localDatabaseService));

            if (loginWindow.ShowDialog() != true || loginWindow.CurrentUser == null)
            {
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow(
                apiService,
                localDatabaseService,
                loginWindow.CurrentUser);

            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
    }
}
