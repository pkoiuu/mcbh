// 主窗体 — 在线安装器界面（现代简约 · 深色无边框自绘）
// 设计语言：低饱和深灰底 + 发丝分隔线 + 单一强调色 + 克制的留白
// 流程: 查最新版本 → 自建加速线路 → 多线程分块下载 → 启动 Inno 安装向导
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
        // ===== 设计令牌 =====
        private static readonly Color BgTop = Color.FromArgb(31, 32, 37);
        private static readonly Color BgBottom = Color.FromArgb(24, 25, 29);
        private static readonly Color Hairline = Color.FromArgb(43, 45, 51);
        private static readonly Color TextPri = Color.FromArgb(236, 236, 241);
        private static readonly Color TextSec = Color.FromArgb(154, 156, 166);
        private static readonly Color TextTri = Color.FromArgb(106, 109, 120);
        private static readonly Color Accent = Color.FromArgb(52, 120, 246);
        private static readonly Color AccentHi = Color.FromArgb(96, 158, 255);
        private static readonly Color Track = Color.FromArgb(40, 42, 48);
        private static readonly Color Green = Color.FromArgb(63, 185, 80);
        private static readonly Color Red = Color.FromArgb(248, 113, 113);

        private const int WinW = 440;
        private const int WinH = 560;

        private enum StepState { Pending, Active, Done, Fail }
        private readonly StepState[] _steps = { StepState.Pending, StepState.Pending, StepState.Pending };

        // ===== 下载状态 =====
        private double _progress;            // 0..1
        private long _down, _total;
        private double _speed;               // MB/s
        private bool _indeterminate;
        private CancellationTokenSource _cts;
        private string _tempExePath = "";
        private ReleaseInfo _releaseInfo;
        private bool _completed;
        private int _currentStep = -1;
        private bool _closeHover;

        // ===== 控件 =====
        private readonly Label[] _stepText = new Label[3];
        private readonly Label[] _stepRight = new Label[3];
        private Label _lblDetail;            // 过程明细 / 错误信息
        private Label _statLeft;             // 已下载 / 总量 · 百分比
        private Label _statRight;            // 速度 · 剩余时间
        private Panel _progressPanel;
        private PillButton _btnAction;
        private System.Windows.Forms.Timer _animTimer;
        private Point _dragStart;

        private static readonly Rectangle CloseRect = new Rectangle(WinW - 46, 14, 32, 32);
        private static readonly StringFormat CenterSf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        public MainForm()
        {
            Text = "白鹤服务器 在线安装";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(WinW, WinH);
            MinimumSize = Size;
            MaximumSize = Size;
            BackColor = BgBottom;
            DoubleBuffered = true;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW — 无边框窗体保留投影
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BuildUi();
            _animTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _animTimer.Tick += (s, ev) =>
            {
                if (_indeterminate && _progressPanel != null && !_progressPanel.IsDisposed)
                    _progressPanel.Invalidate();
            };
            _animTimer.Start();
            Shown += async (s, ev) => await RunAsync();
        }

        // ===== UI 构建 =====
        private void BuildUi()
        {
            // 顶栏品牌
            var lblBrand = MakeLabel("白鹤服务器", 10F, TextPri, true);
            lblBrand.Location = new Point(56, 22);
            Controls.Add(lblBrand);

            var lblMode = new Label
            {
                Text = "在线安装",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = TextTri,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(88, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(WinW - 52 - 88, 23),
            };
            Controls.Add(lblMode);

            // 标题区
            var lblTitle = MakeLabel("准备安装", 16F, TextPri, true);
            lblTitle.Location = new Point(30, 88);
            Controls.Add(lblTitle);

            var lblSub = MakeLabel("白鹤服务器启动器 · 完整安装包在线获取", 9F, TextSec);
            lblSub.Location = new Point(33, 127);
            Controls.Add(lblSub);

            // 步骤列表
            var stepNames = new[] { "获取最新版本", "下载完整安装包", "启动安装向导" };
            for (var i = 0; i < 3; i++)
            {
                var t = MakeLabel(stepNames[i], 9.5F, TextSec);
                t.Location = new Point(66, 184 + i * 34 + 1);
                _stepText[i] = t;
                Controls.Add(t);

                var r = new Label
                {
                    Text = "等待",
                    Font = new Font("Microsoft YaHei UI", 8.5F),
                    ForeColor = TextTri,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(140, 20),
                    TextAlign = ContentAlignment.MiddleRight,
                    Location = new Point(WinW - 34 - 140, 184 + i * 34 + 2),
                };
                _stepRight[i] = r;
                Controls.Add(r);
            }

            // 过程明细 / 错误信息
            _lblDetail = new Label
            {
                Text = "",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = TextTri,
                BackColor = Color.Transparent,
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(310, 20),
                Location = new Point(66, 296),
            };
            Controls.Add(_lblDetail);

            // 统计行
            _statLeft = MakeLabel("—", 9F, TextSec);
            _statLeft.Location = new Point(32, 372);
            Controls.Add(_statLeft);

            _statRight = new Label
            {
                Text = "",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = TextTri,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(166, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(WinW - 32 - 166, 373),
            };
            Controls.Add(_statRight);

            // 进度条（细条 · 圆角胶囊）
            _progressPanel = new Panel
            {
                Location = new Point(32, 402),
                Size = new Size(WinW - 64, 5),
                BackColor = Color.Transparent,
            };
            _progressPanel.Paint += ProgressPanel_Paint;
            Controls.Add(_progressPanel);

            // 底部提示
            var lblHint = MakeLabel("下载完成后将自动启动安装向导", 8.25F, TextTri);
            lblHint.Location = new Point(32, 507);
            Controls.Add(lblHint);

            // 操作按钮
            _btnAction = new PillButton
            {
                Text = "取消",
                Location = new Point(WinW - 32 - 108, 496),
            };
            _btnAction.Click += async (s, ev) => await OnActionClickAsync();
            Controls.Add(_btnAction);
        }

        private static Label MakeLabel(string text, float size, Color color, bool bold = false)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = true,
            };
        }

        // ===== 进度条绘制 =====
        private void ProgressPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = _progressPanel.ClientRectangle;
            var r = rect.Height / 2f;

            using (var bg = new SolidBrush(Track))
                g.FillRounded(bg, rect, r);

            if (_indeterminate)
            {
                // 未知总量时：滑动小段表示「正在进行」
                var seg = rect.Width * 0.28f;
                var travel = rect.Width + seg;
                var p = (Environment.TickCount % 1400) / 1400f;
                var x = rect.X + travel * p - seg;
                g.SetClip(GraphicsExtensions.RoundedPath(rect, r));
                using (var brush = new LinearGradientBrush(
                    new RectangleF(x, rect.Y, seg, rect.Height), Accent, AccentHi, LinearGradientMode.Horizontal))
                    g.FillRectangle(brush, x, rect.Y, seg, rect.Height);
                g.ResetClip();
            }
            else if (_progress > 0.001)
            {
                var fillW = (float)(rect.Width * Math.Min(_progress, 1.0));
                var fillRect = new RectangleF(rect.X, rect.Y, Math.Max(fillW, r * 2), rect.Height);
                using (var brush = new LinearGradientBrush(fillRect, Accent, AccentHi, LinearGradientMode.Horizontal))
                    g.FillRounded(brush, fillRect, r);
            }
        }

        // ===== 主流程 =====
        private async Task RunAsync()
        {
            try
            {
                ResetSteps();
                SetButton("取消", true);
                SetDetail("正在连接 GitHub 获取版本信息...", false);
                SetStep(0, StepState.Active);
                _currentStep = 0;

                var info = await UpdateService.GetLatestAsync();
                if (info == null)
                {
                    SetStep(0, StepState.Fail);
                    SetDetail("无法获取版本信息 — 请检查网络后点击「重试」", true);
                    SetButton("重试", true);
                    return;
                }
                _releaseInfo = info;
                SetStep(0, StepState.Done, "v" + info.Version);

                var token = UpdateService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    SetDetail("配置缺失：加速服务令牌未注入（打包问题，请反馈）", true);
                    SetButton("重试", true);
                    return;
                }

                SetStep(1, StepState.Active);
                _currentStep = 1;
                _tempExePath = Path.Combine(Path.GetTempPath(), $"BaiheServer_Setup_v{info.Version}_dl.exe");
                try { if (File.Exists(_tempExePath)) File.Delete(_tempExePath); } catch { }
                _cts = new CancellationTokenSource();

                using (var dl = new Downloader(
                    info.Candidates != null && info.Candidates.Length > 0
                        ? info.Candidates
                        : new[] { info.BestUrl },
                    _tempExePath,
                    threads: 8,
                    authHeaderName: "token",
                    authHeaderValue: token))
                {
                    var ok = await dl.DownloadAsync(
                        (down, total, speed) =>
                        {
                            _down = down;
                            _total = total;
                            _speed = speed;
                            _progress = total > 0 ? (double)down / total : 0;
                            _indeterminate = total <= 0;
                            UpdateStats();
                        },
                        s => SetDetail(s, false),
                        _cts.Token);
                    _indeterminate = false;
                    if (!ok)
                    {
                        SetStep(1, StepState.Fail);
                        SetDetail("下载失败 — 多线程与单线程兜底均未完成，请检查网络后重试", true);
                        SetButton("重试", true);
                        return;
                    }
                }

                _progress = 1;
                UpdateStats();
                SetStep(1, StepState.Done, _total > 0 ? FormatSize(_total) : null);
                SetStep(2, StepState.Active);
                _currentStep = 2;
                SetDetail("正在启动安装向导 — 首次解包约需 10–30 秒，请勿关闭窗口", false);
                SetButton("请等待", false);
                _completed = true;

                System.Diagnostics.Process installerProcess = null;
                try
                {
                    installerProcess = await Task.Run(() =>
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            UseShellExecute = true,
                        })).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    SetStep(2, StepState.Fail);
                    SetDetail("无法启动安装程序：" + ex.Message, true);
                    SetButton("关闭", true);
                    return;
                }

                if (installerProcess != null)
                {
                    SetDetail("安装向导窗口即将出现...", false);
                    for (var i = 0; i < 60; i++)
                    {
                        await Task.Delay(500).ConfigureAwait(true);
                        if (installerProcess.HasExited)
                        {
                            SetDetail("安装向导已退出（可能已完成或被取消）", false);
                            break;
                        }
                        if (installerProcess.MainWindowHandle != IntPtr.Zero)
                            break;
                    }
                }

                SetStep(2, StepState.Done);
                SetDetail("安装向导已就绪 — 此窗口即将关闭", false);
                await Task.Delay(600).ConfigureAwait(true);
                if (!IsDisposed)
                    Close();
            }
            catch (OperationCanceledException)
            {
                OnCancelled();
            }
            catch (Exception ex)
            {
                if (_currentStep >= 0)
                    SetStep(_currentStep, StepState.Fail);
                SetDetail("发生错误：" + ex.Message, true);
                SetButton("重试", true);
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
                await RunAsync();
                return;
            }
            if (_btnAction.Text == "关闭")
            {
                CleanupTemp();
                Close();
            }
        }

        private void OnCancelled()
        {
            _indeterminate = false;
            if (_currentStep >= 0 && _steps[_currentStep] == StepState.Active)
                SetStep(_currentStep, StepState.Pending, "已取消");
            SetDetail("已取消 — 临时文件已清理", false);
            SetButton("关闭", true);
            CleanupTemp();
        }

        private void CleanupTemp()
        {
            try { if (!string.IsNullOrEmpty(_tempExePath) && File.Exists(_tempExePath) && !_completed) File.Delete(_tempExePath); } catch { }
        }

        // ===== UI 更新辅助 =====
        private void SetStep(int idx, StepState st, string rightOverride = null)
        {
            if (IsDisposed) return;
            _steps[idx] = st;
            var main = _stepText[idx];
            var right = _stepRight[idx];
            if (main == null || right == null) return;

            main.ForeColor = st == StepState.Pending ? TextSec : TextPri;
            switch (st)
            {
                case StepState.Pending:
                    right.Text = rightOverride ?? "等待";
                    right.ForeColor = TextTri;
                    break;
                case StepState.Active:
                    right.Text = rightOverride ?? "进行中";
                    right.ForeColor = Accent;
                    break;
                case StepState.Done:
                    right.Text = rightOverride ?? "完成";
                    right.ForeColor = TextTri;
                    break;
                case StepState.Fail:
                    right.Text = rightOverride ?? "失败";
                    right.ForeColor = Red;
                    break;
            }
            Invalidate();
        }

        private void SetDetail(string text, bool isError)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    _lblDetail.Text = text;
                    _lblDetail.ForeColor = isError ? Red : TextTri;
                }));
            }
            catch { }
        }

        private void SetButton(string text, bool enabled)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    _btnAction.Text = text;
                    _btnAction.Enabled = enabled;
                }));
            }
            catch { }
        }

        private void UpdateStats()
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    var pct = _total > 0 ? Math.Floor(_progress * 100) : -1;
                    _statLeft.Text = pct >= 0
                        ? $"{FormatSize(_down)} / {FormatSize(_total)} · {pct}%"
                        : FormatSize(_down);

                    var right = _speed > 0 ? $"{_speed:0.0} MB/s" : "";
                    if (_total > 0 && _speed > 0 && _down < _total)
                    {
                        var eta = (_total - _down) / 1024.0 / 1024.0 / _speed;
                        right += eta >= 60
                            ? $" · 剩余 {(int)eta / 60}分{Math.Ceiling(eta % 60)}秒"
                            : $" · 剩余 {Math.Ceiling(eta)}秒";
                    }
                    _statRight.Text = string.IsNullOrEmpty(right) ? "—" : right;

                    if (_steps[1] == StepState.Active && pct >= 0)
                        _stepRight[1].Text = pct + "%";

                    _progressPanel.Invalidate();
                }));
            }
            catch { }
        }

        private void ResetSteps()
        {
            _currentStep = -1;
            for (var i = 0; i < 3; i++)
                SetStep(i, StepState.Pending);
            _progress = 0;
            _down = 0;
            _total = 0;
            _speed = 0;
            _indeterminate = false;
            if (!IsDisposed)
            {
                try
                {
                    _statLeft.Text = "—";
                    _statRight.Text = "—";
                    _lblDetail.Text = "";
                    _progressPanel.Invalidate();
                }
                catch { }
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }

        // ===== 自绘 =====
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 背景在 OnPaint 中统一绘制（双缓冲，避免闪烁）
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 渐变背景（极轻，仅提供纵深）
            using (var bg = new LinearGradientBrush(ClientRectangle, BgTop, BgBottom, LinearGradientMode.Vertical))
                g.FillRectangle(bg, ClientRectangle);

            // 外沿发丝描边
            using (var pen = new Pen(Hairline))
            {
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                g.DrawLine(pen, 0, 62, ClientSize.Width, 62);       // 顶栏分隔
                g.DrawLine(pen, 0, 464, ClientSize.Width, 464);     // 底栏分隔
            }

            // Logo（小方块 + 单字）
            var logo = new Rectangle(24, 20, 22, 22);
            using (var b = new SolidBrush(Accent))
                g.FillRounded(b, logo, 6);
            using (var f = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
            using (var w = new SolidBrush(Color.White))
                g.DrawString("白", f, w, new RectangleF(logo.X - 1, logo.Y + 1, logo.Width + 2, logo.Height + 2), CenterSf);

            // 关闭按钮（右上角）
            if (_closeHover)
                using (var b = new SolidBrush(Color.FromArgb(42, 44, 50)))
                    g.FillRounded(b, CloseRect, 8);
            using (var p = new Pen(_closeHover ? TextPri : TextTri, 1.6f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                var cx = CloseRect.X + 16;
                var cy = CloseRect.Y + 16;
                const int rr = 5;
                g.DrawLine(p, cx - rr, cy - rr, cx + rr, cy + rr);
                g.DrawLine(p, cx - rr, cy + rr, cx + rr, cy - rr);
            }

            // 步骤圆点
            for (var i = 0; i < 3; i++)
                DrawStepCircle(g, i, new Rectangle(36, 184 + i * 34 + 2, 18, 18));
        }

        private void DrawStepCircle(Graphics g, int idx, Rectangle r)
        {
            switch (_steps[idx])
            {
                case StepState.Pending:
                    using (var p = new Pen(Color.FromArgb(58, 61, 70), 1.4f))
                        g.DrawEllipse(p, r);
                    using (var b = new SolidBrush(TextTri))
                    using (var f = new Font("Segoe UI", 8F, FontStyle.Bold))
                        g.DrawString((idx + 1).ToString(), f, b, new RectangleF(r.X - 1, r.Y + 1, r.Width + 2, r.Height + 2), CenterSf);
                    break;
                case StepState.Active:
                    using (var b = new SolidBrush(Accent))
                        g.FillEllipse(b, r);
                    using (var b = new SolidBrush(Color.White))
                    using (var f = new Font("Segoe UI", 8F, FontStyle.Bold))
                        g.DrawString((idx + 1).ToString(), f, b, new RectangleF(r.X - 1, r.Y + 1, r.Width + 2, r.Height + 2), CenterSf);
                    break;
                case StepState.Done:
                    using (var b = new SolidBrush(Green))
                        g.FillEllipse(b, r);
                    using (var p = new Pen(Color.White, 1.8f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawLine(p, r.X + 5, r.Y + 9, r.X + 8, r.Y + 12);
                        g.DrawLine(p, r.X + 8, r.Y + 12, r.X + 13, r.Y + 6);
                    }
                    break;
                case StepState.Fail:
                    using (var b = new SolidBrush(Color.FromArgb(58, 34, 38)))
                        g.FillEllipse(b, r);
                    using (var p = new Pen(Red, 1.6f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawLine(p, r.X + 6, r.Y + 6, r.X + 12, r.Y + 12);
                        g.DrawLine(p, r.X + 6, r.Y + 12, r.X + 12, r.Y + 6);
                    }
                    break;
            }
        }

        // ===== 交互：拖动 / 关闭 / Esc =====
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var overClose = CloseRect.Contains(e.Location);
            if (overClose != _closeHover)
            {
                _closeHover = overClose;
                Invalidate(CloseRect);
            }
            Cursor = overClose ? Cursors.Hand : Cursors.Default;
            if (e.Button == MouseButtons.Left && !_dragStart.IsEmpty)
            {
                var p = PointToScreen(e.Location);
                Location = new Point(p.X - _dragStart.X, p.Y - _dragStart.Y);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_closeHover)
            {
                _closeHover = false;
                Invalidate(CloseRect);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && !CloseRect.Contains(e.Location))
                _dragStart = new Point(e.X, e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragStart = Point.Empty;
            if (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location))
            {
                _cts?.Cancel();
                CleanupTemp();
                Close();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                _cts?.Cancel();
                CleanupTemp();
                Close();
            }
        }
    }

    // ===== 胶囊按钮（自绘 · 圆角 · 悬停/按下态）=====
    internal class PillButton : Control
    {
        private bool _hover;
        private bool _pressed;

        private static readonly Color IdleBg = Color.FromArgb(33, 34, 39);
        private static readonly Color HoverBg = Color.FromArgb(42, 44, 50);
        private static readonly Color PressBg = Color.FromArgb(49, 51, 58);
        private static readonly Color BorderCol = Color.FromArgb(58, 60, 68);
        private static readonly Color TextOn = Color.FromArgb(236, 236, 241);
        private static readonly Color TextOff = Color.FromArgb(106, 109, 120);

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(108, 38);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);

            var bg = !Enabled ? IdleBg : _pressed ? PressBg : _hover ? HoverBg : IdleBg;
            using (var b = new SolidBrush(bg))
                g.FillRounded(b, rect, 9);
            using (var p = new Pen(!Enabled ? Color.FromArgb(46, 48, 54) : BorderCol))
                g.DrawRounded(p, Rectangle.Round(rect), 9);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height),
                Enabled ? TextOn : TextOff,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var wasPressed = _pressed;
            _pressed = false;
            Invalidate();
            if (e.Button == MouseButtons.Left && wasPressed && ClientRectangle.Contains(e.Location))
                OnClick(EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hover = false;
            _pressed = false;
            Invalidate();
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

        internal static GraphicsPath RoundedPath(RectangleF rect, float radius)
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
