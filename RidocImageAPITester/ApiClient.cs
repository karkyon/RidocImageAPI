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
    public sealed partial class RidocImageApiClient : IDisposable
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

// ── multi エンドポイント対応 ──────────────────────────────────────────────
namespace RidocImageAPITester
{
    // 候補一覧のアイテム
    public sealed class DrawingImageListItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id           { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name         { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("sectionCount")]
        public long   SectionCount { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("extension")]
        public string Extension    { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("size")]
        public long   Size         { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public int    Index        { get; init; }
    }

    // 候補一覧レスポンス
    public sealed class DrawingImageSearchResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("docId")]
        public string DocId      { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("totalCount")]
        public long   TotalCount { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("candidates")]
        public System.Collections.Generic.List<DrawingImageListItem> Candidates { get; init; } = new();
    }

    // multi エンドポイントの1件分の結果（バイナリ + メタ情報）
    public sealed class MultiFetchItem : System.IDisposable
    {
        public int     Index       { get; init; }
        public string  DocumentName { get; init; } = string.Empty;
        public string  ContentType  { get; init; } = string.Empty;
        public string  FileName     { get; init; } = string.Empty;
        public byte[]  ImageBytes   { get; init; } = System.Array.Empty<byte>();
        public long    SizeBytes    => ImageBytes.LongLength;
        public void Dispose() { }
    }
}

namespace RidocImageAPITester
{
    public sealed partial class RidocImageApiClient
    {
        /// <summary>候補一覧を取得する（バイナリなし）</summary>
        public async System.Threading.Tasks.Task<DrawingImageSearchResponse?> SearchAsync(
            string docId,
            System.Threading.CancellationToken ct = default)
        {
            try
            {
                string url = $"v1/DrawingImages/search?docId={System.Uri.EscapeDataString(docId)}";
                var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content
                    .ReadFromJsonAsync<DrawingImageSearchResponse>(cancellationToken: ct);
            }
            catch { return null; }
        }

        /// <summary>インデックス指定1件取得</summary>
        public async System.Threading.Tasks.Task<FetchResult> GetImageByIndexAsync(
            string docId, string imgType, int index,
            System.Threading.CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string url = $"v1/DrawingImages?docId={System.Uri.EscapeDataString(docId)}" +
                             $"&imgType={System.Uri.EscapeDataString(imgType)}&index={index}";
                using var response = await _http.GetAsync(url, ct);
                sw.Stop();
                int status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var bytes      = await response.Content.ReadAsByteArrayAsync(ct);
                    var ct2        = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                    var fileName   = response.Headers.TryGetValues("X-Document-Name", out var vals)
                                     ? System.Linq.Enumerable.FirstOrDefault(vals) ?? $"{docId}_{imgType}_{index}"
                                     : $"{docId}_{imgType}_{index}";

                    return new FetchResult
                    {
                        IsSuccess   = true,
                        StatusCode  = status,
                        ImageBytes  = bytes,
                        ContentType = ct2,
                        FileName    = fileName,
                        ElapsedMs   = sw.ElapsedMilliseconds
                    };
                }

                ApiErrorResponse? err = null;
                try { err = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(ct); } catch { }
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
            catch (System.OperationCanceledException)
            {
                return new FetchResult { IsSuccess = false, StatusCode = 0,
                    ErrorMessage = "キャンセルされました。", ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (System.Exception ex)
            {
                return new FetchResult { IsSuccess = false, StatusCode = 0,
                    ErrorMessage = $"通信エラー: {ex.Message}", ErrorDetail = ex.ToString(),
                    ElapsedMs = sw.ElapsedMilliseconds };
            }
        }

        /// <summary>
        /// multipart/mixed レスポンスを受け取り、各パートを順次 yield する。
        /// offset, count=0 で全件取得。
        /// </summary>
        public async System.Collections.Generic.IAsyncEnumerable<MultiFetchItem> GetImagesAsync(
            string docId, string imgType, int offset = 0, int count = 0,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            System.Threading.CancellationToken ct = default)
        {
            string url = $"v1/DrawingImages?docId={System.Uri.EscapeDataString(docId)}" +
                         $"&imgType={System.Uri.EscapeDataString(imgType)}" +
                         $"&offset={offset}&count={count}";

            using var response = await _http.GetAsync(url,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) yield break;

            // multipart/mixed の boundary を取得
            string? contentType = response.Content.Headers.ContentType?.ToString();
            string? boundary    = null;
            if (contentType != null)
            {
                foreach (var part in contentType.Split(';'))
                {
                    var p = part.Trim();
                    if (p.StartsWith("boundary=", System.StringComparison.OrdinalIgnoreCase))
                    {
                        boundary = p.Substring("boundary=".Length).Trim('"');
                        break;
                    }
                }
            }
            if (boundary == null) yield break;

            // ストリームを読み込んでパースする
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
            string body   = System.Text.Encoding.UTF8.GetString(bodyBytes);

            string delimiter = "--" + boundary;
            string endMark   = "--" + boundary + "--";

            int pos = 0;
            int partIndex = 0;

            while (pos < body.Length)
            {
                int delimPos = body.IndexOf(delimiter, pos, System.StringComparison.Ordinal);
                if (delimPos < 0) break;

                pos = delimPos + delimiter.Length;
                if (pos < body.Length && body[pos] == '-') break; // 終端 --

                // ヘッダーと本文を分割（空行で区切られる）
                int headerEnd = body.IndexOf("\r\n\r\n", pos, System.StringComparison.Ordinal);
                if (headerEnd < 0) break;

                string headerSection = body.Substring(pos, headerEnd - pos);
                int    bodyStart     = headerEnd + 4;

                int nextDelim = body.IndexOf(delimiter, bodyStart, System.StringComparison.Ordinal);
                int bodyEnd   = nextDelim < 0 ? body.Length : nextDelim - 2; // -2 for \r\n

                // ヘッダー解析
                string partCt      = ExtractHeader(headerSection, "Content-Type");
                string partDisp    = ExtractHeader(headerSection, "Content-Disposition");
                string docName     = ExtractHeader(headerSection, "X-Document-Name");
                string indexStr    = ExtractHeader(headerSection, "X-Document-Index");
                int    docIndex    = int.TryParse(indexStr, out int di) ? di : partIndex;

                string fileName    = string.Empty;
                foreach (var seg in partDisp.Split(';'))
                {
                    var s = seg.Trim();
                    if (s.StartsWith("filename=", System.StringComparison.OrdinalIgnoreCase))
                        fileName = s.Substring("filename=".Length).Trim('"');
                }

                byte[] imgBytes = bodyStart < bodyEnd
                    ? System.Text.Encoding.GetEncoding("iso-8859-1")
                              .GetBytes(body.Substring(bodyStart, bodyEnd - bodyStart))
                    : System.Array.Empty<byte>();

                yield return new MultiFetchItem
                {
                    Index        = docIndex,
                    DocumentName = docName,
                    ContentType  = partCt,
                    FileName     = fileName,
                    ImageBytes   = imgBytes
                };

                pos = bodyStart;
                partIndex++;
            }
        }

        private static string ExtractHeader(string headers, string name)
        {
            foreach (var line in headers.Split("\r\n"))
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                if (string.Equals(line.Substring(0, colon).Trim(), name,
                    System.StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return string.Empty;
        }
    }
}
