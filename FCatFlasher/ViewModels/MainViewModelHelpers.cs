using System.Media;
using System.Windows;

namespace FCatFlasher.ViewModels;

internal static class MainViewModelHelpers
{
    public static void ShowTestErrorDialog(string title, string details)
    {
        SystemSounds.Hand.Play();
        string message = string.IsNullOrWhiteSpace(details)
            ? "Тест завершився з помилкою."
            : details.Trim();

        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
