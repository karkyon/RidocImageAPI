using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;
using RidocImageAPI.Models;

namespace RidocImageAPI.Controllers
{
    /// <summary>
    /// レスポンスヘッダー用の共通処理。
    /// HTTP ヘッダーには ASCII しか入れられない（Kestrel が非ASCII文字で例外を出す）ため、
    /// 日本語を含む文書名・ファイル名はここで変換してから設定する。
    /// </summary>
    internal static class HttpHeaderHelper
    {
        /// <summary>
        /// Content-Disposition: inline を RFC 6266 形式で組み立てる。
        /// filename には ASCII 代替名、filename* に UTF-8 の正式名が入る。
        /// </summary>
        public static string InlineDisposition(string fileName)
        {
            var cd = new ContentDispositionHeaderValue("inline");
            cd.SetHttpFileName(string.IsNullOrEmpty(fileName) ? "file" : fileName);
            return cd.ToString();
        }

        /// <summary>X-Document-Name 等の任意文字列ヘッダー値（UTF-8 を URL エンコード）</summary>
        public static string EncodeValue(string? value) => Uri.EscapeDataString(value ?? string.Empty);
    }

    /// <summary>
    /// エラー応答(ApiErrorResponse)を常に application/json で返す。
    /// コントローラーの [Produces("image/jpeg", ...)] が ObjectResult の出力形式を画像に限定するため、
    /// これが無いとエラー時に JSON を出力できず 406 Not Acceptable（本文なし）になっていた。
    /// Produces より後に実行させるため Order を最大にしている。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ApiErrorJsonAttribute : Attribute, IAlwaysRunResultFilter, IOrderedFilter
    {
        public int Order => int.MaxValue;

        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is ObjectResult { Value: ApiErrorResponse } result)
            {
                result.ContentTypes.Clear();
                result.ContentTypes.Add("application/json");
            }
        }

        public void OnResultExecuted(ResultExecutedContext context) { }
    }
}
