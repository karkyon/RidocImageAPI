namespace RidocImageAPI.Models
{
    /// <summary>
    /// GET /v1/DrawingImage のクエリパラメーターモデル。
    /// 以前は未使用だったクラスをクエリバインディング用に再活用（問題⑬対策）。
    /// </summary>
    public class DrawingImageQuery
    {
        /// <summary>
        /// 検索キーワード（文書ID / 図番など）。必須。
        /// </summary>
        public string? DocId { get; set; }

        /// <summary>
        /// 取得する画像種別。"TN"（サムネイル）または "ORG"（オリジナル）。必須。
        /// </summary>
        public string? ImgType { get; set; }
    }
}
