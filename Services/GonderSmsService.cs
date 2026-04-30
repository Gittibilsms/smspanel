using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.IO.Compression;
namespace GittBilSmsCore.Services
{
    public class GonderSmsService
    {
        private static string _cachedToken;
        private static DateTime _tokenExpiryUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

        private const string AuthUrl = "https://gondersms.com:8443/api/user/auth";
        private const string SendUrl = "https://gondersms.com:8443/api/sms/send";

        public class SendResult
        {
            public bool Success { get; set; }
            public string CampaignId { get; set; }
            public string Message { get; set; }
            public string RawResponse { get; set; }
            public int HttpStatus { get; set; }
        }

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
                                       | DecompressionMethods.Deflate
                                       | DecompressionMethods.Brotli
            };
            var client = new HttpClient(handler);
            client.Timeout = timeout;
            return client;
        }

        /// <summary>Gets a valid JWT token, logging in if needed.</summary>
        public async Task<string> GetTokenAsync(string username, string password)
        {
            await _tokenLock.WaitAsync();
            try
            {
                if (!string.IsNullOrEmpty(_cachedToken) &&
                    DateTime.UtcNow < _tokenExpiryUtc.AddMinutes(-5))
                {
                    return _cachedToken;
                }

                using var client = CreateClient(TimeSpan.FromSeconds(30));

                var loginBody = JsonConvert.SerializeObject(new
                {
                    username = username,
                    password = password,
                    rememberme = false
                });

                var request = new HttpRequestMessage(HttpMethod.Post, AuthUrl)
                {
                    Content = new StringContent(loginBody, Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"GonderSMS login failed: HTTP {(int)response.StatusCode} - {body}");

                var parsed = JObject.Parse(body);
                var token = parsed["user"]?["token"]?.ToString();
                if (string.IsNullOrEmpty(token))
                    throw new Exception($"GonderSMS login returned no token. Response: {body}");

                _cachedToken = token;
                _tokenExpiryUtc = GetJwtExpiryUtc(token);
                return _cachedToken;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>Sends an SMS via GonderSMS. Retries once on 401.</summary>
        public async Task<SendResult> SendSmsAsync(
            string username,
            string password,
            string heading,
            string message,
            IEnumerable<string> phoneNumbers,
            DateTime? plannedAt = null)
        {
            var token = await GetTokenAsync(username, password);
            var result = await SendInternalAsync(token, heading, message, phoneNumbers, plannedAt);

            if (result.HttpStatus == 401)
            {
                InvalidateToken();
                token = await GetTokenAsync(username, password);
                result = await SendInternalAsync(token, heading, message, phoneNumbers, plannedAt);
            }

            return result;
        }

        private async Task<SendResult> SendInternalAsync(
            string token, string heading, string message,
            IEnumerable<string> phoneNumbers, DateTime? plannedAt)
        {
            using var client = CreateClient(TimeSpan.FromMinutes(10));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var phoneArray = phoneNumbers.Select(n => n?.Trim()).Where(n => !string.IsNullOrEmpty(n)).ToArray();
            var jsonPayload = JsonConvert.SerializeObject(phoneArray);
            var plainBytes = Encoding.UTF8.GetBytes(jsonPayload);

            // GonderSMS expects the recipients file to be gzip-compressed
            byte[] gzippedBytes;
            using (var ms = new MemoryStream())
            {
                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                {
                    gz.Write(plainBytes, 0, plainBytes.Length);
                }
                gzippedBytes = ms.ToArray();
            }

            using var form = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(gzippedBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            form.Add(fileContent, "file", "recipients.txt.gz");

            form.Add(new StringContent(message ?? ""), "message");
            form.Add(new StringContent(heading ?? ""), "heading");
            form.Add(new StringContent("gsm7"), "encoding");
            form.Add(new StringContent(plannedAt.HasValue
                ? plannedAt.Value.ToUniversalTime().ToString("o")
                : ""), "plannedAt");

            var response = await client.PostAsync(SendUrl, form);
            var raw = await response.Content.ReadAsStringAsync();

            var result = new SendResult
            {
                RawResponse = raw,
                HttpStatus = (int)response.StatusCode
            };

            try
            {
                var json = JObject.Parse(raw);
                result.Success = response.IsSuccessStatusCode && (json["success"]?.Value<bool>() ?? false);
                result.CampaignId = json["campaignId"]?.ToString();
                result.Message = json["message"]?.ToString();
            }
            catch
            {
                result.Success = false;
                result.Message = "Invalid response format from GonderSMS";
            }

            return result;
        }

        private void InvalidateToken()
        {
            _cachedToken = null;
            _tokenExpiryUtc = DateTime.MinValue;
        }

        private static DateTime GetJwtExpiryUtc(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length != 3) return DateTime.UtcNow.AddHours(1);

                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var parsed = JObject.Parse(decoded);
                var exp = parsed["exp"]?.Value<long>() ?? 0;
                if (exp == 0) return DateTime.UtcNow.AddHours(1);
                return DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
            }
            catch
            {
                return DateTime.UtcNow.AddHours(1);
            }
        }
    }
}