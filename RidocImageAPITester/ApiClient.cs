using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RidocImageAPITester
{
    // ── API レスポンスモデル ────────────────────────────────────────────────
    public class ApiErrorResponse
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("errorKey")]
        public string? ErrorKey { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }

    // ── 画像取得結果 ────────────────────────────────────────────────────────
    public sealed class FetchResult : IDisposable
    {
        public bool    IsSuccess    { get; init; }
        public int     StatusCode   { get; init; }
        public byte[]? ImageBytes   { get; init; }
        public string? ContentType  { get; init; }
        public string? FileName     { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ErrorKey     { get; init; }
        public string? ErrorDetail  { get; init; }
        public long    ElapsedMs    { get; init; }
        public long    SizeBytes    => ImageBytes?.LongLength ?? 0;

        public void Dispose() { /* byte[] は GC に任せる */ }
    }

    // ── API クライアント ────────────────────────────────────────────────────
    public sealed class RidocImageApiClient : IDisposable
    {
        private readonly HttpClient _http;

        public RidocImageApiClient(string baseUrl)
        {
            // HttpClient は再利用。証明書エラーを開発時は無視できるよう設定。
            var handler = new HttpClientHandler
            {
                // 開発環境の自己署名証明書を許容（本番では false に変えること）
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout     = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// GET /v1/DrawingImage?docId=…&imgType=… を呼び出す
        /// </summary>
        public async Task<FetchResult> GetImageAsync(
            string docId, string imgType, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                string url = $"v1/DrawingImage?docId={Uri.EscapeDataString(docId)}&imgType={Uri.EscapeDataString(imgType)}";
                using var response = await _http.GetAsync(url, ct);

                sw.Stop();
                int status = (int)response.StatusCode;

                // ── 成功 ──────────────────────────────────────────────────
                if (response.IsSuccessStatusCode)
                {
                    var bytes      = await response.Content.ReadAsByteArrayAsync(ct);
                    var ct2        = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                    var disposition = response.Content.Headers.ContentDisposition?.FileNameStar
                                   ?? response.Content.Headers.ContentDisposition?.FileName
                                   ?? $"{docId}_{imgType}";

                    return new FetchResult
                    {
                        IsSuccess   = true,
                        StatusCode  = status,
                        ImageBytes  = bytes,
                        ContentType = ct2,
                        FileName    = disposition.Trim('"'),
                        ElapsedMs   = sw.ElapsedMilliseconds
                    };
                }

                // ── エラーレスポンス ───────────────────────────────────────
                ApiErrorResponse? err = null;
                try { err = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(ct); }
                catch { /* JSON でない場合は無視 */ }

                return new FetchResult
                {
                    IsSuccess    = false,
                    StatusCode   = status,
                    ErrorMessage = err?.Message ?? $"HTTP {status}",
                    ErrorKey     = err?.ErrorKey,
                    ErrorDetail  = err?.Detail,
                    ElapsedMs    = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                return new FetchResult
                {
                    IsSuccess    = false,
                    StatusCode   = 0,
                    ErrorMessage = "キャンセルされました。",
                    ElapsedMs    = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                return new FetchResult
                {
                    IsSuccess    = false,
                    StatusCode   = 0,
                    ErrorMessage = $"通信エラー: {ex.Message}",
                    ErrorDetail  = ex.ToString(),
                    ElapsedMs    = sw.ElapsedMilliseconds
                };
            }
        }

        /// <summary>バイト配列を一時ファイルに保存して既定のアプリで開く</summary>
        public static string SaveAndOpen(byte[] bytes, string fileName)
        {
            string dir  = Path.Combine(Path.GetTempPath(), "RidocAPITester");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, bytes);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return path;
        }

        public void Dispose() => _http.Dispose();
    }
}
