using FCatFlasher.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace FCatFlasher.Services
{
/// <summary>
/// HTTP-клієнт для авторизації, реєстрацій, коробок і rework-даних у Spring Boot API.
/// </summary>
    internal class ApiService(string baseUrl, HttpMessageHandler? handler = null)
    {
        private readonly HttpClient _httpClient = new(handler ?? new HttpClientHandler())
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
            string firmwarePath,
            string firmwareChecksumSha256,
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
                    firmwarePath,
                    firmwareChecksumSha256,
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
            string productName,
            string firmwareName,
            string firmwarePath,
            string firmwareChecksumSha256,
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
                    productName,
                    firmwareName,
                    firmwarePath,
                    firmwareChecksumSha256,
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
                .GroupBy(item => new { item.OrderId, item.ProductName })
                .Select(group => new ReworkSummary
                {
                    OrderId = group.Key.OrderId,
                    ProductName = group.Key.ProductName,
                    FirmwareName = group.Select(item => item.FirmwareName).FirstOrDefault() ?? string.Empty,
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

        public IReadOnlyList<ReworkOrderInfo> LoadActiveReworkTasks()
        {
            List<DatePlanResponse>? tasks =
                Get<List<DatePlanResponse>>("api/date-plans/my-active");

            if (tasks == null)
                return Array.Empty<ReworkOrderInfo>();

            List<ProductResponse>? products =
                Get<List<ProductResponse>>("api/products");
            Dictionary<long, ProductResponse> productsById = products?
                .ToDictionary(product => product.Id)
                ?? new Dictionary<long, ProductResponse>();

            return tasks
                .Where(task => task.Rework == true
                    && task.Completed != true
                    && string.Equals(
                    task.Status,
                    "AWAITING_FLASH",
                    StringComparison.OrdinalIgnoreCase))
                .Select(task =>
                {
                    productsById.TryGetValue(
                        task.ProductId ?? 0,
                        out ProductResponse? product);

                    return new ReworkOrderInfo
                    {
                        TaskId = task.Id,
                        OrderId = task.OrderRequestId?.ToString() ?? string.Empty,
                        TrackingNumber = task.TrackingNumberIn?.ToString() ?? string.Empty,
                        Quantity = task.Quantity ?? 0,
                        ProductName = product?.Name?.Trim()
                            ?? task.ProductName?.Trim()
                            ?? string.Empty,
                        FirmwareName = product?.FirmwareName?.Trim()
                            ?? task.FirmwareName?.Trim()
                            ?? string.Empty,
                        C0FirmwareName = product?.C0FirmwareName?.Trim()
                            ?? task.C0FirmwareName?.Trim()
                            ?? string.Empty,
                        G0FirmwareName = product?.G0FirmwareName?.Trim()
                            ?? task.G0FirmwareName?.Trim()
                            ?? string.Empty,
                        C0FirmwareId = product?.C0FirmwareId ?? task.C0FirmwareId,
                        G0FirmwareId = product?.G0FirmwareId ?? task.G0FirmwareId
                    };
                })
                .Where(task => !string.IsNullOrWhiteSpace(task.OrderId))
                .ToList();
        }

        public string DownloadFirmware(long firmwareId, string fileName)
        {
            string expectedChecksum = LoadFirmwareChecksum(firmwareId);

            using HttpResponseMessage response = _httpClient
                .GetAsync($"api/firmware/{firmwareId}/download")
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            string downloadFileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? fileName;
            downloadFileName = Path.GetFileName(downloadFileName);

            string safeFileName = string.Join("_", (downloadFileName ?? string.Empty)
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeFileName))
                safeFileName = $"firmware-{firmwareId}.hex";

            string cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FCatFlasher",
                "FirmwareCache");
            Directory.CreateDirectory(cacheDirectory);
            string localPath = Path.Combine(cacheDirectory, safeFileName);
            string temporaryPath = localPath + ".download";
            byte[] firmwareData = response.Content
                .ReadAsByteArrayAsync()
                .GetAwaiter()
                .GetResult();

            if (firmwareData.Length == 0)
                throw new InvalidDataException("Сервер повернув порожній файл прошивки.");

            if (response.Headers.TryGetValues(
                "X-Firmware-SHA256",
                out IEnumerable<string>? checksumValues))
            {
                expectedChecksum = checksumValues.FirstOrDefault()?.Trim()
                    ?? expectedChecksum;
            }

            string actualChecksum = Convert.ToHexString(SHA256.HashData(firmwareData)).ToLowerInvariant();
            if (expectedChecksum.Length == 64
                && !string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Контрольна сума прошивки не збігається. Очікується {expectedChecksum}, отримано {actualChecksum}.");
            }

            File.WriteAllBytes(temporaryPath, firmwareData);
            string writtenChecksum = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(temporaryPath)))
                .ToLowerInvariant();
            if (!string.Equals(actualChecksum, writtenChecksum, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidDataException("Контрольна сума записаного файла прошивки не збігається.");
            }

            File.Move(temporaryPath, localPath, overwrite: true);
            File.WriteAllText(localPath + ".sha256", actualChecksum);
            return localPath;
        }

        private string LoadFirmwareChecksum(long firmwareId)
        {
            List<FirmwareMetadataResponse>? firmwares =
                Get<List<FirmwareMetadataResponse>>("api/firmware");
            return firmwares?
                .FirstOrDefault(firmware => firmware.Id == firmwareId)?
                .DataChecksumSha256?
                .Trim()
                ?? string.Empty;
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
            string FirmwarePath,
            string FirmwareChecksumSha256,
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
            string ProductName,
            string FirmwareName,
            string FirmwarePath,
            string FirmwareChecksumSha256,
            string McuId,
            string McuName,
            string StationName,
            string Result);

        private sealed record CountResponse(int Count);

        private sealed record ReworkOrderResponse(
            long Id,
            string FirmwareName,
            string FirmwarePath);

        private sealed record DatePlanResponse(
            long Id,
            long? ProductId,
            long? OrderRequestId,
            long? TrackingNumberIn,
            string? Status,
            string? ProductName,
            string? FirmwareName,
            bool? Rework,
            bool? Completed,
            int? Quantity,
            string? C0FirmwareName,
            string? G0FirmwareName,
            long? C0FirmwareId,
            long? G0FirmwareId);

        private sealed record ProductResponse(
            long Id,
            string? Name,
            string? FirmwareName,
            string? C0FirmwareName,
            string? G0FirmwareName,
            long? C0FirmwareId,
            long? G0FirmwareId);

        private sealed record FirmwareMetadataResponse(
            long Id,
            string? DataChecksumSha256);

        private sealed record ReworkRegistrationResponse(
            string OrderId,
            string SerialNumber,
            string ProductName,
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
        public long TaskId { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string TrackingNumber { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string FirmwareName { get; set; } = string.Empty;
        public string FirmwarePath { get; set; } = string.Empty;
        public IReadOnlyList<string> FirmwareNames { get; set; } = Array.Empty<string>();
        public string C0FirmwareName { get; set; } = string.Empty;
        public string G0FirmwareName { get; set; } = string.Empty;
        public long? C0FirmwareId { get; set; }
        public long? G0FirmwareId { get; set; }
        public string RegistrationOrderId => string.IsNullOrWhiteSpace(TrackingNumber)
            ? OrderId
            : TrackingNumber;
        public string DisplayName
        {
            get
            {
                string parcelNumber = string.IsNullOrWhiteSpace(TrackingNumber)
                    ? OrderId
                    : TrackingNumber;

                string description = string.IsNullOrWhiteSpace(ProductName)
                    ? parcelNumber
                    : $"{parcelNumber} — {ProductName}";
                return TaskId > 0 ? $"Задача #{TaskId}: {description}" : description;
            }
        }
    }
}
