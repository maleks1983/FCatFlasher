using FCatFlasher.Models;
using FCatFlasher.Services;
using System.Windows;

namespace FCatFlasher
{
/// <summary>
/// Діалог авторизації оператора.
/// </summary>
    public partial class LoginWindow : Window
    {
        private readonly AuthenticationService _authenticationService;
        private bool _loginPending;
        private bool _closed;

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            base.OnClosed(e);
        }

        internal LoginWindow(AuthenticationService authenticationService)
        {
            InitializeComponent();

            _authenticationService = authenticationService;
            UsernameTextBox.Focus();
        }

        internal CurrentUser? CurrentUser { get; private set; }

/// <summary>
/// Перевіряє введені облікові дані та закриває діалог при успіху.
/// </summary>
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loginPending)
                return;
            _loginPending = true;
            var button = (System.Windows.Controls.Button)sender;
            button.IsEnabled = false;
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;
            try
            {
                CurrentUser = await Task.Run(() => _authenticationService.Login(username, password));
            }
            catch (Exception ex)
            {
                if (!_closed)
                    ErrorTextBlock.Text = "Не вдалося увійти: " + ex.Message;
                return;
            }
            finally
            {
                _loginPending = false;
                button.IsEnabled = true;
            }

            if (_closed)
                return;

            if (CurrentUser == null)
            {
                ErrorTextBlock.Text = _authenticationService.LastLoginError
                    ?? "Невірний телефон або пароль.";
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_authenticationService.LastSyncError))
            {
                MessageBox.Show(
                    _authenticationService.LastSyncError,
                    "Помилка синхронізації",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DialogResult = true;
        }

/// <summary>
/// Скасовує авторизацію і закриває застосунок.
/// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
