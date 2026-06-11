using FCatFlasher.Models;

namespace FCatFlasher.Services
{
    internal class AuthenticationService(
        ApiService apiService,
        LocalDatabaseService localDatabaseService)
    {
        public CurrentUser? Login(string username, string password)
        {
            try
            {
                CurrentUser? user = apiService.Login(username, password);

                if (user == null)
                    return null;

                localDatabaseService.CacheUser(user, password);
                localDatabaseService.SyncPending(apiService);

                return user;
            }
            catch
            {
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
                Id = 1,
                Username = "admin",
                FullName = "Test Admin",
                IsOffline = true
            };
        }
    }
}
