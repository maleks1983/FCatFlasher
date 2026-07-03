using FCatFlasher.Models;

namespace FCatFlasher.Services
{
/// <summary>
/// Обєднує онлайн-логін через API та fallback-логін із локального кешу.
/// </summary>
    internal class AuthenticationService(
        ApiService apiService,
        LocalDatabaseService localDatabaseService)
    {
        public string? LastLoginError { get; private set; }
        public string? LastSyncError { get; private set; }

/// <summary>
/// Авторизує користувача онлайн або, якщо API недоступний, через локальну базу.
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

                try
                {
                    localDatabaseService.SyncPending(apiService);
                }
                catch (Exception ex)
                {
                    LastSyncError = "Не вдалося синхронізувати локальні дані: " + ex.Message;
                }

                return user;
            }
            catch (Exception ex)
            {
                LastLoginError = $"Не вдалося підключитися до Spring Boot API: {ex.Message}";

                try
                {
                    return localDatabaseService.LoginOffline(username, password)
                        ?? LoginForTesting(username, password);
                }
                catch
                {
                    return LoginForTesting(username, password);
                }
            }
        }

/// <summary>
/// Дозволяє тестовий логін без сервера для локальної перевірки застосунку.
/// </summary>
        private static CurrentUser? LoginForTesting(string username, string password)
        {
            if (!string.Equals(username.Trim(), "admin", StringComparison.OrdinalIgnoreCase) ||
                (password != "admin" && password != "2"))
            {
                return null;
            }

            return new CurrentUser
            {
                Id = "00000000-0000-0000-0000-000000000001",
                Username = "admin",
                FullName = "Test Admin",
                IsOffline = true
            };
        }
    }
}
