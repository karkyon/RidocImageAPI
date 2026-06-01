using System.IO;
using System.Threading.Tasks;

namespace RidocImageAPI.Services
{
    /// <summary>
    /// RSN から画像データを取得するサービスのインターフェース。
    /// </summary>
    public interface IRsnImageService
    {
        /// <summary>
        /// 指定キーワードで文書を検索し、画像データを ImageResult で返す。
        /// </summary>
        /// <param name="docId">検索キーワード（図番・文書名など）</param>
        /// <param name="imgType">"TN"（サムネイル）または "ORG"（オリジナル）</param>
        /// <returns>画像バイナリ・Content-Type・ファイル名を含む ImageResult</returns>
        Task<ImageResult> GetImageAsync(string docId, string imgType);
    }

    /// <summary>
    /// 画像取得結果。バイナリ・Content-Type・ダウンロードファイル名をまとめて保持する。
    /// ORG はJPEG以外（DXF, PDF等）になり得るため Content-Type を動的に決定する。
    /// </summary>
    public sealed class ImageResult : System.IDisposable
    {
        /// <summary>画像バイナリ（先頭にシーク済み）</summary>
        public MemoryStream Stream { get; }

        /// <summary>Content-Type（例: image/jpeg, image/png, application/pdf, application/octet-stream）</summary>
        public string ContentType { get; }

        /// <summary>Content-Disposition に使用するファイル名（拡張子付き）</summary>
        public string FileName { get; }

        /// <summary>文書名（RSN 上の名前）</summary>
        public string DocumentName { get; }

        /// <summary>セクション名（拡張子を含む）</summary>
        public string SectionName { get; }

        public ImageResult(
            MemoryStream stream,
            string contentType,
            string fileName,
            string documentName,
            string sectionName)
        {
            Stream       = stream;
            ContentType  = contentType;
            FileName     = fileName;
            DocumentName = documentName;
            SectionName  = sectionName;
        }

        public void Dispose() => Stream?.Dispose();
    }
}
