// macOS 风格交通灯标题栏代码后置 — 三色按钮事件处理和窗口拖拽
// 关闭(#FF5F57)、最小化(#FEBC2E)、最大化(#28C840)

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Baihe.Host.Chrome;

/// <summary>
/// macOS 风格交通灯标题栏 — 提供窗口控制按钮和拖拽支持
/// </summary>
public partial class TitleBar : System.Windows.Controls.UserControl
{
    /// <summary>
    /// 创建标题栏实例
    /// </summary>
    public TitleBar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 切换标题栏主题 — 同步背景色和文字颜色
    /// </summary>
    /// <param name="isDark">是否为深色主题</param>
    public void SetTheme(bool isDark)
    {
        if (isDark)
        {
            this.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1C));
            if (TitleText != null)
                TitleText.Foreground = new SolidColorBrush(Colors.White);
        }
        else
        {
            this.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xF7, 0xF7, 0xFA));
            if (TitleText != null)
                TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));
        }
    }

    /// <summary>
    /// 标题栏鼠标按下 — 触发窗口拖拽
    /// </summary>
    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 仅左键拖拽
        if (e.ChangedButton == MouseButton.Left)
        {
            var window = Window.GetWindow(this);
            window?.DragMove();
        }
    }

    /// <summary>
    /// 关闭按钮按下 — 关闭应用程序
    /// </summary>
    private void OnCloseMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 阻止事件冒泡到标题栏，避免触发拖拽
        e.Handled = true;
        Application.Current.Shutdown();
    }

    /// <summary>
    /// 最小化按钮按下 — 最小化窗口
    /// </summary>
    private void OnMinimizeMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    /// <summary>
    /// 最大化按钮按下 — 切换最大化/正常状态
    /// </summary>
    private void OnMaximizeMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
