// 主窗体 — 在线安装器界面（无边框自绘 + 下载/安装流程编排）
// 流程: 查版本 → 测速选线路 → 多线程下载 → 启动安装程序
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Baihe.OnlineInstaller
{
    public class MainForm : Form
    {
        // ===== 界面常量 =====
        private static readonly Color BgTop = Color.FromArgb(30, 30, 34);
        private static readonly Color BgBottom = Color.FromArgb(18, 18, 21);
        private static readonly Color Accent = Color.FromArgb(0, 122, 255);
        private static readonly Color AccentLight = Color.FromArgb(80, 170, 255);
        private static readonly Color TextMain = Color.FromArgb(240, 240, 245);
        private static readonly Color TextDim = Color.FromArgb(150, 150, 160);
        private static readonly Color CardBg = Color.FromArgb(38, 38, 44);
        private static readonly Color BorderDim = Color.FromArgb(60, 60, 68);

        private const int WinW = 440;
        private const int WinH = 580;

        // ===== 状态 =====
        private string _statusText = "正在初始化...";
        private double _progress;      // 0..1
        private string _speedText = "";
        private bool _downloading;
        private CancellationTokenSource _cts;
        private string _tempExePath = "";
        private ReleaseInfo _releaseInfo;
        private bool _completed;

        // ===== 控件 =====
        private Label _lblTitle;
        private Label _lblSubtitle;
        private Label _lblStatus;
        private Label _lblInfo;
        private Panel _progressPanel;
        private Button _btnAction;
        private Button _btnClose;
        private System.Windows.Forms.Timer _animTimer;
        private int _animPhase;

        public MainForm()
        {
            Text = "白鹤服务器 在线安装";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(WinW, WinH);
            BackColor = BgBottom;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9F);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BuildUi();
            _animTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _animTimer.Tick += (s, ev) => { _animPhase++; Invalidate(); };
            _animTimer.Start();
            Shown += async (s, ev) => await RunAsync();
        }

        // ===== UI 构建 =====
        private void BuildUi()
        {
            // 标题
            _lblTitle = new Label
            {
                Text = "白鹤服务器 · 在线安装",
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(28, 34),
            };
            _lblSubtitle = new Label
            {
                Text = "轻量安装器 · 自动选择最快线路 · 多线程下载",
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(29, 68),
            };

            // 状态与信息
            _lblStatus = new Label
            {
                Text = _statusText,
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(30, 300),
                Size = new Size(WinW - 60, 28),
            };
            _lblInfo = new Label
            {
                Text = "",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = TextDim,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new Point(30, 332),
                Size = new Size(WinW - 60, 64),
            };

            // 进度条
            _progressPanel = new Panel
            {
                Location = new Point(30, 420),
                Size = new Size(WinW - 60, 12),
                BackColor = CardBg,
            };
            _progressPanel.Paint += ProgressPanel_Paint;

            // 操作按钮
            _btnAction = new Button
            {
                Text = "取消",
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = TextMain,
                BackColor = CardBg,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(WinW - 128, WinH - 62),
                Size = new Size(98, 34),
            };
            _btnAction.FlatAppearance.BorderSize = 1;
            _btnAction.FlatAppearance.BorderColor = BorderDim;
            _btnAction.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 52, 60);
            _btnAction.Click += async (s, ev) => await OnActionClickAsync();

            // 关闭按钮（右上角 ×）
            _btnClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextDim,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(WinW - 44, 14),
                Size = new Size(30, 26),
                TabStop = false,
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 78);
            _btnClose.Click += (s, ev) => { _cts?.Cancel(); CleanupTemp(); Close(); };

            Controls.Add(_lblTitle);
            Controls.Add(_lblSubtitle);
            Controls.Add(_lblStatus);
            Controls.Add(_lblInfo);
            Controls.Add(_progressPanel);
            Controls.Add(_btnAction);
            Controls.Add(_btnClose);
        }

        private void ProgressPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = _progressPanel.ClientRectangle;
            rect.Inflate(-1, -1);
            var radius = rect.Height / 2f;

            // 背景
            using (var bg = new SolidBrush(CardBg))
                g.FillRounded(bg, rect, radius);

            // 进度填充
            if (_progress > 0.001)
            {
                var fillW = (float)(rect.Width * Math.Min(_progress, 1.0));
                var fillRect = new RectangleF(rect.X, rect.Y, Math.Max(fillW, radius * 2), rect.Height);
                using (var brush = new LinearGradientBrush(fillRect, Accent, AccentLight, LinearGradientMode.Horizontal))
                    g.FillRounded(brush, fillRect, radius);
            }
        }

        // ===== 流程 =====
        private async Task RunAsync()
        {
            try
            {
                // 1. 查最新版本
                SetStatus("正在检查最新版本...", "");
                _btnAction.Text = "取消";
                _btnAction.Enabled = true;
                var info = await UpdateService.GetLatestAsync();
                if (info == null)
                {
                    SetStatus("获取版本信息失败", "请检查网络后重试（GitHub API 不可达）");
                    _btnAction.Text = "重试";
                    return;
                }
                _releaseInfo = info;

                // 2. 测速选线路
                SetStatus("正在选择最快下载线路...", $"发现新版本 v{info.Version}");
                info = await UpdateService.PickFastestAsync(info);
                if (string.IsNullOrEmpty(info.BestUrl))
                    info.BestUrl = info.DownloadUrl;
                SetStatus("线路选择完成", $"线路：{info.Source}（{info.SpeedMbps:0.0} MB/s）");

                // 3. 下载
                _tempExePath = Path.Combine(Path.GetTempPath(), $"BaiheServer_Setup_v{info.Version}_dl.exe");
                try { if (File.Exists(_tempExePath)) File.Delete(_tempExePath); } catch { }
                _downloading = true;
                _cts = new CancellationTokenSource();

                using (var dl = new Downloader(info.BestUrl, _tempExePath, threads: 8))
                {
                    var ok = await dl.DownloadAsync(
                        (down, total, speed) =>
                        {
                            if (total > 0)
                                _progress = (double)down / total;
                            _speedText = speed > 0 ? $"{speed:0.0} MB/s" : "";
                            UpdateInfoText(down, total);
                            InvalidateProgress();
                        },
                        s => SafeSetStatus(s),
                        _cts.Token);
                    if (!ok)
                    {
                        SetStatus("下载失败", "文件大小校验不通过，请重试");
                        _btnAction.Text = "重试";
                        return;
                    }
                }

                _downloading = false;
                _progress = 1.0;
                InvalidateProgress();

                // 4. 启动安装程序（Inno 向导接管）
                SetStatus("下载完成，正在启动安装程序...", _tempExePath);
                _btnAction.Text = "关闭";
                _completed = true;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    SetStatus("无法启动安装程序", ex.Message);
                    return;
                }

                // 5. 稍后自动退出（安装向导已接管）
                await Task.Delay(2500);
                if (!IsDisposed)
                    Close();
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消", "临时文件已清理");
                _btnAction.Text = "关闭";
                CleanupTemp();
            }
            catch (Exception ex)
            {
                SetStatus("发生错误", ex.Message);
                _btnAction.Text = "重试";
            }
        }

        private async Task OnActionClickAsync()
        {
            if (_btnAction.Text == "取消")
            {
                _cts?.Cancel();
                return;
            }
            if (_btnAction.Text == "重试")
            {
                _progress = 0;
                InvalidateProgress();
                await RunAsync();
                return;
            }
            if (_btnAction.Text == "关闭")
            {
                CleanupTemp();
                Close();
            }
        }

        private void CleanupTemp()
        {
            try { if (!string.IsNullOrEmpty(_tempExePath) && File.Exists(_tempExePath) && !_completed) File.Delete(_tempExePath); } catch { }
        }

        // ===== UI 更新辅助 =====
        private void SetStatus(string status, string info)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _statusText = status;
                    _lblStatus.Text = status;
                    _lblInfo.Text = info;
                }));
            }
            catch { }
        }

        private void SafeSetStatus(string s)
        {
            SetStatus(s, _lblInfo.Text);
        }

        private void UpdateInfoText(long down, long total)
        {
            if (IsDisposed) return;
            try
            {
                var text = FormatSize(down) + " / " + (total > 0 ? FormatSize(total) : "未知");
                if (!string.IsNullOrEmpty(_speedText))
                    text += "   ·   " + _speedText;
                BeginInvoke(new Action(() => { _lblInfo.Text = text; }));
            }
            catch { }
        }

        private void InvalidateProgress()
        {
            if (IsDisposed) return;
            try { _progressPanel.Invalidate(); } catch { }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }

        // ===== 自绘背景 =====
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 渐变背景
            using (var bg = new LinearGradientBrush(ClientRectangle, BgTop, BgBottom, LinearGradientMode.Vertical))
                g.FillRectangle(bg, ClientRectangle);

            // 顶部品牌条
            using (var accent = new SolidBrush(Accent))
                g.FillRectangle(accent, 0, 0, ClientSize.Width, 3);

            // Logo（圆角方块 + 白字）
            var logoRect = new Rectangle(28, 106, 52, 52);
            using (var logoBg = new LinearGradientBrush(logoRect, Accent, Color.FromArgb(0, 90, 200), LinearGradientMode.ForwardDiagonal))
                g.FillRounded(logoBg, logoRect, 14);
            using (var logoText = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("白", logoText, brush, new RectangleF(logoRect.X, logoRect.Y, logoRect.Width, logoRect.Height), sf);
            }

            // Logo 右侧说明
            using (var nameFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold))
            using (var brush = new SolidBrush(TextMain))
                g.DrawString("白鹤服务器启动器", nameFont, brush, 94, 112);
            using (var verFont = new Font("Microsoft YaHei UI", 8.5F))
            using (var brush = new SolidBrush(TextDim))
                g.DrawString("完整安装包 · 在线自动安装", verFont, brush, 95, 138);

            // 卡片：版本信息
            var cardRect = new Rectangle(28, 180, WinW - 56, 96);
            using (var card = new SolidBrush(CardBg))
                g.FillRounded(card, cardRect, 12);
            using (var pen = new Pen(BorderDim))
                g.DrawRounded(pen, cardRect, 12);

            var verLine = _releaseInfo != null
                ? $"最新版本    v{_releaseInfo.Version}"
                : "正在获取最新版本...";
            using (var verFont = new Font("Microsoft YaHei UI", 10F))
            using (var brush = new SolidBrush(TextMain))
                g.DrawString(verLine, verFont, brush, 46, 196);
            using (var dimFont = new Font("Microsoft YaHei UI", 8.5F))
            using (var brush = new SolidBrush(TextDim))
            {
                var sub = _releaseInfo != null && !string.IsNullOrEmpty(_releaseInfo.Notes)
                    ? FirstLine(_releaseInfo.Notes)
                    : "自动检测更新 · 加速线路择优 · 断点续传";
                g.DrawString(sub, dimFont, brush, 46, 222);
            }

            // 状态指示（下载中动画点）
            if (_downloading && _progress < 1)
            {
                var dotX = 32;
                var dotY = 308;
                using (var dot = new SolidBrush(AccentLight))
                    g.FillEllipse(dot, dotX, dotY + 2, 10, 10);
                using (var font = new Font("Microsoft YaHei UI", 8F))
                using (var brush = new SolidBrush(TextDim))
                    g.DrawString(_progress > 0 ? "下载中" : "连接中", font, brush, 48, 304);
            }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var i = s.IndexOf('\n');
            var line = i >= 0 ? s.Substring(0, i) : s;
            return line.Length > 46 ? line.Substring(0, 46) + "…" : line;
        }

        // ===== 无边框拖动 =====
        private Point _dragStart;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                _dragStart = new Point(e.X, e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left && !_dragStart.IsEmpty)
            {
                var p = PointToScreen(e.Location);
                Location = new Point(p.X - _dragStart.X, p.Y - _dragStart.Y);
            }
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRounded(this Graphics g, Brush brush, RectangleF rect, float radius)
        {
            using (var path = RoundedPath(rect, radius))
                g.FillPath(brush, path);
        }

        public static void DrawRounded(this Graphics g, Pen pen, Rectangle rect, float radius)
        {
            using (var path = RoundedPath(rect, radius))
                g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
