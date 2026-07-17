using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ourmclauncher;

/// <summary>
/// 自定义窗口类 - 支持Windows 11样式和动画效果
/// </summary>
public class LauncherForm : Form
{
    // Windows API 导入
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;
    private const int DWMWCP_DONOTROUND = 1;
    private const int GWL_STYLE = -16;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;
    private const int WM_NCHITTEST = 0x84;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTBOTTOM = 15;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    public LauncherForm()
    {
        Text = "ourmc-launcher";
        Size = new Size(1000, 650);
        MinimumSize = new Size(800, 500);
        MaximumSize = new Size(1920, 1080);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(26, 26, 46);
        ShowInTaskbar = true;

        // 启用双缓冲，减少闪烁
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);

        // 初始隐藏窗口，等加载完成后再显示，避免闪烁
        Opacity = 0;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        
        // 添加必要的窗口样式，支持任务栏交互
        int style = GetWindowLong(this.Handle, GWL_STYLE);
        SetWindowLong(this.Handle, GWL_STYLE, style | WS_MINIMIZEBOX | WS_SYSMENU);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyWindows11Style();
        ApplyRoundedRegionFallback();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegionFallback();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // 添加 WS_MINIMIZEBOX、WS_SYSMENU 和 WS_THICKFRAME 样式
            cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU | WS_THICKFRAME;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        switch (m.Msg)
        {
            case WM_NCHITTEST:
                // 处理鼠标位置检测，用于调整窗口大小
                var point = new Point((int)m.LParam);
                point = this.PointToClient(point);

                int borderWidth = 6; // 边框宽度
                bool hitLeft = point.X <= borderWidth;
                bool hitRight = point.X >= this.ClientSize.Width - borderWidth;
                bool hitTop = point.Y <= borderWidth;
                bool hitBottom = point.Y >= this.ClientSize.Height - borderWidth;

                if (hitLeft && hitTop)
                    m.Result = (IntPtr)HTTOPLEFT;
                else if (hitRight && hitTop)
                    m.Result = (IntPtr)HTTOPRIGHT;
                else if (hitLeft && hitBottom)
                    m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (hitRight && hitBottom)
                    m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (hitLeft)
                    m.Result = (IntPtr)HTLEFT;
                else if (hitRight)
                    m.Result = (IntPtr)HTRIGHT;
                else if (hitTop)
                    m.Result = (IntPtr)HTTOP;
                else if (hitBottom)
                    m.Result = (IntPtr)HTBOTTOM;
                break;
        }
    }

    /// <summary>
    /// 应用Windows 11窗口样式（深色模式、圆角）
    /// </summary>
    private void ApplyWindows11Style()
    {
        try
        {
            // 启用深色模式
            int useImmersiveDarkMode = 1;
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
            }
            else
            {
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
            }

            // 使用系统圆角配合CSS圆角
            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // 扩展内容区域到窗口边缘，消除圆角白边
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(this.Handle, ref margins);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"应用窗口样式失败: {ex.Message}");
        }
    }

    private void ApplyRoundedRegionFallback()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var oldRegion = Region;
        Region = WindowState == FormWindowState.Maximized
            ? new Region(ClientRectangle)
            : CreateRoundedRegion(ClientSize.Width, ClientSize.Height, 20);
        oldRegion?.Dispose();
    }

    /// <summary>
    /// 窗口淡入动画
    /// </summary>
    public async void FadeIn(int duration = 100)
    {
        for (double i = 0; i <= 1; i += 0.05)
        {
            Opacity = i;
            await Task.Delay(duration / 20);
        }
        Opacity = 1;
    }

    /// <summary>
    /// 窗口最小化动画（缩小 + 淡出）
    /// </summary>
    public async void AnimateMinimize()
    {
        try
        {
            var startSize = this.Size;
            var startLocation = this.Location;
            int steps = 10;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double ease = t * t; // ease-in
                this.BeginInvoke(new Action(() =>
                {
                    this.Opacity = 1.0 - ease * 0.5;
                    int newWidth = (int)(startSize.Width * (1.0 - ease * 0.3));
                    int newHeight = (int)(startSize.Height * (1.0 - ease * 0.3));
                    int newX = startLocation.X + (startSize.Width - newWidth) / 2;
                    int newY = startLocation.Y + (startSize.Height - newHeight) / 2;
                    this.SetBounds(newX, newY, newWidth, newHeight);
                }));
                await Task.Delay(15);
            }
            this.BeginInvoke(new Action(() =>
            {
                this.WindowState = FormWindowState.Minimized;
                // 恢复原始大小和透明度
                this.Size = startSize;
                this.Location = startLocation;
                this.Opacity = 1;
            }));
        }
        catch { }
    }

    /// <summary>
    /// 创建圆角区域
    /// </summary>
    public static Region CreateRoundedRegion(int width, int height, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, radius, radius, 180, 90);
        path.AddArc(width - radius, 0, radius, radius, 270, 90);
        path.AddArc(width - radius, height - radius, radius, radius, 0, 90);
        path.AddArc(0, height - radius, radius, radius, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }
}
