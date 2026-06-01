using System;
using System.IO;
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
    [ApiController]
    [Route("v1/[controller]")]
    public class DrawingImageController : ControllerBase
    {
        private readonly ILogger<DrawingImageController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IRsnImageService _rsnImageService;

        public DrawingImageController(
            ILogger<DrawingImageController> logger,
            IWebHostEnvironment environment,
            IRsnImageService rsnImageService)
        {
            _logger          = logger;
            _environment     = environment;
            _rsnImageService = rsnImageService;
        }

        /// <summary>
        /// 図面画像を取得する。
        /// imgType=TN でサムネイル(JPEG固定)、imgType=ORG でオリジナルファイル(DXF/PDF/JPEG等)を返す。
        /// ORG の Content-Type はセクションの拡張子から自動決定する。
        /// </summary>
        /// <param name="docId">検索キーワード（図番・文書名など）</param>
        /// <param name="imgType">TN（サムネイル） または ORG（オリジナル）</param>
        [HttpGet(Name = "GetDrawingImage")]
        [ProducesResponseType(typeof(byte[]),           StatusCodes.Status200OK,                 "image/jpeg")]
        [ProducesResponseType(typeof(byte[]),           StatusCodes.Status200OK,                 "image/png")]
        [ProducesResponseType(typeof(byte[]),           StatusCodes.Status200OK,                 "image/tiff")]
        [ProducesResponseType(typeof(byte[]),           StatusCodes.Status200OK,                 "application/pdf")]
        [ProducesResponseType(typeof(byte[]),           StatusCodes.Status200OK,                 "application/dxf")]
        [ProducesResponseType(typeof(byte[]),           StatusCodes.Status200OK,                 "application/octet-stream")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest,          "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized,        "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden,           "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound,            "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError, "application/json")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable,  "application/json")]
        public async Task<IActionResult> GetAsync(
            [FromQuery] string? docId,
            [FromQuery] string? imgType)
        {
            _logger.LogInformation(
                "[API] リクエスト受信: docId={DocId} imgType={ImgType} remoteIp={RemoteIp}",
                docId, imgType, HttpContext.Connection.RemoteIpAddress);

            // ── 必須チェック ──────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(docId))
            {
                _logger.LogWarning("[API] パラメーター不足: docId");
                return BadRequest(MakeError("docId は必須パラメーターです。"));
            }
            if (string.IsNullOrWhiteSpace(imgType))
            {
                _logger.LogWarning("[API] パラメーター不足: imgType");
                return BadRequest(MakeError("imgType は必須パラメーターです。（TN または ORG）"));
            }

            ImageResult? result = null;
            try
            {
                result = await _rsnImageService.GetImageAsync(docId, imgType);

                _logger.LogInformation(
                    "[API] レスポンス送信: docId={DocId} imgType={ImgType} " +
                    "contentType={ContentType} fileName={FileName} size={Size}bytes " +
                    "docName={DocName} sectionName={SectionName}",
                    docId, imgType,
                    result.ContentType, result.FileName,
                    result.Stream.Length,
                    result.DocumentName, result.SectionName);

                // Content-Disposition: inline でブラウザ表示。filename でダウンロード時の名前を提示
                Response.Headers["Content-Disposition"]
                    = $"inline; filename=\"{result.FileName}\"";

                // ORG がDXF等の非画像の場合、Swagger UIではプレビュー不可（Download になる）
                // Content-Type を動的に設定することでクライアントが正しく処理できる
                return File(result.Stream, result.ContentType);
            }

            // ── SDK 認証エラー → 401 ─────────────────────────────────────────
            catch (RsnSystemException ex) when (
                ex.Key == RsnErrorKeyConsts.AUTHENTICATION_FAULT ||
                ex.Key == RsnErrorKeyConsts.AUTHENTICATION_SERVICE_ERROR)
            {
                result?.Dispose();
                _logger.LogError(ex, "[API] RSN認証エラー: Key={Key} docId={DocId}", ex.Key, docId);
                return Unauthorized(MakeError("RSN サーバーへの認証に失敗しました。", ex));
            }

            // ── アクセス権なし → 403 ─────────────────────────────────────────
            catch (RsnSystemException ex) when (
                ex.Key == RsnErrorKeyConsts.DOCUMENT_ALL_UNAUTHORIZED)
            {
                result?.Dispose();
                _logger.LogWarning(ex, "[API] 文書アクセス権なし: Key={Key} docId={DocId}", ex.Key, docId);
                return StatusCode(StatusCodes.Status403Forbidden,
                    MakeError("この文書へのアクセス権がありません。", ex));
            }

            // ── 入力値エラー → 400 ───────────────────────────────────────────
            catch (RsnSystemException ex) when (ex.Key == RsnErrorKeyConsts.INPUT_ERROR)
            {
                result?.Dispose();
                _logger.LogWarning(ex,
                    "[API] SDK入力エラー: Key={Key} Msg={Msg} docId={DocId}",
                    ex.Key, ex.Message, docId);
                return BadRequest(MakeError($"入力値エラー: {ex.Message}", ex));
            }

            // ── セッションエラー → 503 ───────────────────────────────────────
            catch (RsnSystemException ex) when (
                ex.Key == RsnErrorKeyConsts.SESSION_TIMEOUT ||
                ex.Key == RsnErrorKeyConsts.SEQUENCE_INVALID)
            {
                result?.Dispose();
                _logger.LogError(ex,
                    "[API] SDKセッションエラー: Key={Key} docId={DocId}", ex.Key, docId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    MakeError("RSN サーバーとの接続でエラーが発生しました。再試行してください。", ex));
            }

            // ── その他SDK → 500 ──────────────────────────────────────────────
            catch (RsnSystemException ex)
            {
                result?.Dispose();
                _logger.LogError(ex,
                    "[API] SDK未分類エラー: Key={Key} docId={DocId}", ex.Key, docId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    MakeError("RSN SDK エラーが発生しました。", ex));
            }

            // ── バリデーションエラー → 400 ────────────────────────────────────
            catch (ArgumentException ex)
            {
                result?.Dispose();
                _logger.LogWarning(ex, "[API] 引数エラー: docId={DocId}", docId);
                return BadRequest(MakeError(ex.Message));
            }

            // ── 文書未発見 → 404 ─────────────────────────────────────────────
            catch (FileNotFoundException ex)
            {
                result?.Dispose();
                _logger.LogWarning(ex, "[API] 文書未発見: docId={DocId}", docId);
                return NotFound(MakeError(ex.Message));
            }

            // ── 予期しないエラー → 500 ───────────────────────────────────────
            catch (Exception ex)
            {
                result?.Dispose();
                _logger.LogError(ex, "[API] 予期しないエラー: docId={DocId}", docId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    MakeError("内部エラーが発生しました。", ex));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
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
