using FCatFlasher.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FCatFlasher.Services
{
/// <summary>
/// HTTP-клієнт для авторизації, реєстрацій, коробок і rework-даних у Spring Boot API.
/// </summary>
    internal class ApiService(string baseUrl)
    {
        private readonly HttpClient _httpClient = new()
        {
            BaseAddress = new Uri(NormalizeBaseUrl(baseUrl)),
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

/// <summary>
/// Авторизує оператора на сервері та зберігає bearer-токен для наступних запитів.
/// </summary>
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
                Id = response.Id.ToString(),
                Username = response.Username,
                FullName = response.FullName
            };
        }

/// <summary>
/// Надсилає на сервер реєстрацію протестованої або прошитої плати.
/// </summary>
        public void AddDeviceRegistration(
            string userId,
            string serialNumber,
            string mcuId,
            string mcuName,
            string firmwareName,
            string registrationType)
        {
            Post(
                "api/device-registrations",
                new DeviceRegistrationRequest(
                    userId,
                    serialNumber,
                    mcuId,
                    mcuName,
                    firmwareName,
                    registrationType,
                    Environment.MachineName));
        }

/// <summary>
/// Надсилає звіт заповненої виробничої коробки.
/// </summary>
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

/// <summary>
/// Надсилає результат rework-прошивки для конкретного замовлення.
/// </summary>
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

/// <summary>
/// Завантажує з сервера історію виробничих коробок.
/// </summary>
        public IReadOnlyList<BoxReport> LoadBoxReports()
        {
            return Get<List<BoxReport>>("api/box-reports") ?? new List<BoxReport>();
        }

        public IReadOnlyList<ReworkSummary>? LoadReworkSummaries(DateTime from)
        {
            string fromText = Uri.EscapeDataString(from.Date.ToString("O"));
            List<ReworkRegistrationResponse>? registrations =
                Get<List<ReworkRegistrationResponse>>(
                    $"api/rework-registrations?from={fromText}");

            if (registrations == null)
                return null;

            return registrations
                .GroupBy(item => new { item.OrderId, item.FirmwareName })
                .Select(group => new ReworkSummary
                {
                    OrderId = group.Key.OrderId,
                    FirmwareName = group.Key.FirmwareName,
                    Quantity = group.Count(),
                    FirstCreatedAt = group.Min(item => item.CreatedAt),
                    LastCreatedAt = group.Max(item => item.CreatedAt)
                })
                .OrderByDescending(item => item.LastCreatedAt)
                .ToList();
        }

/// <summary>
/// Рахує серверні реєстрації плат від заданої дати за типом операції.
/// </summary>
        public int CountDeviceRegistrationsFrom(DateTime from, string registrationType)
        {
            string fromText = Uri.EscapeDataString(from.Date.ToString("O"));
            string typeText = Uri.EscapeDataString(registrationType);
            var response = Get<CountResponse>($"api/device-registrations/count?from={fromText}&type={typeText}");

            return response?.Count ?? 0;
        }

/// <summary>
/// Завантажує параметри rework-замовлення для flasher-режиму.
/// </summary>
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

/// <summary>
/// Виконує синхронний GET і десеріалізує JSON-відповідь.
/// </summary>
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

/// <summary>
/// Виконує POST і кидає виняток, якщо сервер повернув помилку.
/// </summary>
        private void Post<TRequest>(string url, TRequest request)
        {
            using HttpResponseMessage response = _httpClient
                .PostAsJsonAsync(url, request, JsonOptions)
                .GetAwaiter()
                .GetResult();

            if (response.IsSuccessStatusCode)
                return;

            string responseBody = response.Content
                .ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();

            string details = string.IsNullOrWhiteSpace(responseBody)
                ? "відповідь без тексту"
                : responseBody.Trim();

            throw new HttpRequestException(
                $"POST {url}: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {details}",
                inner: null,
                response.StatusCode);
        }

/// <summary>
/// Виконує POST із JSON-відповіддю.
/// </summary>
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

/// <summary>
/// Нормалізує базову адресу API та додає завершальний slash.
/// </summary>
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
            JsonElement Id,
            string Username,
            string FullName,
            string? Token);

        private sealed record DeviceRegistrationRequest(
            string UserId,
            string SerialNumber,
            string McuId,
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

        private sealed record ReworkRegistrationResponse(
            string OrderId,
            string SerialNumber,
            string FirmwareName,
            string McuName,
            string Result,
            DateTime CreatedAt);
    }

/// <summary>
/// Дані rework-замовлення, які потрібні flasher-додатку.
/// </summary>
    internal class ReworkOrderInfo
    {
        public string OrderId { get; set; } = string.Empty;
        public string FirmwareName { get; set; } = string.Empty;
        public string FirmwarePath { get; set; } = string.Empty;
    }
}
