namespace RidocImageAPI.Models
{
    /// <summary>
    /// appsettings.json の "RsnServer" セクションにバインドされる設定クラス。
    /// 認証情報・文書タイプIDなどをハードコードから分離する（問題①⑦対策）。
    /// </summary>
    public class RsnServerSettings
    {
        public const string SectionName = "RsnServer";

        /// <summary>Ridoc Smart Navigator サーバーの URL（例: http://192.168.1.5:8080/rsn/）</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>接続ユーザー名</summary>
        public string User { get; set; } = string.Empty;

        /// <summary>接続パスワード</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>検索対象の文書タイプID（空の場合は全文書タイプが対象）</summary>
        public string? DocumentTypeId { get; set; }
    }
}
