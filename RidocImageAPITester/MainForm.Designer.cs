using System.Drawing;
using System.Windows.Forms;

namespace RidocImageAPITester
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null!;

        // ── コントロール宣言 ────────────────────────────────────────────────
        // ツールバー系
        private Label      lblBaseUrl    = null!;
        private TextBox    txtBaseUrl    = null!;
        private Label      lblDocId      = null!;
        private TextBox    txtDocId      = null!;
        private Label      lblImgType    = null!;
        private ComboBox   cmbImgType    = null!;
        private Button     btnFetch      = null!;
        private Button     btnCancel     = null!;
        private ProgressBar progressBar  = null!;
        private Label      lblStatus     = null!;

        // プレビュー系
        private PictureBox picPreview    = null!;
        private Panel      pnlNoPreview  = null!;
        private Label      lblNoPreview  = null!;
        private Label      lblPreviewInfo = null!;
        private Button     btnSave       = null!;
        private Button     btnOpenApp    = null!;

        // 履歴
        private ListView   lstHistory    = null!;
        private Button     btnClearHistory = null!;

        // ログ
        private TextBox    txtLog        = null!;
        private Button     btnClearLog   = null!;
        private Button     btnCopyLog    = null!;

        // レイアウト
        private SplitContainer splitMain   = null!;
        private SplitContainer splitRight  = null!;
        private Panel          pnlTop      = null!;
        private GroupBox       grpPreview  = null!;
        private GroupBox       grpHistory  = null!;
        private GroupBox       grpLog      = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            SuspendLayout();

            // ── フォーム本体 ────────────────────────────────────────────────
            Text            = "RidocImageAPI テスター";
            Size            = new Size(1280, 800);
            MinimumSize     = new Size(900, 600);
            StartPosition   = FormStartPosition.CenterScreen;
            Font            = new Font("Meiryo UI", 9F);
            BackColor       = Color.FromArgb(245, 245, 248);

            // ════════════════════════════════════════════════════════════════
            // 上部：入力パネル
            // ════════════════════════════════════════════════════════════════
            pnlTop = new Panel
            {
                Dock        = DockStyle.Top,
                Height      = 56,
                Padding     = new Padding(8, 8, 8, 4),
                BackColor   = Color.FromArgb(235, 240, 250)
            };

            lblBaseUrl = new Label { Text = "ベース URL:", AutoSize = true, Top = 16, Left = 8 };

            txtBaseUrl = new TextBox
            {
                Left = 76, Top = 12, Width = 300, Height = 24,
                Text = "https://localhost:5088"
            };

            lblDocId = new Label { Text = "docId:", AutoSize = true, Top = 16, Left = 390 };

            txtDocId = new TextBox
            {
                Left = 440, Top = 12, Width = 200, Height = 24,
                PlaceholderText = "例: A15086A02"
            };

            lblImgType = new Label { Text = "imgType:", AutoSize = true, Top = 16, Left = 656 };

            cmbImgType = new ComboBox
            {
                Left = 718, Top = 12, Width = 80, Height = 24,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbImgType.Items.AddRange(new object[] { "TN", "ORG" });
            cmbImgType.SelectedIndex = 0;

            btnFetch = new Button
            {
                Left = 814, Top = 10, Width = 80, Height = 28,
                Text = "取得 (F5)",
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            btnFetch.FlatAppearance.BorderSize = 0;
            btnFetch.Click += btnFetch_Click;

            btnCancel = new Button
            {
                Left = 902, Top = 10, Width = 80, Height = 28,
                Text = "キャンセル",
                Enabled = false,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.Click += btnCancel_Click;

            progressBar = new ProgressBar
            {
                Left = 1000, Top = 14, Width = 120, Height = 20,
                Visible = false, Style = ProgressBarStyle.Marquee
            };

            lblStatus = new Label
            {
                Left = 1130, Top = 16, AutoSize = true,
                Font = new Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold)
            };

            pnlTop.Controls.AddRange(new Control[] {
                lblBaseUrl, txtBaseUrl, lblDocId, txtDocId,
                lblImgType, cmbImgType, btnFetch, btnCancel,
                progressBar, lblStatus
            });

            // ════════════════════════════════════════════════════════════════
            // メインスプリット（左：プレビュー ／ 右：履歴+ログ）
            // ════════════════════════════════════════════════════════════════
            splitMain = new SplitContainer
            {
                Dock            = DockStyle.Fill,
                Orientation     = Orientation.Vertical,
                SplitterDistance = 700,
                Panel1MinSize   = 400,
                Panel2MinSize   = 300
            };

            // ════════════════════════════════════════════════════════════════
            // 左パネル：プレビュー
            // ════════════════════════════════════════════════════════════════
            grpPreview = new GroupBox
            {
                Dock    = DockStyle.Fill,
                Text    = "プレビュー",
                Padding = new Padding(4)
            };

            picPreview = new PictureBox
            {
                Dock        = DockStyle.Fill,
                SizeMode    = PictureBoxSizeMode.Zoom,
                BackColor   = Color.FromArgb(40, 40, 40),
                BorderStyle = BorderStyle.None
            };

            pnlNoPreview = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.FromArgb(40, 40, 40),
                Visible   = false
            };

            lblNoPreview = new Label
            {
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.LightGray,
                Font      = new Font("Meiryo UI", 10F),
                Text      = "ここに画像が表示されます"
            };
            pnlNoPreview.Controls.Add(lblNoPreview);

            var pnlPreviewBottom = new Panel
            {
                Dock   = DockStyle.Bottom,
                Height = 36
            };

            lblPreviewInfo = new Label
            {
                Dock      = DockStyle.Left,
                Width     = 400,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Meiryo UI", 8.5F),
                ForeColor = Color.DimGray
            };

            btnSave = new Button
            {
                Dock      = DockStyle.Right,
                Width     = 100,
                Text      = "💾 保存",
                Enabled   = false,
                FlatStyle = FlatStyle.Flat
            };
            btnSave.Click += btnSave_Click;

            btnOpenApp = new Button
            {
                Dock      = DockStyle.Right,
                Width     = 130,
                Text      = "🔍 外部アプリで開く",
                Enabled   = false,
                FlatStyle = FlatStyle.Flat
            };
            btnOpenApp.Click += btnOpenApp_Click;

            pnlPreviewBottom.Controls.AddRange(new Control[] { lblPreviewInfo, btnSave, btnOpenApp });

            // プレビュー内に PictureBox と noPreview パネルを重ねる
            var pnlPreviewContent = new Panel { Dock = DockStyle.Fill };
            pnlPreviewContent.Controls.Add(picPreview);
            pnlPreviewContent.Controls.Add(pnlNoPreview);

            grpPreview.Controls.Add(pnlPreviewContent);
            grpPreview.Controls.Add(pnlPreviewBottom);
            splitMain.Panel1.Controls.Add(grpPreview);

            // ════════════════════════════════════════════════════════════════
            // 右パネル：履歴 + ログ（縦スプリット）
            // ════════════════════════════════════════════════════════════════
            splitRight = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = Orientation.Horizontal,
                SplitterDistance = 280,
                Panel1MinSize    = 150,
                Panel2MinSize    = 150
            };

            // ── 履歴 GroupBox ─────────────────────────────────────────────
            grpHistory = new GroupBox
            {
                Dock    = DockStyle.Fill,
                Text    = "履歴（ダブルクリックで入力欄に反映）",
                Padding = new Padding(4)
            };

            lstHistory = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                GridLines     = true,
                Font          = new Font("Meiryo UI", 8.5F)
            };
            lstHistory.Columns.Add("docId",       120);
            lstHistory.Columns.Add("imgType",      60);
            lstHistory.Columns.Add("status",       55);
            lstHistory.Columns.Add("size",         70);
            lstHistory.Columns.Add("elapsed",      70);
            lstHistory.Columns.Add("ContentType", 130);
            lstHistory.DoubleClick += lstHistory_DoubleClick;

            btnClearHistory = new Button
            {
                Dock      = DockStyle.Bottom,
                Height    = 26,
                Text      = "履歴をクリア",
                FlatStyle = FlatStyle.Flat
            };
            btnClearHistory.Click += btnClearHistory_Click;

            grpHistory.Controls.Add(lstHistory);
            grpHistory.Controls.Add(btnClearHistory);
            splitRight.Panel1.Controls.Add(grpHistory);

            // ── ログ GroupBox ──────────────────────────────────────────────
            grpLog = new GroupBox
            {
                Dock    = DockStyle.Fill,
                Text    = "ログ",
                Padding = new Padding(4)
            };

            txtLog = new TextBox
            {
                Dock        = DockStyle.Fill,
                Multiline   = true,
                ReadOnly    = true,
                ScrollBars  = ScrollBars.Both,
                WordWrap    = false,
                Font        = new Font("Consolas", 8.5F),
                BackColor   = Color.FromArgb(30, 30, 30),
                ForeColor   = Color.LightGreen
            };

            var pnlLogButtons = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            btnClearLog = new Button
            {
                Dock      = DockStyle.Left,
                Width     = 90,
                Text      = "ログクリア",
                FlatStyle = FlatStyle.Flat
            };
            btnClearLog.Click += btnClearLog_Click;

            btnCopyLog = new Button
            {
                Dock      = DockStyle.Left,
                Width     = 90,
                Text      = "コピー",
                FlatStyle = FlatStyle.Flat
            };
            btnCopyLog.Click += btnCopyLog_Click;

            pnlLogButtons.Controls.AddRange(new Control[] { btnClearLog, btnCopyLog });

            grpLog.Controls.Add(txtLog);
            grpLog.Controls.Add(pnlLogButtons);
            splitRight.Panel2.Controls.Add(grpLog);

            splitMain.Panel2.Controls.Add(splitRight);

            // ════════════════════════════════════════════════════════════════
            // フォームに追加
            // ════════════════════════════════════════════════════════════════
            Controls.Add(splitMain);
            Controls.Add(pnlTop);

            // F5 で取得
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F5) btnFetch.PerformClick(); };

            ResumeLayout(false);
        }
    }
}
