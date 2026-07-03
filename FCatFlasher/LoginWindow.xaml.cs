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
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CurrentUser = _authenticationService.Login(
                    UsernameTextBox.Text,
                    PasswordBox.Password);
            }
            catch (Exception ex)
            {
                ErrorTextBlock.Text = "Не вдалося увійти: " + ex.Message;
                return;
            }

            if (CurrentUser == null)
            {
                ErrorTextBlock.Text = _authenticationService.LastLoginError
                    ?? "Невірний телефон або пароль.";
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
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
