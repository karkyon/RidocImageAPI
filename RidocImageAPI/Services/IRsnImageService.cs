using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RidocImageAPI.Models;

namespace RidocImageAPI.Services
{
    /// <summary>
    /// RSN から画像データを取得するサービスのインターフェース。
    /// </summary>
    public interface IRsnImageService
    {
        // ── 既存（互換維持・ノータッチ） ────────────────────────────────────

        /// <summary>
        /// 指定キーワードで文書を検索し、先頭1件の画像データを返す。
        /// 複数ヒットした場合は検索結果の先頭文書を使用する。
        /// </summary>
        Task<ImageResult> GetImageAsync(string docId, string imgType);

        // ── 新規 multi ────────────────────────────────────────────────────

        /// <summary>
        /// 指定キーワードにヒットする文書の候補一覧を返す（画像バイナリは含まない）。
        /// </summary>
        Task<DrawingImageSearchResponse> SearchAsync(string docId);

        /// <summary>
        /// 検索結果の指定インデックス1件の画像を返す。
        /// </summary>
        /// <param name="docId">検索キーワード</param>
        /// <param name="imgType">TN または ORG</param>
        /// <param name="index">0始まりのインデックス</param>
        Task<ImageResult> GetImageByIndexAsync(string docId, string imgType, int index);

        /// <summary>
        /// 検索結果の offset〜offset+count 件分の画像を順番に返す。
        /// count = 0 で全件取得。
        /// </summary>
        /// <param name="docId">検索キーワード</param>
        /// <param name="imgType">TN または ORG</param>
        /// <param name="offset">取得開始インデックス（0始まり）</param>
        /// <param name="count">取得件数（0 = 全件）</param>
        IAsyncEnumerable<ImageResult> GetImagesAsync(
            string docId, string imgType, int offset = 0, int count = 0);
    }

    /// <summary>
    /// 画像取得結果。バイナリ・Content-Type・ダウンロードファイル名をまとめて保持する。
    /// </summary>
    public sealed class ImageResult : System.IDisposable
    {
        public MemoryStream Stream           { get; }
        public string       ContentType      { get; }
        public string       FileName         { get; }
        public string       DocumentName     { get; }
        public string       SectionExtension { get; }
        /// <summary>検索結果内のインデックス（0始まり）</summary>
        public int          Index            { get; }

        public ImageResult(
            MemoryStream stream,
            string contentType,
            string fileName,
            string documentName,
            string sectionExtension,
            int index = 0)
        {
            Stream           = stream;
            ContentType      = contentType;
            FileName         = fileName;
            DocumentName     = documentName;
            SectionExtension = sectionExtension;
            Index            = index;
        }

        public void Dispose() => Stream?.Dispose();
    }
}
