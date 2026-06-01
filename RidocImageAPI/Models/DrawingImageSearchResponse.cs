using System.Collections.Generic;

namespace RidocImageAPI.Models
{
    /// <summary>
    /// /v1/DrawingImages/search のレスポンス全体。
    /// </summary>
    public sealed class DrawingImageSearchResponse
    {
        /// <summary>検索キーワード（そのまま返す）</summary>
        public string DocId      { get; init; } = string.Empty;

        /// <summary>総ヒット件数（candidates.Count と同値だが明示的に返す）</summary>
        public long   TotalCount { get; init; }

        /// <summary>候補一覧</summary>
        public List<DrawingImageListItem> Candidates { get; init; } = new();
    }
}
