using FCatFlasher.Models;

namespace FCatFlasher.Services
{
/// <summary>
/// Авторизує користувача через API та кешує дані для локальних звітів.
/// </summary>
    internal class AuthenticationService(
        ApiService apiService,
        LocalDatabaseService localDatabaseService)
    {
        public string? LastLoginError { get; private set; }
        public string? LastSyncError { get; private set; }

/// <summary>
/// Авторизує користувача онлайн. Без підтвердження API вхід заборонено.
/// </summary>
        public CurrentUser? Login(string username, string password)
        {
            LastLoginError = null;
            LastSyncError = null;

            try
            {
                CurrentUser? user = apiService.Login(username, password);

                if (user == null)
                    return null;

                localDatabaseService.CacheUser(user, password);

                return user;
            }
            catch (Exception ex)
            {
                LastLoginError = $"Не вдалося підключитися до Spring Boot API: {ex.Message}";

                return null;
            }
        }
    }
}
