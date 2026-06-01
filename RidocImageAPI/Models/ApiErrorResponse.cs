using System.Text.Json.Serialization;

namespace RidocImageAPI.Models
{
    /// <summary>
    /// API エラーレスポンスの統一モデル。
    /// 本番環境では stackTrace を含まず、開発環境のみ詳細を返す（問題⑧対策）。
    /// </summary>
    public class ApiErrorResponse
    {
        /// <summary>エラーの概要メッセージ</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>SDK エラーキー（RsnSystemException.Key の値。開発環境のみ）</summary>
        [JsonPropertyName("errorKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorKey { get; set; }

        /// <summary>スタックトレース（開発環境のみ。本番は null）</summary>
        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; set; }
    }
}
