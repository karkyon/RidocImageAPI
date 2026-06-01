namespace RidocImageAPI.Models
{
    /// <summary>
    /// 文書候補一覧の1件。/v1/DrawingImages/search のレスポンス要素。
    /// </summary>
    public sealed class DrawingImageListItem
    {
        /// <summary>RSN 内部の文書 ID</summary>
        public string Id           { get; init; } = string.Empty;

        /// <summary>文書名（図番など）</summary>
        public string Name         { get; init; } = string.Empty;

        /// <summary>セクション数</summary>
        public long   SectionCount { get; init; }

        /// <summary>セクション1の拡張子（例: ".TIF", ".dxf"）</summary>
        public string Extension    { get; init; } = string.Empty;

        /// <summary>文書全体のデータサイズ（バイト）</summary>
        public long   Size         { get; init; }

        /// <summary>
        /// 検索結果内のインデックス（0始まり）。
        /// /v1/DrawingImages?index={Index} で直接取得できる。
        /// </summary>
        public int    Index        { get; init; }
    }
}
