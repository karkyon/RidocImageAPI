using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using jp.co.ricoh.ridoc.smartnavi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RidocImageAPI.Models;
using RidocImageAPI.Services;

namespace RidocImageAPI.Controllers
{
    /// <summary>
    /// 複数文書対応エンドポイント群。
    /// 既存の /v1/DrawingImage（単体）はノータッチで互換維持。
    /// </summary>
    [ApiController]
    [Route("v1/[controller]")]
    public class DrawingImagesController : ControllerBase
    {
        private readonly ILogger<DrawingImagesController> _logger;
        private readonly IWebHostEnvironment              _environment;
        private readonly IRsnImageService                 _rsnImageService;

        public DrawingImagesController(
            ILogger<DrawingImagesController> logger,
            IWebHostEnvironment environment,
            IRsnImageService rsnImageService)
        {
            _logger          = logger;
            _environment     = environment;
            _rsnImageService = rsnImageService;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 候補一覧取得
        // GET /v1/DrawingImages/search?docId=AU85-00
        // → JSON: { totalCount, candidates: [{id, name, extension, size, index}...] }
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 検索キーワードにヒットする文書の候補一覧を返す。
        /// 画像バイナリは含まない。取得したい文書の index を確認してから
        /// 他のエンドポイントで画像を取得する用途を想定。
        /// </summary>
        [HttpGet("search")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(DrawingImageSearchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse),           StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse),           StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiErrorResponse),           StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ApiErrorResponse),           StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> SearchAsync([FromQuery] string? docId)
        {
            _logger.LogInformation("[API][MULTI] 候補検索: docId={DocId} remoteIp={RemoteIp}",
                docId, HttpContext.Connection.RemoteIpAddress);

            if (string.IsNullOrWhiteSpace(docId))
                return BadRequest(MakeError("docId は必須パラメーターです。"));

            try
            {
                var response = await _rsnImageService.SearchAsync(docId);
                return Ok(response);
            }
            catch (RsnSystemException ex) when (ex.Key == RsnErrorKeyConsts.AUTHENTICATION_FAULT ||
                                                 ex.Key == RsnErrorKeyConsts.AUTHENTICATION_SERVICE_ERROR)
            {
                _logger.LogError(ex, "[API][MULTI] 認証エラー: Key={Key}", ex.Key);
                return Unauthorized(MakeError("RSN サーバーへの認証に失敗しました。", ex));
            }
            catch (RsnSystemException ex) when (ex.Key == RsnErrorKeyConsts.SESSION_TIMEOUT ||
                                                 ex.Key == RsnErrorKeyConsts.SEQUENCE_INVALID)
            {
                _logger.LogError(ex, "[API][MULTI] セッションエラー: Key={Key}", ex.Key);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    MakeError("RSN サーバーとの接続でエラーが発生しました。", ex));
            }
            catch (RsnSystemException ex)
            {
                _logger.LogError(ex, "[API][MULTI] SDKエラー: Key={Key}", ex.Key);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    MakeError("RSN SDK エラーが発生しました。", ex));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(MakeError(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(MakeError(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API][MULTI] 予期しないエラー: docId={DocId}", docId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    MakeError("内部エラーが発生しました。", ex));
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② インデックス指定1件取得
        // GET /v1/DrawingImages?docId=AU85-00&imgType=TN&index=2
        // → Content-Type: image/xxx（単体バイナリ）
        //
        // ③ ページング取得（multipart/mixed）
        // GET /v1/DrawingImages?docId=AU85-00&imgType=TN&offset=0&count=5
        // → multipart/mixed で指定範囲の全バイナリ
        //
        // ④ 全件取得（multipart/mixed）
        // GET /v1/DrawingImages?docId=AU85-00&imgType=TN
        // → multipart/mixed で全件バイナリ（count 省略 = 全件）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 複数図面画像を取得する。
        ///
        /// - index 指定時: 単体バイナリ（Content-Type: image/xxx）
        /// - index 未指定時: multipart/mixed で複数バイナリを返す
        ///   - offset, count でページング可能（count=0 で全件）
        /// </summary>
        [HttpGet]
        [Produces("image/jpeg", "image/tiff", "image/png", "application/pdf",
                  "application/dxf", "application/octet-stream", "multipart/mixed")]
        [ProducesResponseType(typeof(byte[]),           StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest,  "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound,    "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status416RangeNotSatisfiable, "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError, "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable,  "application/json")]
        public async Task<IActionResult> GetAsync(
            [FromQuery] string? docId,
            [FromQuery] string? imgType,
            [FromQuery] int?    index  = null,
            [FromQuery] int     offset = 0,
            [FromQuery] int     count  = 0)
        {
            _logger.LogInformation(
                "[API][MULTI] 画像取得: docId={DocId} imgType={ImgType} " +
                "index={Index} offset={Offset} count={Count} remoteIp={RemoteIp}",
                docId, imgType, index, offset, count,
                HttpContext.Connection.RemoteIpAddress);

            if (string.IsNullOrWhiteSpace(docId))
                return BadRequest(MakeError("docId は必須パラメーターです。"));
            if (string.IsNullOrWhiteSpace(imgType))
                return BadRequest(MakeError("imgType は必須パラメーターです。（TN または ORG）"));
            if (offset < 0)
                return BadRequest(MakeError("offset は 0 以上を指定してください。"));
            if (count < 0)
                return BadRequest(MakeError("count は 0 以上を指定してください。"));

            try
            {
                // ── index 指定あり → 単体バイナリ返却 ────────────────────────
                if (index.HasValue)
                {
                    var result = await _rsnImageService.GetImageByIndexAsync(
                        docId, imgType, index.Value);

                    _logger.LogInformation(
                        "[API][MULTI] 単体レスポンス: docId={DocId} index={Index} " +
                        "contentType={ContentType} size={Size}bytes",
                        docId, index.Value, result.ContentType, result.Stream.Length);

                    Response.Headers["Content-Disposition"] =
                        $"inline; filename=\"{result.FileName}\"";
                    Response.Headers["X-Document-Name"]  = result.DocumentName;
                    Response.Headers["X-Document-Index"] = index.Value.ToString();

                    return File(result.Stream, result.ContentType);
                }

                // ── index 未指定 → multipart/mixed で複数返却 ─────────────────
                string boundary = $"ridoc-image-{Guid.NewGuid():N}";
                Response.ContentType = $"multipart/mixed; boundary={boundary}";
                Response.StatusCode  = StatusCodes.Status200OK;

                int partCount = 0;
                await foreach (var item in _rsnImageService.GetImagesAsync(
                    docId, imgType, offset, count))
                {
                    byte[] imageBytes = item.Stream.ToArray();

                    // multipart ヘッダー
                    string partHeader =
                        $"\r\n--{boundary}\r\n" +
                        $"Content-Type: {item.ContentType}\r\n" +
                        $"Content-Disposition: inline; filename=\"{item.FileName}\"\r\n" +
                        $"X-Document-Name: {item.DocumentName}\r\n" +
                        $"X-Document-Index: {item.Index}\r\n" +
                        $"Content-Length: {imageBytes.Length}\r\n" +
                        $"\r\n";

                    byte[] headerBytes = Encoding.UTF8.GetBytes(partHeader);
                    await Response.Body.WriteAsync(headerBytes);
                    await Response.Body.WriteAsync(imageBytes);
                    item.Dispose();
                    partCount++;

                    _logger.LogDebug(
                        "[API][MULTI] パート送信: index={Index} file={File} size={Size}bytes",
                        item.Index, item.FileName, imageBytes.Length);
                }

                // 終端バウンダリ
                byte[] epilogue = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
                await Response.Body.WriteAsync(epilogue);

                _logger.LogInformation(
                    "[API][MULTI] multipart完了: docId={DocId} imgType={ImgType} " +
                    "parts={Parts} offset={Offset} count={Count}",
                    docId, imgType, partCount, offset, count);

                return new EmptyResult(); // StatusCode は既に 200 を設定済み
            }
            catch (RsnSystemException ex) when (ex.Key == RsnErrorKeyConsts.AUTHENTICATION_FAULT ||
                                                 ex.Key == RsnErrorKeyConsts.AUTHENTICATION_SERVICE_ERROR)
            {
                _logger.LogError(ex, "[API][MULTI] 認証エラー: Key={Key}", ex.Key);
                return Unauthorized(MakeError("RSN サーバーへの認証に失敗しました。", ex));
            }
            catch (RsnSystemException ex) when (ex.Key == RsnErrorKeyConsts.SESSION_TIMEOUT ||
                                                 ex.Key == RsnErrorKeyConsts.SEQUENCE_INVALID)
            {
                _logger.LogError(ex, "[API][MULTI] セッションエラー: Key={Key}", ex.Key);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    MakeError("RSN サーバーとの接続でエラーが発生しました。", ex));
            }
            catch (RsnSystemException ex)
            {
                _logger.LogError(ex, "[API][MULTI] SDKエラー: Key={Key}", ex.Key);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    MakeError("RSN SDK エラーが発生しました。", ex));
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(MakeError(ex.Message));
            }
            // ArgumentOutOfRangeException は ArgumentException のサブクラスのため先に catch する
            catch (ArgumentOutOfRangeException ex)
            {
                return StatusCode(StatusCodes.Status416RangeNotSatisfiable,
                    MakeError(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(MakeError(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[API][MULTI] 予期しないエラー: docId={DocId}", docId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    MakeError("内部エラーが発生しました。", ex));
            }
        }

        private ApiErrorResponse MakeError(string message, Exception? ex = null)
        {
            bool isDev = _environment.IsDevelopment();
            return new ApiErrorResponse
            {
                Message  = message,
                ErrorKey = isDev && ex is RsnSystemException rsnEx ? rsnEx.Key : null,
                Detail   = isDev ? ex?.ToString() : null
            };
        }
    }
}
