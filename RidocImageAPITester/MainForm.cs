using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RidocImageAPITester
{
    public partial class MainForm : Form
    {
        // ── フィールド ──────────────────────────────────────────────────────
        private RidocImageApiClient? _client;
        private CancellationTokenSource? _cts;

        // 最後に取得した結果（保存・外部アプリ起動用）
        private byte[]? _lastImageBytes;
        private string  _lastFileName    = string.Empty;
        private string  _lastContentType = string.Empty;

        // 元画像サイズ（ズーム計算用）
        private Size    _originalImageSize = Size.Empty;

        // 現在のズーム倍率（1.0 = 100%）
        private double  _zoomScale = 1.0;
        private const double ZoomStep = 0.25;
        private const double ZoomMin  = 0.1;
        private const double ZoomMax  = 8.0;

        // 履歴（docId + imgType → 結果サマリー）
        private readonly List<HistoryItem> _history = new();

        // ── コンストラクター ────────────────────────────────────────────────
        public MainForm()
        {
            InitializeComponent();
            LoadSettings();
            // SplitterDistance はフォームサイズ確定後（Load 時）に設定する。
            // コンストラクター内で設定すると幅未確定で InvalidOperationException が発生する。
            Load += MainForm_Load;
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            // SplitContainer の MinSize / SplitterDistance を安全に設定する。
            //
            // 問題の背景:
            //   SplitContainer は SplitterDistance を変更する際に内部で
            //   「Panel1MinSize <= SplitterDistance <= Width - Panel2MinSize」
            //   を検証する。Load イベント時点でも Width が小さい場合がある。
            //
            // 解決策:
            //   ① SplitterDistance を 1 にリセット（MinSize < Width を保証）
            //   ② MinSize を設定
            //   ③ Width と MinSize の合計を考慮した安全な SplitterDistance を設定

            int mainW = splitMain.Width;
            int mainP1 = 300;
            int mainP2 = 200;
            // Width が MinSize の合計以下の場合は MinSize を縮小する
            if (mainW <= mainP1 + mainP2)
            {
                mainP1 = mainW * 6 / 10;
                mainP2 = mainW - mainP1 - 6; // スプリッターの幅 6px 分を引く
            }
            splitMain.SplitterDistance  = 1;
            splitMain.Panel1MinSize     = mainP1;
            splitMain.Panel2MinSize     = mainP2;
            splitMain.SplitterDistance  = Math.Max(mainP1, mainW * 6 / 10);

            int rightH = splitRight.Height;
            int rightP1 = 100;
            int rightP2 = 100;
            if (rightH <= rightP1 + rightP2)
            {
                rightP1 = rightH * 4 / 10;
                rightP2 = rightH - rightP1 - 4;
            }
            splitRight.SplitterDistance = 1;
            splitRight.Panel1MinSize    = rightP1;
            splitRight.Panel2MinSize    = rightP2;
            splitRight.SplitterDistance = Math.Max(rightP1, rightH * 4 / 10);
        }

        // ════════════════════════════════════════════════════════════════════
        // 設定の保存・読込
        // ════════════════════════════════════════════════════════════════════
        private void LoadSettings()
        {
            txtBaseUrl.Text = Properties.Settings.Default.BaseUrl.Length > 0
                ? Properties.Settings.Default.BaseUrl
                : "https://localhost:5088";
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.BaseUrl = txtBaseUrl.Text.Trim();
            Properties.Settings.Default.Save();
        }

        // ════════════════════════════════════════════════════════════════════
        // クライアント生成
        // ════════════════════════════════════════════════════════════════════
        private RidocImageApiClient GetClient()
        {
            string url = txtBaseUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("ベース URL を入力してください。");

            // URL が変わったら再生成
            if (_client == null || _client.ToString() != url)
            {
                _client?.Dispose();
                _client = new RidocImageApiClient(url);
            }
            return _client;
        }

        // ════════════════════════════════════════════════════════════════════
        // 送信ボタン
        // ════════════════════════════════════════════════════════════════════
        private async void btnFetch_Click(object sender, EventArgs e)
        {
            string docId   = txtDocId.Text.Trim();
            string imgType = cmbImgType.Text.Trim().ToUpperInvariant();

            // ── バリデーション ────────────────────────────────────────────
            if (string.IsNullOrEmpty(docId))
            {
                ShowError("docId を入力してください。");
                txtDocId.Focus();
                return;
            }
            if (imgType != "TN" && imgType != "ORG")
            {
                ShowError("imgType は TN または ORG を選択してください。");
                cmbImgType.Focus();
                return;
            }

            SaveSettings();
            SetBusy(true);
            ClearResult();

            _cts = new CancellationTokenSource();

            try
            {
                var client = GetClient();
                var result = await client.GetImageAsync(docId, imgType, _cts.Token);
                HandleResult(docId, imgType, result);
            }
            catch (Exception ex)
            {
                ShowError($"予期しないエラー:\n{ex.Message}");
            }
            finally
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ── キャンセルボタン ─────────────────────────────────────────────
        private void btnCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
        }

        // ════════════════════════════════════════════════════════════════════
        // 結果処理
        // ════════════════════════════════════════════════════════════════════
        private void HandleResult(string docId, string imgType, FetchResult result)
        {
            // 履歴に追加
            var hi = new HistoryItem(docId, imgType, result.StatusCode, result.ElapsedMs,
                                     result.ContentType ?? "", result.SizeBytes, result.IsSuccess);
            _history.Insert(0, hi);
            RefreshHistory();

            if (!result.IsSuccess)
            {
                // エラー表示
                lblStatus.Text      = $"❌ HTTP {result.StatusCode}";
                lblStatus.ForeColor = Color.Crimson;
                txtLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] NG  docId={docId} imgType={imgType} " +
                                  $"status={result.StatusCode} elapsed={result.ElapsedMs}ms");
                txtLog.AppendLine($"  message  : {result.ErrorMessage}");
                if (!string.IsNullOrEmpty(result.ErrorKey))
                    txtLog.AppendLine($"  errorKey : {result.ErrorKey}");
                if (!string.IsNullOrEmpty(result.ErrorDetail))
                    txtLog.AppendLine($"  detail   :\n{result.ErrorDetail}");

                ShowErrorPanel(result);
                return;
            }

            // 成功
            _lastImageBytes  = result.ImageBytes;
            _lastFileName    = result.FileName ?? $"{docId}_{imgType}";
            _lastContentType = result.ContentType ?? "application/octet-stream";

            lblStatus.Text      = $"✅ HTTP {result.StatusCode}  {result.SizeBytes / 1024.0:F1} KB  {result.ElapsedMs} ms";
            lblStatus.ForeColor = Color.DarkGreen;

            txtLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] OK  docId={docId} imgType={imgType} " +
                              $"contentType={result.ContentType} size={result.SizeBytes}bytes " +
                              $"file={result.FileName} elapsed={result.ElapsedMs}ms");

            // 画像プレビュー
            ShowImagePreview(result.ImageBytes!, result.ContentType ?? "");

            // 操作ボタン有効化
            btnSave.Enabled    = true;
            btnOpenApp.Enabled = true;
        }

        // ════════════════════════════════════════════════════════════════════
        // 画像プレビュー
        // ════════════════════════════════════════════════════════════════════
        private void ShowImagePreview(byte[] bytes, string contentType)
        {
            picPreview.Image?.Dispose();
            picPreview.Image    = null;
            _originalImageSize  = Size.Empty;
            lblPreviewInfo.Text = string.Empty;
            pnlNoPreview.Visible = false;
            pnlScroll.Visible    = false;

            bool isDisplayable = contentType.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase)
                              || contentType.StartsWith("image/png",  StringComparison.OrdinalIgnoreCase)
                              || contentType.StartsWith("image/bmp",  StringComparison.OrdinalIgnoreCase)
                              || contentType.StartsWith("image/gif",  StringComparison.OrdinalIgnoreCase)
                              || contentType.StartsWith("image/tiff", StringComparison.OrdinalIgnoreCase);

            if (isDisplayable)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    var img = Image.FromStream(ms);
                    _originalImageSize = img.Size;
                    picPreview.Image   = img;

                    // 初回は Fit（パネルに収まるスケール）で表示
                    SetZoomFit();

                    pnlScroll.Visible = true;
                    lblPreviewInfo.Text =
                        $"{img.Width} × {img.Height} px  |  {contentType}  |  {bytes.Length / 1024.0:F1} KB";
                }
                catch
                {
                    ShowNoPreview($"画像の読み込みに失敗しました。\n({contentType})");
                }
            }
            else
            {
                ShowNoPreview($"このファイル形式はプレビューできません。\n({contentType})\n\n「外部アプリで開く」ボタンを使用してください。");
            }
        }

        // ── ズーム処理 ────────────────────────────────────────────────────
        /// <summary>現在の _zoomScale を PictureBox のサイズに反映する</summary>
        private void ApplyZoom()
        {
            if (_originalImageSize.IsEmpty) return;

            int w = Math.Max(1, (int)(_originalImageSize.Width  * _zoomScale));
            int h = Math.Max(1, (int)(_originalImageSize.Height * _zoomScale));
            picPreview.Size = new Size(w, h);

            int pct = (int)Math.Round(_zoomScale * 100);
            lblZoom.Text = $"{pct}%";
        }

        /// <summary>パネルに収まる最大スケールに設定する（Fit）</summary>
        private void SetZoomFit()
        {
            if (_originalImageSize.IsEmpty) return;

            double scaleW = (double)pnlScroll.ClientSize.Width  / _originalImageSize.Width;
            double scaleH = (double)pnlScroll.ClientSize.Height / _originalImageSize.Height;
            _zoomScale = Math.Min(scaleW, scaleH);
            _zoomScale = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoomScale));
            ApplyZoom();
        }

        private void btnZoomFit_Click(object sender, EventArgs e) => SetZoomFit();

        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            _zoomScale = Math.Min(ZoomMax, _zoomScale + ZoomStep);
            ApplyZoom();
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            _zoomScale = Math.Max(ZoomMin, _zoomScale - ZoomStep);
            ApplyZoom();
        }

        private void ShowNoPreview(string message)
        {
            picPreview.Image     = null;
            pnlScroll.Visible    = false;
            pnlNoPreview.Visible = true;
            lblNoPreview.Text    = message;
        }

        private void ShowErrorPanel(FetchResult result)
        {
            picPreview.Image     = null;
            pnlScroll.Visible    = false;
            pnlNoPreview.Visible = true;
            var sb = new StringBuilder();
            sb.AppendLine($"エラー: HTTP {result.StatusCode}");
            sb.AppendLine(result.ErrorMessage);
            if (!string.IsNullOrEmpty(result.ErrorKey))
                sb.AppendLine($"errorKey: {result.ErrorKey}");
            lblNoPreview.Text = sb.ToString();
        }

        // ════════════════════════════════════════════════════════════════════
        // 保存 / 外部アプリで開く
        // ════════════════════════════════════════════════════════════════════
        private void btnSave_Click(object sender, EventArgs e)
        {
            if (_lastImageBytes == null) return;

            using var dlg = new SaveFileDialog
            {
                Title    = "ファイルを保存",
                FileName = _lastFileName,
                Filter   = BuildSaveFilter(_lastContentType)
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    File.WriteAllBytes(dlg.FileName, _lastImageBytes);
                    txtLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] 保存完了: {dlg.FileName}");
                    MessageBox.Show($"保存しました:\n{dlg.FileName}", "保存完了",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    ShowError($"保存に失敗しました:\n{ex.Message}");
                }
            }
        }

        private void btnOpenApp_Click(object sender, EventArgs e)
        {
            if (_lastImageBytes == null) return;
            try
            {
                string path = RidocImageApiClient.SaveAndOpen(_lastImageBytes, _lastFileName);
                txtLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] 外部アプリで開く: {path}");
            }
            catch (Exception ex)
            {
                ShowError($"外部アプリの起動に失敗しました:\n{ex.Message}");
            }
        }

        private static string BuildSaveFilter(string contentType)
        {
            return contentType switch
            {
                var ct when ct.Contains("jpeg") => "JPEG ファイル|*.jpg;*.jpeg|全ファイル|*.*",
                var ct when ct.Contains("tiff") => "TIFF ファイル|*.tif;*.tiff|全ファイル|*.*",
                var ct when ct.Contains("png")  => "PNG ファイル|*.png|全ファイル|*.*",
                var ct when ct.Contains("pdf")  => "PDF ファイル|*.pdf|全ファイル|*.*",
                var ct when ct.Contains("dxf")  => "DXF ファイル|*.dxf|全ファイル|*.*",
                _                               => "全ファイル|*.*"
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // 履歴
        // ════════════════════════════════════════════════════════════════════
        private void RefreshHistory()
        {
            lstHistory.BeginUpdate();
            lstHistory.Items.Clear();
            foreach (var h in _history.Take(50))
            {
                var item = new ListViewItem(h.DocId);
                item.SubItems.Add(h.ImgType);
                item.SubItems.Add(h.StatusCode.ToString());
                item.SubItems.Add($"{h.SizeBytes / 1024.0:F1} KB");
                item.SubItems.Add($"{h.ElapsedMs} ms");
                item.SubItems.Add(h.ContentType);
                item.ForeColor = h.IsSuccess ? Color.DarkGreen : Color.Crimson;
                item.Tag       = h;
                lstHistory.Items.Add(item);
            }
            lstHistory.EndUpdate();
        }

        // 履歴をダブルクリック → docId を入力欄に反映
        private void lstHistory_DoubleClick(object sender, EventArgs e)
        {
            if (lstHistory.SelectedItems.Count == 0) return;
            if (lstHistory.SelectedItems[0].Tag is HistoryItem h)
            {
                txtDocId.Text    = h.DocId;
                cmbImgType.Text  = h.ImgType;
            }
        }

        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            _history.Clear();
            lstHistory.Items.Clear();
        }

        // ════════════════════════════════════════════════════════════════════
        // ログ
        // ════════════════════════════════════════════════════════════════════
        private void btnClearLog_Click(object sender, EventArgs e) => txtLog.Clear();

        private void btnCopyLog_Click(object sender, EventArgs e)
        {
            if (txtLog.Text.Length > 0)
                Clipboard.SetText(txtLog.Text);
        }

        // ════════════════════════════════════════════════════════════════════
        // UI ヘルパー
        // ════════════════════════════════════════════════════════════════════
        private void SetBusy(bool busy)
        {
            btnFetch.Enabled   = !busy;
            btnCancel.Enabled  = busy;
            progressBar.Visible = busy;
            progressBar.Style  = ProgressBarStyle.Marquee;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void ClearResult()
        {
            lblStatus.Text       = string.Empty;
            lblPreviewInfo.Text  = string.Empty;
            lblZoom.Text         = "100%";
            picPreview.Image?.Dispose();
            picPreview.Image     = null;
            _originalImageSize   = Size.Empty;
            _zoomScale           = 1.0;
            pnlScroll.Visible    = false;
            pnlNoPreview.Visible = false;
            btnSave.Enabled      = false;
            btnOpenApp.Enabled   = false;
            _lastImageBytes      = null;
        }

        private static void ShowError(string msg) =>
            MessageBox.Show(msg, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

        // Enter キーで送信
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && txtDocId.Focused)
            {
                btnFetch.PerformClick();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // フォームクローズ時にクライアントを解放
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _client?.Dispose();
            _cts?.Dispose();
        }
    }

    // ── 履歴アイテム ──────────────────────────────────────────────────────
    public sealed record HistoryItem(
        string DocId,
        string ImgType,
        int    StatusCode,
        long   ElapsedMs,
        string ContentType,
        long   SizeBytes,
        bool   IsSuccess);

    // ── TextBox 拡張 ──────────────────────────────────────────────────────
    internal static class TextBoxExtensions
    {
        public static void AppendLine(this TextBox tb, string text)
        {
            tb.AppendText(text + Environment.NewLine);
            tb.ScrollToCaret();
        }
    }
}
