using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using jp.co.ricoh.ridoc.smartnavi;
using jp.co.ricoh.ridoc.smartnavi.model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RidocImageAPI.Models;

namespace RidocImageAPI.Services
{
    /// <summary>
    /// Ridoc Smart Navigator SDK を呼び出して画像データを取得するサービス。
    /// </summary>
    public class RsnImageService : IRsnImageService
    {
        // ── imgType 許容値 ────────────────────────────────────────────────────
        private static readonly HashSet<string> ValidImgTypes
            = new(StringComparer.OrdinalIgnoreCase) { "TN", "ORG" };

        // ── 拡張子 → Content-Type マッピング ─────────────────────────────────
        // ORG は DXF / PDF / TIFF / PNG 等あらゆる形式になり得る。
        // 拡張子が不明な場合は application/octet-stream を返す。
        private static readonly Dictionary<string, string> ExtToMimeMap
            = new(StringComparer.OrdinalIgnoreCase)
        {
            { ".jpg",  "image/jpeg"                },
            { ".jpeg", "image/jpeg"                },
            { ".png",  "image/png"                 },
            { ".gif",  "image/gif"                 },
            { ".bmp",  "image/bmp"                 },
            { ".tif",  "image/tiff"                },
            { ".tiff", "image/tiff"                },
            { ".webp", "image/webp"                },
            { ".pdf",  "application/pdf"           },
            { ".dxf",  "application/dxf"           },
            { ".dwg",  "application/acad"          },
            { ".svg",  "image/svg+xml"             },
            { ".xml",  "application/xml"           },
            { ".zip",  "application/zip"           },
        };

        private readonly RsnServerSettings _settings;
        private readonly ILogger<RsnImageService> _logger;

        public RsnImageService(
            IOptions<RsnServerSettings> settings,
            ILogger<RsnImageService> logger)
        {
            _settings = settings.Value;
            _logger   = logger;
        }

        /// <inheritdoc />
        public async Task<ImageResult> GetImageAsync(string docId, string imgType)
        {
            // ── バリデーション ────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(docId))
                throw new ArgumentException("docId は必須です。", nameof(docId));

            if (string.IsNullOrWhiteSpace(imgType) || !ValidImgTypes.Contains(imgType))
                throw new ArgumentException(
                    $"imgType は TN または ORG を指定してください。指定値: '{imgType}'",
                    nameof(imgType));

            return await Task.Run(() => ExecuteSdkCall(docId, imgType));
        }

        // ─────────────────────────────────────────────────────────────────────
        private ImageResult ExecuteSdkCall(string docId, string imgType)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogDebug("[RSN] 接続開始: Url={Url} docId={DocId} imgType={ImgType}",
                _settings.Url, docId, imgType);

            var rsnSystem = new RsnSystem();
            RsnSearchResultSet? searchResult = null;

            try
            {
                // ── 1. 接続 ───────────────────────────────────────────────────
                rsnSystem.Connect(_settings.Url, _settings.User, _settings.Password);
                _logger.LogDebug("[RSN] 接続成功 elapsed={Elapsed}ms", sw.ElapsedMilliseconds);

                // ── 2. 検索 ───────────────────────────────────────────────────
                var condition = new RsnSearchCondition
                {
                    documentTypeId  = string.IsNullOrWhiteSpace(_settings.DocumentTypeId)
                                      ? null : _settings.DocumentTypeId,
                    searchDocument  = true,
                    searchFolder    = false,
                    searchSubFolder = true,
                    rangeFolderId   = null,
                    keywords        = new List<string> { docId }
                };

                _logger.LogDebug("[RSN] 検索開始: keyword={DocId}", docId);
                searchResult = rsnSystem.Search(condition);

                long count = searchResult.GetDocumentCount();
                _logger.LogDebug("[RSN] 検索結果: {Count}件 elapsed={Elapsed}ms",
                    count, sw.ElapsedMilliseconds);

                if (count == 0)
                    throw new FileNotFoundException($"文書が見つかりません: {docId}");

                List<RsnDocument> docs = searchResult.GetDocumentList(0, 1);
                if (docs == null || docs.Count == 0)
                    throw new FileNotFoundException($"文書リストの取得に失敗しました: {docId}");

                RsnDocument document = docs[0];
                _logger.LogInformation(
                    "[RSN] 文書発見: id={Id} name={Name} size={Size} sections={Sections} elapsed={Elapsed}ms",
                    document.documentProperty.id,
                    document.documentProperty.name,
                    document.documentProperty.size,
                    document.documentProperty.sectionCount,
                    sw.ElapsedMilliseconds);

                // ── 3. セクション拡張子を先に取得（Content-Type 決定）────────
                //    RsnSection.extension を直接使用することで確実に拡張子が取れる
                string sectionExt    = GetSectionExtension(document, imgType);
                string contentType   = ResolveContentType(imgType, sectionExt);
                string downloadName  = BuildDownloadFileName(docId, imgType, sectionExt);

                _logger.LogDebug(
                    "[RSN] セクション情報: ext={Ext} contentType={ContentType} downloadName={DownloadName}",
                    sectionExt, contentType, downloadName);

                // ── 4. 画像データ読み込み ────────────────────────────────────
                ImageResult result = ReadImageToMemoryStream(
                    document, imgType, contentType, downloadName,
                    document.documentProperty.name, sectionExt);

                _logger.LogInformation(
                    "[RSN] 画像読み込み完了: docId={DocId} imgType={ImgType} " +
                    "contentType={ContentType} size={Size}bytes ext={Ext} elapsed={Elapsed}ms",
                    docId, imgType, contentType, result.Stream.Length, sectionExt, sw.ElapsedMilliseconds);

                return result;
            }
            catch (RsnSystemException ex)
            {
                _logger.LogError(ex,
                    "[RSN] SDKエラー: Key={Key} docId={DocId} elapsed={Elapsed}ms",
                    ex.Key, docId, sw.ElapsedMilliseconds);
                throw;
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[RSN] 予期しないエラー: docId={DocId} elapsed={Elapsed}ms",
                    docId, sw.ElapsedMilliseconds);
                throw;
            }
            finally
            {
                // ── 5. 検索結果解放（ReadSectionData の後に実施）───────────────
                //    SDKの仕様上、ReadSectionData後にDispose/Disconnectで403になる場合がある。
                //    これはSDKがReadSectionData完了後にセッションをクローズするため。
                //    403 は「既にクローズ済み」を示す想定なのでWarnのみ記録しリソースリーク扱いしない。
                if (searchResult != null)
                {
                    try
                    {
                        searchResult.Dispose();
                        _logger.LogDebug("[RSN] searchResult.Dispose() 完了");
                    }
                    catch (RsnSystemException ex) when (IsAlreadyClosedError(ex))
                    {
                        // ReadSectionData後のセッション自動クローズによる想定内403
                        _logger.LogDebug(
                            "[RSN] searchResult.Dispose() スキップ（SDK自動クローズ済み）: Key={Key}",
                            ex.Key);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[RSN] searchResult.Dispose() 失敗（予期外）");
                    }
                }

                // ── 6. 切断 ──────────────────────────────────────────────────
                try
                {
                    rsnSystem.Disconnect();
                    _logger.LogDebug("[RSN] 切断完了 totalElapsed={Elapsed}ms", sw.ElapsedMilliseconds);
                }
                catch (RsnSystemException ex) when (IsAlreadyClosedError(ex))
                {
                    // 同上：SDK自動クローズ後の想定内403
                    _logger.LogDebug(
                        "[RSN] Disconnect() スキップ（SDK自動クローズ済み）: Key={Key}",
                        ex.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RSN] Disconnect() 失敗（予期外）");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// ReadSectionData後にSDKが自動でセッションをクローズした場合の
        /// 「既にクローズ済み」エラーを判定する。
        /// ログ上は HTTPステータス=403 で現れる。
        /// </summary>
        private static bool IsAlreadyClosedError(RsnSystemException ex)
        {
            // SDK が返す 403 = リソースアクセス拒否（セッション無効化含む）
            return ex.Message != null &&
                   (ex.Message.Contains("403") ||
                    ex.Message.Contains("リソースにアクセスすることを拒否") ||
                    ex.Key == RsnErrorKeyConsts.SEQUENCE_INVALID);
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// セクション1の拡張子を取得する。
        /// RsnSection.extension プロパティを直接使用することで
        /// ファイル名のパース不要・確実に拡張子が取れる。
        /// TN（サムネイル）は常に ".jpg" 固定。
        /// </summary>
        private string GetSectionExtension(RsnDocument document, string imgType)
        {
            if (imgType.Equals("TN", StringComparison.OrdinalIgnoreCase))
                return ".jpg"; // サムネイルは常に JPEG

            try
            {
                var sections = document.GetSectionList();
                if (sections != null && sections.Count > 0)
                {
                    // RsnSection.extension は "dxf" のようにドットなしで返る場合があるため正規化する
                    string raw = sections[0].extension ?? string.Empty;
                    string ext = raw.StartsWith('.') ? raw : (raw.Length > 0 ? "." + raw : string.Empty);
                    _logger.LogDebug("[RSN] セクション1: name={Name} extension={Extension} sectionNo={SectionNo}",
                        sections[0].name, ext, sections[0].sectionNo);
                    return ext;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RSN] GetSectionList() 失敗。拡張子不明として処理");
            }

            return string.Empty; // 不明 → application/octet-stream にフォールバック
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// imgType と拡張子から Content-Type を決定する。
        /// TN は常に image/jpeg。ORG は拡張子で判断し、不明なら application/octet-stream。
        /// </summary>
        private string ResolveContentType(string imgType, string extension)
        {
            if (imgType.Equals("TN", StringComparison.OrdinalIgnoreCase))
                return "image/jpeg";

            if (!string.IsNullOrEmpty(extension) && ExtToMimeMap.TryGetValue(extension, out string? mime))
                return mime;

            _logger.LogDebug(
                "[RSN] 拡張子 '{Ext}' に対応する MIME が未定義。application/octet-stream を使用", extension);
            return "application/octet-stream";
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Content-Disposition に使用するダウンロードファイル名を組み立てる。
        /// </summary>
        private static string BuildDownloadFileName(
            string docId, string imgType, string extension)
        {
            string ext = string.IsNullOrEmpty(extension)
                ? (imgType.Equals("TN", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".bin")
                : extension;

            string safeDocId = Path.GetFileName(docId); // パストラバーサル防止
            return $"{safeDocId}_{imgType}{ext}";
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 文書の指定セクションを MemoryStream に書き込んで ImageResult を返す。
        /// </summary>
        private ImageResult ReadImageToMemoryStream(
            RsnDocument document,
            string imgType,
            string contentType,
            string downloadName,
            string documentName,
            string sectionExt,
            int index = 0)
        {
            int option = imgType.Equals("TN", StringComparison.OrdinalIgnoreCase)
                ? RsnDocument.OPTION_THUMBNAIL
                : RsnDocument.OPTION_FILE_DATA;

            _logger.LogDebug(
                "[RSN] ReadSectionData 開始: imgType={ImgType} option={Option} ext={Ext}",
                imgType, option, sectionExt);

            var ms = new MemoryStream();
            Stream stream = ms;

            RsnSection? section;
            try
            {
                section = document.ReadSectionData(1, option, ref stream);
            }
            catch (Exception ex)
            {
                ms.Dispose();
                _logger.LogError(ex,
                    "[RSN] ReadSectionData 失敗: docName={DocName} imgType={ImgType}",
                    documentName, imgType);
                throw;
            }

            if (section == null)
            {
                ms.Dispose();
                throw new InvalidOperationException(
                    $"セクションデータが空です。docName={documentName}, imgType={imgType}");
            }

            ms.Seek(0, SeekOrigin.Begin);

            _logger.LogDebug(
                "[RSN] ReadSectionData 完了: size={Size}bytes sectionNo={SectionNo} extension={Extension}",
                ms.Length, section.sectionNo, section.extension ?? "null");

            return new ImageResult(ms, contentType, downloadName, documentName, sectionExt, index);
        }   // ReadImageToMemoryStream 終わり

        // ═════════════════════════════════════════════════════════════════════
        // ── multi 用メソッド群（既存 ExecuteSdkCall はノータッチ）─────────────
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>候補一覧取得（バイナリなし）</summary>
        public async Task<DrawingImageSearchResponse> SearchAsync(string docId)
        {
            if (string.IsNullOrWhiteSpace(docId))
                throw new ArgumentException("docId は必須です。", nameof(docId));
            return await Task.Run(() => ExecuteSearch(docId));
        }

        private DrawingImageSearchResponse ExecuteSearch(string docId)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogDebug("[RSN][MULTI] 候補検索開始: docId={DocId}", docId);

            var rsnSystem = new RsnSystem();
            RsnSearchResultSet? searchResult = null;

            try
            {
                rsnSystem.Connect(_settings.Url, _settings.User, _settings.Password);
                searchResult = rsnSystem.Search(BuildCondition(docId));

                long total = searchResult.GetDocumentCount();
                _logger.LogDebug("[RSN][MULTI] 検索結果: {Total}件 elapsed={Elapsed}ms",
                    total, sw.ElapsedMilliseconds);

                var candidates = new List<DrawingImageListItem>();
                if (total > 0)
                {
                    var docs = searchResult.GetDocumentList(0, (int)total);
                    for (int i = 0; i < docs.Count; i++)
                    {
                        var doc = docs[i];
                        string ext = string.Empty;
                        try
                        {
                            var sections = doc.GetSectionList();
                            if (sections != null && sections.Count > 0)
                            {
                                string raw = sections[0].extension ?? string.Empty;
                                ext = raw.StartsWith('.') ? raw : (raw.Length > 0 ? "." + raw : string.Empty);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[RSN][MULTI] GetSectionList失敗: index={Index}", i);
                        }
                        candidates.Add(new DrawingImageListItem
                        {
                            Id           = doc.documentProperty.id   ?? string.Empty,
                            Name         = doc.documentProperty.name  ?? string.Empty,
                            SectionCount = doc.documentProperty.sectionCount,
                            Extension    = ext,
                            Size         = doc.documentProperty.size,
                            Index        = i
                        });
                    }
                }

                _logger.LogInformation(
                    "[RSN][MULTI] 候補取得完了: docId={DocId} total={Total} elapsed={Elapsed}ms",
                    docId, total, sw.ElapsedMilliseconds);

                return new DrawingImageSearchResponse
                {
                    DocId      = docId,
                    TotalCount = total,
                    Candidates = candidates
                };
            }
            catch (RsnSystemException ex)
            {
                _logger.LogError(ex, "[RSN][MULTI] SDKエラー: Key={Key} docId={DocId}", ex.Key, docId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSN][MULTI] 予期しないエラー: docId={DocId}", docId);
                throw;
            }
            finally
            {
                DisposeSafely(searchResult);
                DisconnectSafely(rsnSystem);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>インデックス指定1件取得</summary>
        public async Task<ImageResult> GetImageByIndexAsync(
            string docId, string imgType, int index)
        {
            if (string.IsNullOrWhiteSpace(docId))
                throw new ArgumentException("docId は必須です。", nameof(docId));
            if (string.IsNullOrWhiteSpace(imgType) || !ValidImgTypes.Contains(imgType))
                throw new ArgumentException("imgType は TN または ORG を指定してください。", nameof(imgType));
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(index));

            return await Task.Run(() => ExecuteSdkCallByIndex(docId, imgType, index));
        }

        private ImageResult ExecuteSdkCallByIndex(string docId, string imgType, int index)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogDebug("[RSN][MULTI] インデックス指定取得: docId={DocId} imgType={ImgType} index={Index}",
                docId, imgType, index);

            var rsnSystem = new RsnSystem();
            RsnSearchResultSet? searchResult = null;

            try
            {
                rsnSystem.Connect(_settings.Url, _settings.User, _settings.Password);
                searchResult = rsnSystem.Search(BuildCondition(docId));

                long total = searchResult.GetDocumentCount();
                if (total == 0)
                    throw new FileNotFoundException($"文書が見つかりません: {docId}");
                if (index >= total)
                    throw new ArgumentOutOfRangeException(nameof(index),
                        $"index={index} は範囲外です。総件数: {total}");

                var docs = searchResult.GetDocumentList(index, 1);
                if (docs == null || docs.Count == 0)
                    throw new FileNotFoundException($"文書リストの取得に失敗しました: docId={docId} index={index}");

                var document = docs[0];
                _logger.LogInformation(
                    "[RSN][MULTI] 文書発見: id={Id} name={Name} index={Index}/{Total} elapsed={Elapsed}ms",
                    document.documentProperty.id, document.documentProperty.name,
                    index, total, sw.ElapsedMilliseconds);

                string sectionExt   = GetSectionExtension(document, imgType);
                string contentType  = ResolveContentType(imgType, sectionExt);
                // ダウンロードファイル名は実際の文書名から生成（docId はキーワードのため）
                string docName      = document.documentProperty.name ?? docId;
                string downloadName = BuildDownloadFileName(docName, imgType, sectionExt);

                var result = ReadImageToMemoryStream(
                    document, imgType, contentType, downloadName, docName, sectionExt, index);

                _logger.LogInformation(
                    "[RSN][MULTI] 画像取得完了: docId={DocId} index={Index} imgType={ImgType} " +
                    "contentType={ContentType} size={Size}bytes elapsed={Elapsed}ms",
                    docId, index, imgType, contentType, result.Stream.Length, sw.ElapsedMilliseconds);

                return result;
            }
            catch (RsnSystemException ex)
            {
                _logger.LogError(ex, "[RSN][MULTI] SDKエラー: Key={Key}", ex.Key);
                throw;
            }
            catch (FileNotFoundException) { throw; }
            catch (ArgumentOutOfRangeException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RSN][MULTI] 予期しないエラー: docId={DocId}", docId);
                throw;
            }
            finally
            {
                DisposeSafely(searchResult);
                DisconnectSafely(rsnSystem);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// offset〜offset+count件分を非同期ストリームで返す（count=0で全件）。
        /// SDK は1接続1操作のため1件ずつ独立したセッションで取得する。
        /// </summary>
        public async IAsyncEnumerable<ImageResult> GetImagesAsync(
            string docId, string imgType, int offset = 0, int count = 0)
        {
            if (string.IsNullOrWhiteSpace(docId))
                throw new ArgumentException("docId は必須です。", nameof(docId));
            if (string.IsNullOrWhiteSpace(imgType) || !ValidImgTypes.Contains(imgType))
                throw new ArgumentException("imgType は TN または ORG を指定してください。", nameof(imgType));
            ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(offset));
            ArgumentOutOfRangeException.ThrowIfNegative(count,  nameof(count));

            var search = await SearchAsync(docId);
            long total = search.TotalCount;
            if (total == 0) yield break;

            int end = count == 0
                ? (int)total
                : Math.Min(offset + count, (int)total);

            for (int i = offset; i < end; i++)
            {
                yield return await Task.Run(() => ExecuteSdkCallByIndex(docId, imgType, i));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // multi 用共通ヘルパー
        // ─────────────────────────────────────────────────────────────────────

        private RsnSearchCondition BuildCondition(string docId) =>
            new RsnSearchCondition
            {
                documentTypeId  = string.IsNullOrWhiteSpace(_settings.DocumentTypeId)
                                  ? null : _settings.DocumentTypeId,
                searchDocument  = true,
                searchFolder    = false,
                searchSubFolder = true,
                rangeFolderId   = null,
                keywords        = new List<string> { docId }
            };

        private void DisposeSafely(RsnSearchResultSet? searchResult)
        {
            if (searchResult == null) return;
            try { searchResult.Dispose(); }
            catch (RsnSystemException ex) when (IsAlreadyClosedError(ex))
            { _logger.LogDebug("[RSN] Dispose() スキップ（SDK自動クローズ済み）: Key={Key}", ex.Key); }
            catch (Exception ex)
            { _logger.LogWarning(ex, "[RSN] Dispose() 失敗（予期外）"); }
        }

        private void DisconnectSafely(RsnSystem rsnSystem)
        {
            try { rsnSystem.Disconnect(); }
            catch (RsnSystemException ex) when (IsAlreadyClosedError(ex))
            { _logger.LogDebug("[RSN] Disconnect() スキップ（SDK自動クローズ済み）: Key={Key}", ex.Key); }
            catch (Exception ex)
            { _logger.LogWarning(ex, "[RSN] Disconnect() 失敗（予期外）"); }
        }
    }       // RsnImageService クラス終わり
}
