using FCatFlasher.Models;

namespace FCatFlasher.Services
{
    internal class AuthenticationService(
        ApiService apiService,
        LocalDatabaseService localDatabaseService)
    {
        public string? LastLoginError { get; private set; }

        public CurrentUser? Login(string username, string password)
        {
            LastLoginError = null;

            try
            {
                CurrentUser? user = apiService.Login(username, password);

                if (user == null)
                    return null;

                localDatabaseService.CacheUser(user, password);
                localDatabaseService.SyncPending(apiService);

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
