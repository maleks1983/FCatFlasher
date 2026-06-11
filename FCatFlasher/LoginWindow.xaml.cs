using FCatFlasher.Models;
using FCatFlasher.Services;
using System.Windows;

namespace FCatFlasher
{
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
                ErrorTextBlock.Text = "Не вдалося підключитися до Spring Boot API: " + ex.Message;
                return;
            }

            if (CurrentUser == null)
            {
                ErrorTextBlock.Text = "Невірний телефон або пароль.";
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
