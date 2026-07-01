using FCatFlasher.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FCatFlasher.Services
{
    internal class ApiService(string baseUrl)
    {
        private readonly HttpClient _httpClient = new()
        {
            BaseAddress = new Uri(NormalizeBaseUrl(baseUrl)),
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public CurrentUser? Login(string username, string password)
        {
            var response = Post<LoginRequest, LoginResponse>(
                "api/auth/login",
                new LoginRequest(username, password));

            if (response == null)
                return null;

            if (!string.IsNullOrWhiteSpace(response.Token))
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", response.Token);

            return new CurrentUser
            {
                Id = response.Id,
                Username = response.Username,
                FullName = response.FullName
            };
        }

        public void AddDeviceRegistration(
            string userId,
            string serialNumber,
            string mcuName,
            string firmwareName,
            string registrationType)
        {
            Post(
                "api/device-registrations",
                new DeviceRegistrationRequest(
                    userId,
                    serialNumber,
                    mcuName,
                    firmwareName,
                    registrationType,
                    Environment.MachineName));
        }

        public void AddBoxReport(string userId, BoxReport report)
        {
            Post(
                "api/box-reports",
                new BoxReportRequest(
                    userId,
                    report.BoxNumber,
                    report.Quantity,
                    report.FirmwareName,
                    report.SerialNumbersText,
                    report.FirstSerial,
                    report.LastSerial,
                    Environment.MachineName));
        }

        public void AddReworkRegistration(
            string userId,
            string orderId,
            string serialNumber,
            string firmwareName,
            string firmwarePath,
            string mcuId,
            string mcuName,
            string result)
        {
            Post(
                "api/rework-registrations",
                new ReworkRegistrationRequest(
                    userId,
                    orderId,
                    serialNumber,
                    firmwareName,
                    firmwarePath,
                    mcuId,
                    mcuName,
                    Environment.MachineName,
                    result));
        }

        public IReadOnlyList<BoxReport> LoadBoxReports()
        {
            return Get<List<BoxReport>>("api/box-reports") ?? new List<BoxReport>();
        }

        public int CountDeviceRegistrationsFrom(DateTime from, string registrationType)
        {
            string fromText = Uri.EscapeDataString(from.Date.ToString("O"));
            string typeText = Uri.EscapeDataString(registrationType);
            var response = Get<CountResponse>($"api/device-registrations/count?from={fromText}&type={typeText}");

            return response?.Count ?? 0;
        }

        public ReworkOrderInfo? LoadReworkOrder(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                return null;

            string orderIdText = Uri.EscapeDataString(orderId.Trim());
            var response = Get<ReworkOrderResponse>($"api/order-requests/{orderIdText}/flasher");

            if (response == null)
                return null;

            return new ReworkOrderInfo
            {
                OrderId = response.Id.ToString(),
                FirmwareName = response.FirmwareName,
                FirmwarePath = response.FirmwarePath
            };
        }

        private TResponse? Get<TResponse>(string url)
        {
            using HttpResponseMessage response = _httpClient
                .GetAsync(url)
                .GetAwaiter()
                .GetResult();

            if (!response.IsSuccessStatusCode)
                return default;

            return response.Content
                .ReadFromJsonAsync<TResponse>(JsonOptions)
                .GetAwaiter()
                .GetResult();
        }

        private void Post<TRequest>(string url, TRequest request)
        {
            using HttpResponseMessage response = _httpClient
                .PostAsJsonAsync(url, request, JsonOptions)
                .GetAwaiter()
                .GetResult();

            response.EnsureSuccessStatusCode();
        }

        private TResponse? Post<TRequest, TResponse>(string url, TRequest request)
        {
            using HttpResponseMessage response = _httpClient
                .PostAsJsonAsync(url, request, JsonOptions)
                .GetAwaiter()
                .GetResult();

            if (!response.IsSuccessStatusCode)
                return default;

            return response.Content
                .ReadFromJsonAsync<TResponse>(JsonOptions)
                .GetAwaiter()
                .GetResult();
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return "http://localhost:8080/";

            return baseUrl.EndsWith("/")
                ? baseUrl
                : baseUrl + "/";
        }

        private sealed record LoginRequest(string Username, string Password);

        private sealed record LoginResponse(
            string Id,
            string Username,
            string FullName,
            string? Token);

        private sealed record DeviceRegistrationRequest(
            string UserId,
            string SerialNumber,
            string McuName,
            string FirmwareName,
            string RegistrationType,
            string StationName);

        private sealed record BoxReportRequest(
            string UserId,
            int BoxNumber,
            int Quantity,
            string FirmwareName,
            string SerialNumbersText,
            string FirstSerial,
            string LastSerial,
            string StationName);

        private sealed record ReworkRegistrationRequest(
            string UserId,
            string OrderId,
            string SerialNumber,
            string FirmwareName,
            string FirmwarePath,
            string McuId,
            string McuName,
            string StationName,
            string Result);

        private sealed record CountResponse(int Count);

        private sealed record ReworkOrderResponse(
            long Id,
            string FirmwareName,
            string FirmwarePath);
    }

    internal class ReworkOrderInfo
    {
        public string OrderId { get; set; } = string.Empty;
        public string FirmwareName { get; set; } = string.Empty;
        public string FirmwarePath { get; set; } = string.Empty;
    }
}
