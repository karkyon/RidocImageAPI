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

        /// <summary>
        /// キーワード検索ヒットのうち文書名照合に掛ける最大件数。
        /// キーワード検索は全文（備考等）にも当たるため、ここで取りこぼさない件数を確保する。
        /// </summary>
        public int MaxKeywordHits { get; set; } = 1000;

        /// <summary>
        /// true: 文書名が「図面番号 + 区切り文字(「(」「[」「_」)」で始まる文書も同一図面として扱う
        /// （例: キー F42186A01 → 文書名 F42186A01(2) も対象）。
        /// false: 文書名完全一致のみ。
        /// </summary>
        public bool AllowNameSuffixMatch { get; set; } = true;
    }
}
