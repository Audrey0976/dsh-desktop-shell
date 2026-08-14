using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("DSH Desktop Shell")]
[assembly: AssemblyDescription("Unofficial community desktop shell for DeepSeek Harness")]
[assembly: AssemblyCompany("DSH Desktop Shell contributors")]
[assembly: AssemblyProduct("DSH Desktop Shell")]
[assembly: AssemblyCopyright("Copyright © 2026 DSH Desktop Shell contributors")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0")]

namespace DshDesktopShell
{
    internal static class Program
    {
        private static Mutex instanceMutex;

        [STAThread]
        private static void Main()
        {
            bool created;
            instanceMutex = new Mutex(true, ProductInfo.MutexName, out created);
            if (!created)
            {
                NativeMethods.SignalExistingWindow(ProductInfo.Name);
                return;
            }

            NativeMethods.SetAppUserModelId(ProductInfo.AppUserModelId);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                instanceMutex.ReleaseMutex();
                instanceMutex.Dispose();
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const int WmNcCalcSize = 0x0083;
        private const int WmNcHitTest = 0x0084;
        private const int WmNcPaint = 0x0085;
        private const int WmNcActivate = 0x0086;
        private const int HtClient = 1;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private const int TitleBarHeight = 32;
        private const int TrayCommandOpen = 1001;
        private const int TrayCommandReload = 1002;
        private const int TrayCommandProject = 1003;
        private const int TrayCommandData = 1004;
        private const int TrayCommandLog = 1005;
        private const int TrayCommandCopyAddress = 1006;
        private const int TrayCommandExit = 1007;
        private const int TrayCommandRestartBackend = 1008;
        private const int TrayCommandCheckDshUpdate = 1009;
        private const int MenuCommandNewSession = 2101;
        private const int MenuCommandOpenProject = 2102;
        private const int MenuCommandOpenData = 2103;
        private const int MenuCommandHideToTray = 2104;
        private const int MenuCommandExit = 2105;
        private const int MenuCommandUndo = 2201;
        private const int MenuCommandRedo = 2202;
        private const int MenuCommandCut = 2203;
        private const int MenuCommandCopy = 2204;
        private const int MenuCommandPaste = 2205;
        private const int MenuCommandSelectAll = 2206;
        private const int MenuCommandFind = 2207;
        private const int MenuCommandToggleSidebar = 2301;
        private const int MenuCommandBack = 2302;
        private const int MenuCommandForward = 2303;
        private const int MenuCommandReload = 2304;
        private const int MenuCommandZoomIn = 2305;
        private const int MenuCommandZoomOut = 2306;
        private const int MenuCommandResetZoom = 2307;
        private const int MenuCommandDevTools = 2308;
        private const int MenuCommandProjectHome = 2401;
        private const int MenuCommandViewLog = 2402;
        private const int MenuCommandAbout = 2403;
        private const int MenuCommandCheckShellUpdate = 2404;
        private const int MenuCommandCheckDshUpdate = 2405;
        private readonly AppPaths appPaths;
        private readonly string stdoutLog;
        private readonly string webViewData;
        private readonly Panel contentHost;
        private readonly AnimatedLoadingView loadingView;
        private readonly WebView2 webView;
        private readonly MenuStrip appMenu;
        private Panel titleBar;
        private Panel windowButtons;
        private Button minimizeButton;
        private Button maximizeButton;
        private Button closeButton;
        private BootstrapClient bootstrap;
        private string appUrl;
        private string currentDshVersion;
        private NotifyIcon trayIcon;
        private ToolStripButton backButton;
        private ToolStripButton forwardButton;
        private TaskCompletionSource<bool> firstNavigation;
        private FormWindowState restoreWindowState = FormWindowState.Normal;
        private bool closing;
        private bool exitRequested;
        private bool backgroundNoticeShown;
        private bool lightTheme;
        private bool webInterfaceVisible;
        private bool updatePromptVisible;
        private bool compatibilityNoticeShown;
        private BootstrapMessageEventArgs pendingUpdateMessage;

        private enum ApplicationMenuGroup
        {
            File,
            Edit,
            View,
            Help
        }

        internal MainForm()
        {
            appPaths = AppPaths.Discover();
            stdoutLog = appPaths.BootstrapLogPath;
            webViewData = appPaths.WebViewDataDirectory;
            lightTheme = DetectInitialLightTheme(appPaths.DshHome);

            Text = ProductInfo.Name;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1440, 900);
            MinimumSize = new Size(900, 620);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = lightTheme ? Color.White : Color.FromArgb(21, 21, 23);
            ForeColor = lightTheme ? Color.FromArgb(15, 17, 21) : Color.FromArgb(245, 246, 247);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            loadingView = new AnimatedLoadingView(LoadLogoImage());
            loadingView.Dock = DockStyle.Fill;
            loadingView.ApplyTheme(lightTheme);
            loadingView.DetailText = "正在准备应用";

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.Visible = false;
            webView.DefaultBackgroundColor = BackColor;
            webView.KeyDown += HandleWebViewKeyDown;

            contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            contentHost.Margin = Padding.Empty;
            contentHost.BackColor = BackColor;
            contentHost.Controls.Add(webView);
            contentHost.Controls.Add(loadingView);

            appMenu = CreateApplicationMenu();
            appMenu.Visible = false;
            MainMenuStrip = appMenu;
            titleBar = CreateTitleBar();

            TableLayoutPanel rootLayout = new TableLayoutPanel();
            rootLayout.Dock = DockStyle.Fill;
            rootLayout.Margin = Padding.Empty;
            rootLayout.Padding = Padding.Empty;
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 2;
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleBarHeight));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
            rootLayout.Controls.Add(titleBar, 0, 0);
            rootLayout.Controls.Add(contentHost, 0, 1);
            Controls.Add(rootLayout);
            CreateTrayIcon();
            ApplyChromeTheme(lightTheme);
            Shown += HandleShown;
            FormClosing += HandleFormClosing;
            Resize += delegate { UpdateMaximizeButton(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style |= 0x00040000; // WS_THICKFRAME: native resize and DWM shadow.
                parameters.Style |= 0x00020000; // WS_MINIMIZEBOX.
                parameters.Style |= 0x00010000; // WS_MAXIMIZEBOX.
                parameters.Style |= 0x00080000; // WS_SYSMENU.
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.SetDarkTitleBar(Handle, !lightTheme);
            NativeMethods.ApplyNativeMenuTheme(Handle, !lightTheme);
            NativeMethods.EnableRoundedCorners(Handle);
            NativeMethods.RemoveStandardFrameInset(Handle);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.ShowWindowMessage)
            {
                RestoreFromTray();
                return;
            }
            if (message.Msg == WmNcCalcSize)
            {
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == WmNcPaint)
            {
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == WmNcActivate)
            {
                message.Result = (IntPtr)1;
                return;
            }
            if (message.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref message);
                if (message.Result.ToInt32() == HtClient)
                {
                    long packed = message.LParam.ToInt64();
                    Point client = PointToClient(new Point(
                        unchecked((short)(packed & 0xffff)),
                        unchecked((short)((packed >> 16) & 0xffff))));
                    int grip = Math.Max(7, DeviceDpi * 7 / 96);
                    bool left = client.X <= grip;
                    bool right = client.X >= ClientSize.Width - grip;
                    bool top = client.Y <= grip;
                    bool bottom = client.Y >= ClientSize.Height - grip;
                    if (left && top) message.Result = (IntPtr)HtTopLeft;
                    else if (right && top) message.Result = (IntPtr)HtTopRight;
                    else if (left && bottom) message.Result = (IntPtr)HtBottomLeft;
                    else if (right && bottom) message.Result = (IntPtr)HtBottomRight;
                    else if (left) message.Result = (IntPtr)HtLeft;
                    else if (right) message.Result = (IntPtr)HtRight;
                    else if (top) message.Result = (IntPtr)HtTop;
                    else if (bottom) message.Result = (IntPtr)HtBottom;
                }
                return;
            }
            base.WndProc(ref message);
        }

        private static bool DetectInitialLightTheme(string root)
        {
            try
            {
                string settingsPath = Path.Combine(root, "settings.yaml");
                if (File.Exists(settingsPath))
                {
                    bool insideTheme = false;
                    foreach (string line in File.ReadAllLines(settingsPath))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                        bool topLevel = line.Length == line.TrimStart().Length;
                        if (topLevel)
                        {
                            insideTheme = trimmed.Equals("ui-theme:", StringComparison.OrdinalIgnoreCase);
                            continue;
                        }
                        if (!insideTheme || !trimmed.StartsWith("preference:", StringComparison.OrdinalIgnoreCase)) continue;
                        string preference = trimmed.Substring("preference:".Length).Trim().Trim('"', '\'');
                        if (preference.Equals("light", StringComparison.OrdinalIgnoreCase)) return true;
                        if (preference.Equals("dark", StringComparison.OrdinalIgnoreCase)) return false;
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value != null) return Convert.ToInt32(value) != 0;
                }
            }
            catch
            {
            }
            return false;
        }

        private void ApplyChromeTheme(bool useLightTheme)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(ApplyChromeTheme), useLightTheme);
                return;
            }

            lightTheme = useLightTheme;
            Color baseBackground = lightTheme ? Color.White : Color.FromArgb(21, 21, 23);
            Color barBackground = lightTheme ? Color.FromArgb(249, 250, 251) : Color.FromArgb(27, 27, 28);
            Color menuBackground = lightTheme ? Color.White : Color.FromArgb(35, 35, 36);
            Color primaryText = lightTheme ? Color.FromArgb(15, 17, 21) : Color.FromArgb(245, 246, 247);
            Color mutedText = lightTheme ? Color.FromArgb(97, 102, 107) : Color.FromArgb(151, 157, 166);

            BackColor = baseBackground;
            ForeColor = primaryText;
            contentHost.BackColor = baseBackground;
            loadingView.ApplyTheme(lightTheme);
            webView.DefaultBackgroundColor = baseBackground;

            if (appMenu != null)
            {
                appMenu.BackColor = barBackground;
                appMenu.ForeColor = primaryText;
                appMenu.Renderer = new DarkMenuRenderer(lightTheme);
                foreach (ToolStripItem item in appMenu.Items)
                {
                    ApplyMenuItemTheme(item, lightTheme, primaryText, mutedText, menuBackground);
                }
            }

            if (titleBar != null)
            {
                titleBar.BackColor = barBackground;
                Color hover = lightTheme ? Color.FromArgb(233, 236, 242) : Color.FromArgb(53, 54, 56);
                Color pressedHover = lightTheme ? Color.FromArgb(220, 224, 229) : Color.FromArgb(67, 69, 74);
                if (windowButtons != null) windowButtons.BackColor = barBackground;
                foreach (Button button in new Button[] { minimizeButton, maximizeButton, closeButton })
                {
                    if (button == null) continue;
                    button.BackColor = barBackground;
                    button.ForeColor = primaryText;
                    bool close = String.Equals(button.Tag as string, "close", StringComparison.Ordinal);
                    button.FlatAppearance.MouseOverBackColor = close ? Color.FromArgb(196, 43, 28) : hover;
                    button.FlatAppearance.MouseDownBackColor = close ? Color.FromArgb(165, 35, 24) : pressedHover;
                    button.FlatAppearance.BorderColor = barBackground;
                }
            }

            if (IsHandleCreated)
            {
                NativeMethods.SetDarkTitleBar(Handle, !lightTheme);
                NativeMethods.ApplyNativeMenuTheme(Handle, !lightTheme);
            }
            Invalidate(true);
        }

        private void ApplyMenuItemTheme(ToolStripItem item, bool useLightTheme, Color primaryText,
            Color mutedText, Color menuBackground)
        {
            ToolStripSeparator separator = item as ToolStripSeparator;
            if (separator != null) return;

            ToolStripLabel label = item as ToolStripLabel;
            if (label != null && !(item is ToolStripButton))
            {
                label.ForeColor = mutedText;
                return;
            }

            bool danger = String.Equals(item.Tag as string, "danger", StringComparison.Ordinal)
                || item.Text.StartsWith("退出", StringComparison.Ordinal);
            item.ForeColor = danger
                ? (useLightTheme ? Color.FromArgb(194, 54, 66) : Color.FromArgb(244, 204, 207))
                : primaryText;

            ToolStripMenuItem menuItem = item as ToolStripMenuItem;
            if (menuItem == null) return;
            menuItem.DropDown.BackColor = menuBackground;
            menuItem.DropDown.ForeColor = primaryText;
            menuItem.DropDown.Renderer = new DarkMenuRenderer(useLightTheme);
            foreach (ToolStripItem child in menuItem.DropDownItems)
            {
                ApplyMenuItemTheme(child, useLightTheme, primaryText, mutedText, menuBackground);
            }
        }

        private Panel CreateTitleBar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Fill;
            bar.Margin = Padding.Empty;
            bar.Padding = Padding.Empty;
            bar.MouseDown += HandleTitleBarMouseDown;

            windowButtons = new Panel();
            windowButtons.Dock = DockStyle.Right;
            windowButtons.Width = 138;
            windowButtons.Margin = Padding.Empty;
            windowButtons.Padding = Padding.Empty;
            windowButtons.Paint += delegate(object paintSender, PaintEventArgs paint)
            {
                Color line = lightTheme ? Color.FromArgb(225, 229, 238) : Color.FromArgb(60, 60, 61);
                using (Pen pen = new Pen(line))
                {
                    paint.Graphics.DrawLine(pen, 0, windowButtons.Height - 1,
                        windowButtons.Width, windowButtons.Height - 1);
                }
            };

            minimizeButton = CreateWindowButton("\uE921", "最小化", false);
            maximizeButton = CreateWindowButton("\uE922", "最大化", false);
            closeButton = CreateWindowButton("\uE8BB", "关闭到系统托盘", true);
            minimizeButton.Location = new Point(0, 0);
            maximizeButton.Location = new Point(46, 0);
            closeButton.Location = new Point(92, 0);
            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            maximizeButton.Click += delegate { ToggleMaximize(); };
            closeButton.Click += delegate { Close(); };
            windowButtons.Controls.Add(minimizeButton);
            windowButtons.Controls.Add(maximizeButton);
            windowButtons.Controls.Add(closeButton);

            appMenu.MouseDown += HandleTitleBarMouseDown;
            bar.Controls.Add(appMenu);
            bar.Controls.Add(windowButtons);
            windowButtons.BringToFront();
            return bar;
        }

        private static Button CreateWindowButton(string glyph, string accessibleName, bool close)
        {
            Button button = new Button();
            button.Size = new Size(46, TitleBarHeight);
            button.Margin = Padding.Empty;
            button.Padding = Padding.Empty;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.Transparent;
            button.ForeColor = Color.FromArgb(218, 222, 230);
            button.Font = new Font("Segoe Fluent Icons", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
            button.Text = glyph;
            button.AccessibleName = accessibleName;
            button.TabStop = false;
            button.Tag = close ? "close" : null;
            return button;
        }

        private void HandleTitleBarMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            MenuStrip menu = sender as MenuStrip;
            if (menu != null && menu.GetItemAt(e.Location) != null) return;
            if (e.Clicks >= 2)
            {
                ToggleMaximize();
                return;
            }
            NativeMethods.BeginWindowDrag(Handle);
        }

        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
            }
            else
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            UpdateMaximizeButton();
        }

        private void UpdateMaximizeButton()
        {
            if (maximizeButton == null) return;
            maximizeButton.Text = WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922";
            maximizeButton.AccessibleName = WindowState == FormWindowState.Maximized ? "还原" : "最大化";
        }

        private MenuStrip CreateApplicationMenu()
        {
            MenuStrip menu = new MenuStrip();
            menu.Dock = DockStyle.Fill;
            menu.AutoSize = false;
            menu.Height = TitleBarHeight;
            menu.BackColor = Color.FromArgb(28, 31, 36);
            menu.ForeColor = Color.FromArgb(224, 228, 235);
            menu.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            menu.GripStyle = ToolStripGripStyle.Hidden;
            menu.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
            menu.Padding = new Padding(12, 0, 146, 0);
            menu.Renderer = new DarkMenuRenderer(lightTheme);
            menu.ShowItemToolTips = true;

            backButton = CreateNavigationButton("\uE72B", "后退");
            backButton.Enabled = false;
            backButton.Click += delegate { NavigateBack(); };
            menu.Items.Add(backButton);

            forwardButton = CreateNavigationButton("\uE72A", "前进");
            forwardButton.Enabled = false;
            forwardButton.Click += delegate { NavigateForward(); };
            menu.Items.Add(forwardButton);

            ToolStripSeparator navigationSeparator = new ToolStripSeparator();
            navigationSeparator.AutoSize = false;
            navigationSeparator.Size = new Size(13, 22);
            navigationSeparator.Margin = new Padding(4, 5, 5, 5);
            menu.Items.Add(navigationSeparator);

            ToolStripMenuItem fileMenu = CreateTopLevelMenu("文件");
            fileMenu.Tag = ApplicationMenuGroup.File;
            fileMenu.Click += delegate { ShowNativeApplicationMenu(ApplicationMenuGroup.File); };
            menu.Items.Add(fileMenu);

            ToolStripMenuItem editMenu = CreateTopLevelMenu("编辑");
            editMenu.Tag = ApplicationMenuGroup.Edit;
            editMenu.Click += delegate { ShowNativeApplicationMenu(ApplicationMenuGroup.Edit); };
            menu.Items.Add(editMenu);

            ToolStripMenuItem viewMenu = CreateTopLevelMenu("视图");
            viewMenu.Tag = ApplicationMenuGroup.View;
            viewMenu.Click += delegate { ShowNativeApplicationMenu(ApplicationMenuGroup.View); };
            menu.Items.Add(viewMenu);

            ToolStripMenuItem helpMenu = CreateTopLevelMenu("帮助");
            helpMenu.Tag = ApplicationMenuGroup.Help;
            helpMenu.Click += delegate { ShowNativeApplicationMenu(ApplicationMenuGroup.Help); };
            menu.Items.Add(helpMenu);

            return menu;
        }

        private static ToolStripButton CreateNavigationButton(string glyph, string toolTip)
        {
            ToolStripButton button = new ToolStripButton();
            button.AutoSize = false;
            button.Size = new Size(32, 28);
            button.Margin = new Padding(1, 2, 1, 2);
            button.Padding = Padding.Empty;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Text = glyph;
            button.ToolTipText = toolTip;
            button.ForeColor = Color.FromArgb(188, 194, 205);
            button.Font = new Font("Segoe Fluent Icons", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            return button;
        }

        private static ToolStripMenuItem CreateTopLevelMenu(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.AutoSize = false;
            item.Size = new Size(52, 28);
            item.Margin = new Padding(0, 2, 0, 2);
            item.Padding = new Padding(7, 0, 7, 0);
            item.ForeColor = Color.FromArgb(218, 222, 230);
            return item;
        }

        private void ShowNativeApplicationMenu(ApplicationMenuGroup group)
        {
            if (!IsHandleCreated || appMenu == null || !appMenu.Visible) return;
            ToolStripItem anchor = null;
            foreach (ToolStripItem item in appMenu.Items)
            {
                if (item.Tag is ApplicationMenuGroup && (ApplicationMenuGroup)item.Tag == group)
                {
                    anchor = item;
                    break;
                }
            }
            if (anchor == null) return;

            NativeMethods.ApplyNativeMenuTheme(Handle, !lightTheme);
            IntPtr menu = NativeMethods.CreateNativePopupMenu();
            if (menu == IntPtr.Zero) return;
            try
            {
                PopulateNativeApplicationMenu(menu, group);
                Point popup = appMenu.PointToScreen(new Point(anchor.Bounds.Left, anchor.Bounds.Bottom));
                int command = NativeMethods.ShowNativePopupMenuAt(menu, Handle, popup);
                if (command != 0) ExecuteNativeApplicationCommand(command);
            }
            finally
            {
                NativeMethods.DestroyNativePopupMenu(menu);
                appMenu.Invalidate();
            }
        }

        private void PopulateNativeApplicationMenu(IntPtr menu, ApplicationMenuGroup group)
        {
            bool ready = webView.CoreWebView2 != null;
            switch (group)
            {
                case ApplicationMenuGroup.File:
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandNewSession, "新建会话\tCtrl+N", ready);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandOpenProject, "打开应用目录", true);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandOpenData, "打开 Harness 数据目录", true);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandHideToTray, "隐藏到系统托盘\tCtrl+Shift+H", true);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandExit, "退出 DSH Desktop Shell\tCtrl+Q", true);
                    break;
                case ApplicationMenuGroup.Edit:
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandUndo, "撤销\tCtrl+Z", ready);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandRedo, "重做\tCtrl+Y", ready);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandCut, "剪切\tCtrl+X", ready);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandCopy, "复制\tCtrl+C", ready);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandPaste, "粘贴\tCtrl+V", ready);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandSelectAll, "全选\tCtrl+A", ready);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandFind, "在页面中查找\tCtrl+F", ready);
                    break;
                case ApplicationMenuGroup.View:
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandToggleSidebar, "切换侧栏\tCtrl+B", ready);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandBack, "后退\tAlt+Left", ready && webView.CoreWebView2.CanGoBack);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandForward, "前进\tAlt+Right", ready && webView.CoreWebView2.CanGoForward);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandReload, "重新加载界面\tCtrl+R", ready);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandZoomIn, "放大\tCtrl++", ready);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandZoomOut, "缩小\tCtrl+-", ready);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandResetZoom, "恢复默认缩放\tCtrl+0", ready);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandDevTools, "开发者工具\tF12", ready);
                    break;
                case ApplicationMenuGroup.Help:
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandProjectHome, "DeepSeek Harness 项目主页", true);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandViewLog, "查看后台日志", true);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandCheckDshUpdate, "检查 dsh 更新", bootstrap != null && bootstrap.IsRunning);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandCheckShellUpdate, "检查桌面壳更新", true);
                    NativeMethods.AppendNativeMenuSeparator(menu);
                    NativeMethods.AppendNativeMenuItem(menu, MenuCommandAbout, "关于 DSH Desktop Shell", true);
                    break;
            }
        }

        private async void ExecuteNativeApplicationCommand(int command)
        {
            switch (command)
            {
                case MenuCommandNewSession:
                    bool newSession = await ClickPageButtonAsync(new string[]
                    {
                        "新对话", "新会话", "新建会话", "new chat", "new conversation", "new session"
                    });
                    if (!newSession) ShowTransientNotice("当前界面没有找到“新建会话”按钮。");
                    break;
                case MenuCommandOpenProject:
                    OpenPath(appPaths.ApplicationDirectory);
                    break;
                case MenuCommandOpenData:
                    Directory.CreateDirectory(appPaths.DshHome);
                    OpenPath(appPaths.DshHome);
                    break;
                case MenuCommandHideToTray:
                    HideToTray();
                    break;
                case MenuCommandExit:
                    ExitFromTray();
                    break;
                case MenuCommandUndo:
                    SendWebShortcut("^z");
                    break;
                case MenuCommandRedo:
                    SendWebShortcut("^y");
                    break;
                case MenuCommandCut:
                    SendWebShortcut("^x");
                    break;
                case MenuCommandCopy:
                    SendWebShortcut("^c");
                    break;
                case MenuCommandPaste:
                    SendWebShortcut("^v");
                    break;
                case MenuCommandSelectAll:
                    SendWebShortcut("^a");
                    break;
                case MenuCommandFind:
                    SendWebShortcut("^f");
                    break;
                case MenuCommandToggleSidebar:
                    bool toggled = await ClickPageButtonAsync(new string[]
                    {
                        "切换侧栏", "切换边栏", "打开侧边栏", "收起侧边栏",
                        "收起侧栏", "展开侧栏",
                        "toggle sidebar", "open sidebar", "collapse sidebar", "expand sidebar",
                        "show sidebar", "hide sidebar"
                    });
                    if (!toggled) ShowTransientNotice("当前界面没有找到可切换的侧栏按钮。");
                    break;
                case MenuCommandBack:
                    NavigateBack();
                    break;
                case MenuCommandForward:
                    NavigateForward();
                    break;
                case MenuCommandReload:
                    ReloadWebView();
                    break;
                case MenuCommandZoomIn:
                    ChangeZoom(0.10);
                    break;
                case MenuCommandZoomOut:
                    ChangeZoom(-0.10);
                    break;
                case MenuCommandResetZoom:
                    ResetZoom();
                    break;
                case MenuCommandDevTools:
                    if (webView.CoreWebView2 != null) webView.CoreWebView2.OpenDevToolsWindow();
                    break;
                case MenuCommandProjectHome:
                    OpenExternal(ProductInfo.UpstreamUrl);
                    break;
                case MenuCommandViewLog:
                    OpenLog();
                    break;
                case MenuCommandCheckDshUpdate:
                    if (bootstrap != null && bootstrap.IsRunning)
                    {
                        ShowTransientNotice("正在检查 dsh 更新…");
                        bootstrap.RequestUpdateCheck();
                    }
                    break;
                case MenuCommandCheckShellUpdate:
                    CheckShellUpdate();
                    break;
                case MenuCommandAbout:
                    ShowAbout();
                    break;
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F)) { ShowNativeApplicationMenu(ApplicationMenuGroup.File); return true; }
            if (keyData == (Keys.Alt | Keys.E)) { ShowNativeApplicationMenu(ApplicationMenuGroup.Edit); return true; }
            if (keyData == (Keys.Alt | Keys.V)) { ShowNativeApplicationMenu(ApplicationMenuGroup.View); return true; }
            if (keyData == (Keys.Alt | Keys.H)) { ShowNativeApplicationMenu(ApplicationMenuGroup.Help); return true; }
            if (keyData == (Keys.Control | Keys.N)) { ExecuteNativeApplicationCommand(MenuCommandNewSession); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.H)) { HideToTray(); return true; }
            if (keyData == (Keys.Control | Keys.Q)) { ExitFromTray(); return true; }
            if (keyData == (Keys.Control | Keys.B)) { ExecuteNativeApplicationCommand(MenuCommandToggleSidebar); return true; }
            if (keyData == (Keys.Alt | Keys.Left)) { NavigateBack(); return true; }
            if (keyData == (Keys.Alt | Keys.Right)) { NavigateForward(); return true; }
            if (keyData == (Keys.Control | Keys.R)) { ReloadWebView(); return true; }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add)) { ChangeZoom(0.10); return true; }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract)) { ChangeZoom(-0.10); return true; }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { ResetZoom(); return true; }
            if (keyData == Keys.F12)
            {
                if (webView.CoreWebView2 != null) webView.CoreWebView2.OpenDevToolsWindow();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void HandleWebViewKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control || e.Alt || e.Shift || e.KeyCode != Keys.B) return;

            // WebView2 owns keyboard focus after the setup dialog is dismissed, so
            // the Form-level ProcessCmdKey path is no longer guaranteed to run.
            // Suppress the browser accelerator synchronously, then queue WebView2
            // work until after its accelerator callback has returned.
            e.Handled = true;
            e.SuppressKeyPress = true;
            BeginInvoke(new Action(delegate
            {
                ExecuteNativeApplicationCommand(MenuCommandToggleSidebar);
            }));
        }

        private static ToolStripMenuItem CreateAppMenuItem(string text, Keys shortcut)
        {
            return CreateAppMenuItem(text, shortcut, null);
        }

        private static ToolStripMenuItem CreateAppMenuItem(string text, Keys shortcut, EventHandler click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.AutoSize = false;
            item.Size = new Size(286, 38);
            item.Margin = Padding.Empty;
            item.Padding = new Padding(13, 0, 13, 0);
            item.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            item.ForeColor = Color.FromArgb(232, 235, 241);
            if (shortcut != Keys.None)
            {
                item.ShortcutKeys = shortcut;
                item.ShowShortcutKeys = true;
            }
            if (click != null) item.Click += click;
            return item;
        }

        private ToolStripMenuItem CreateWebShortcutItem(string text, string shortcutDisplay, string keys)
        {
            ToolStripMenuItem item = CreateAppMenuItem(text, Keys.None);
            item.ShortcutKeyDisplayString = shortcutDisplay;
            item.Click += delegate { SendWebShortcut(keys); };
            return item;
        }

        private static ToolStripSeparator CreateAppSeparator()
        {
            ToolStripSeparator separator = new ToolStripSeparator();
            separator.AutoSize = false;
            separator.Size = new Size(286, 9);
            separator.Margin = Padding.Empty;
            return separator;
        }

        private void ConfigureDropDown(ToolStripMenuItem menu)
        {
            menu.DropDown.AutoSize = true;
            menu.DropDown.MinimumSize = new Size(300, 0);
            menu.DropDown.Padding = new Padding(6, 7, 6, 7);
            menu.DropDown.BackColor = Color.FromArgb(31, 34, 40);
            menu.DropDown.ForeColor = Color.FromArgb(232, 235, 241);
            menu.DropDown.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            menu.DropDown.Renderer = new DarkMenuRenderer(lightTheme);
        }

        private async Task<bool> ClickPageButtonAsync(string[] labels)
        {
            if (webView.CoreWebView2 == null) return false;
            StringBuilder labelArray = new StringBuilder();
            for (int index = 0; index < labels.Length; index++)
            {
                if (index > 0) labelArray.Append(',');
                labelArray.Append('"');
                labelArray.Append(EscapeJavaScriptString(labels[index].ToLowerInvariant()));
                labelArray.Append('"');
            }

            string script = "(function(){"
                + "var labels=[" + labelArray + "];"
                + "var nodes=document.querySelectorAll('button,[role=\\\"button\\\"],a');"
                + "for(var i=0;i<nodes.length;i++){var node=nodes[i];"
                + "var values=[node.getAttribute('aria-label'),node.getAttribute('title'),node.textContent];"
                + "for(var j=0;j<values.length;j++){var value=(values[j]||'').trim().toLowerCase();"
                + "if(!value)continue;for(var k=0;k<labels.length;k++){"
                + "if(value===labels[k]||value.indexOf(labels[k])>=0){node.click();return true;}}}}"
                + "return false;})()";
            try
            {
                string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                return String.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string EscapeJavaScriptString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private void SendWebShortcut(string keys)
        {
            if (webView.CoreWebView2 == null) return;
            webView.Focus();
            BeginInvoke(new Action(delegate { SendKeys.SendWait(keys); }));
        }

        private void NavigateBack()
        {
            if (webView.CoreWebView2 != null && webView.CoreWebView2.CanGoBack) webView.CoreWebView2.GoBack();
        }

        private void NavigateForward()
        {
            if (webView.CoreWebView2 != null && webView.CoreWebView2.CanGoForward) webView.CoreWebView2.GoForward();
        }

        private void ReloadWebView()
        {
            if (webView.CoreWebView2 != null) webView.CoreWebView2.Reload();
        }

        private void ChangeZoom(double delta)
        {
            if (webView.CoreWebView2 == null) return;
            webView.ZoomFactor = Math.Max(0.50, Math.Min(2.50, webView.ZoomFactor + delta));
        }

        private void ResetZoom()
        {
            if (webView.CoreWebView2 != null) webView.ZoomFactor = 1.0;
        }

        private void UpdateNavigationUi()
        {
            bool ready = webView.CoreWebView2 != null;
            if (backButton != null) backButton.Enabled = ready && webView.CoreWebView2.CanGoBack;
            if (forwardButton != null) forwardButton.Enabled = ready && webView.CoreWebView2.CanGoForward;
        }

        private void OpenLog()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stdoutLog));
            if (!File.Exists(stdoutLog)) File.WriteAllText(stdoutLog, String.Empty);
            OpenPath(stdoutLog);
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                ProductInfo.Name + "\r\n版本 " + ProductInfo.Version
                + "\r\n内置 dsh " + ProductInfo.BundledDshVersion
                + "\r\n\r\nUnofficial community desktop shell\r\n"
                + "本项目与 DeepSeek 官方没有隶属或背书关系。",
                "关于 " + ProductInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowTransientNotice(string message)
        {
            if (trayIcon != null) trayIcon.ShowBalloonTip(2200, ProductInfo.Name, message, ToolTipIcon.Info);
        }

        private async void CheckShellUpdate()
        {
            try
            {
                ShellUpdateResult result = await ShellUpdateChecker.CheckAsync();
                if (!result.UpdateAvailable)
                {
                    MessageBox.Show(this, "当前已是最新桌面壳版本。", ProductInfo.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult answer = MessageBox.Show(this,
                    "发现 DSH Desktop Shell " + result.Version
                    + "。\r\n\r\n本应用不会静默替换未签名程序。是否打开 GitHub Release 下载页？",
                    "桌面壳更新可用", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes) OpenExternal(result.Url ?? ProductInfo.ReleasesUrl);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "暂时无法检查桌面壳更新：" + error.Message, ProductInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void HandleShown(object sender, EventArgs e)
        {
            Task webViewPreparation = null;
            try
            {
                ValidateInstallation();
                appPaths.EnsureDirectories();

                // WebView2 and dsh perform independent cold-start work. Running them
                // in parallel removes the serial startup delay of the legacy shell.
                webViewPreparation = PrepareWebViewAsync();
                bootstrap = new BootstrapClient(appPaths);
                bootstrap.MessageReceived += HandleBootstrapMessage;
                bootstrap.Exited += HandleBootstrapExited;
                SetStatus("正在启动本地服务");
                BootstrapReady ready = await bootstrap.StartAndWaitForReadyAsync(TimeSpan.FromSeconds(100));
                appUrl = ready.Url;
                currentDshVersion = ready.Version;

                SetStatus("正在加载工作区");
                await webViewPreparation;
                firstNavigation = new TaskCompletionSource<bool>();
                webView.NavigationCompleted += HandleFirstNavigationCompleted;
                webView.Source = new Uri(appUrl);
                Task navigationTimeout = Task.Delay(15000);
                Task navigationResult = await Task.WhenAny(firstNavigation.Task, navigationTimeout);
                if (navigationResult != firstNavigation.Task)
                {
                    throw new TimeoutException("应用界面加载超时，请查看日志后重试。");
                }
                await firstNavigation.Task;

                loadingView.StopAnimation();
                loadingView.Visible = false;
                appMenu.Visible = true;
                webView.Visible = true;
                webView.BringToFront();
                appMenu.BringToFront();
                webInterfaceVisible = true;
                if (pendingUpdateMessage != null)
                {
                    BootstrapMessageEventArgs pending = pendingUpdateMessage;
                    pendingUpdateMessage = null;
                    HandleUpdateAvailable(pending);
                }
            }
            catch (Exception error)
            {
                if (webViewPreparation != null && webViewPreparation.IsFaulted)
                {
                    error = webViewPreparation.Exception.GetBaseException();
                }
                ShowStartupError(error);
            }
        }

        private void ValidateInstallation()
        {
            if (!File.Exists(appPaths.NodeExecutable))
            {
                throw new FileNotFoundException("找不到应用内置的 Node.js，请重新安装 DSH Desktop Shell。", appPaths.NodeExecutable);
            }
            if (!File.Exists(appPaths.BootstrapScript))
            {
                throw new FileNotFoundException("找不到 runtime bootstrap，请重新安装 DSH Desktop Shell。", appPaths.BootstrapScript);
            }
            string bundledCli = Path.Combine(appPaths.BundledDshDirectory,
                @"node_modules\@deepseek-ai\dsh\lib\bin.js");
            if (!File.Exists(bundledCli) || !File.Exists(appPaths.BundledManifest))
            {
                throw new FileNotFoundException("找不到经过验证的内置 dsh 运行时，请重新安装应用。", bundledCli);
            }
        }

        private void HandleBootstrapMessage(object sender, BootstrapMessageEventArgs message)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate { HandleBootstrapMessageOnUi(message); }));
            }
            catch (InvalidOperationException) { }
        }

        private void HandleBootstrapMessageOnUi(BootstrapMessageEventArgs message)
        {
            if (String.Equals(message.Type, "status", StringComparison.Ordinal))
            {
                SetStatus(message.Message ?? message.Phase ?? "正在准备应用");
                if (!String.IsNullOrEmpty(message.Phase)
                    && message.Phase.StartsWith("update-", StringComparison.Ordinal)
                    && !String.Equals(message.Phase, "update-check", StringComparison.Ordinal)
                    && !String.Equals(message.Phase, "update-offline", StringComparison.Ordinal)
                    && !String.Equals(message.Phase, "update-complete", StringComparison.Ordinal))
                {
                    ShowLoadingSurface();
                }
                if (String.Equals(message.Phase, "update-complete", StringComparison.Ordinal))
                {
                    ShowTransientNotice("dsh 已更新到 " + (message.CandidateVersion ?? message.Version) + "。");
                }
                else if (String.Equals(message.Phase, "update-current", StringComparison.Ordinal))
                {
                    ShowTransientNotice("当前已是最新的兼容 dsh 版本。");
                }
                else if (String.Equals(message.Phase, "update-failed", StringComparison.Ordinal))
                {
                    ShowTransientNotice("dsh 更新安装失败，已继续使用当前版本；可查看后台日志。");
                }
                return;
            }
            if (String.Equals(message.Type, "ready", StringComparison.Ordinal))
            {
                bool originChanged = !String.Equals(appUrl, message.Url, StringComparison.OrdinalIgnoreCase);
                appUrl = message.Url;
                currentDshVersion = message.Version;
                if (webInterfaceVisible && webView.CoreWebView2 != null)
                {
                    if (originChanged) webView.CoreWebView2.Navigate(appUrl);
                    else webView.CoreWebView2.Reload();
                    ShowWebInterface();
                }
                return;
            }
            if (String.Equals(message.Type, "update-available", StringComparison.Ordinal))
            {
                if (!webInterfaceVisible) pendingUpdateMessage = message;
                else HandleUpdateAvailable(message);
                return;
            }
            if (String.Equals(message.Type, "fatal", StringComparison.Ordinal))
            {
                ShowLoadingSurface();
                string failure = message.Message ?? "本地 dsh 服务意外停止。";
                MessageBox.Show(this, failure + "\r\n\r\n日志：" + stdoutLog,
                    ProductInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void HandleBootstrapExited(object sender, EventArgs e)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (closing) return;
                    ShowLoadingSurface();
                    SetStatus("本地服务已停止");
                    if (trayIcon != null)
                    {
                        trayIcon.ShowBalloonTip(4000, ProductInfo.Name,
                            "本地 dsh 服务已停止，请从托盘菜单退出后重新打开。", ToolTipIcon.Error);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void HandleUpdateAvailable(BootstrapMessageEventArgs message)
        {
            if (updatePromptVisible || String.IsNullOrWhiteSpace(message.CandidateVersion)) return;
            if (message.Compatible)
            {
                updatePromptVisible = true;
                try
                {
                    DialogResult result = MessageBox.Show(this,
                        "发现兼容的 dsh 更新：\r\n\r\n"
                        + (message.CurrentVersion ?? currentDshVersion) + "  →  " + message.CandidateVersion
                        + "\r\n\r\n安装后本地后台任务会重启，当前会话和凭据不会被删除。现在更新吗？",
                        "dsh 更新可用", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result == DialogResult.Yes && bootstrap != null)
                    {
                        ShowLoadingSurface();
                        bootstrap.RequestUpdate(message.CandidateVersion);
                    }
                }
                finally { updatePromptVisible = false; }
            }
            else if (message.RequiresShellUpdate && !compatibilityNoticeShown)
            {
                compatibilityNoticeShown = true;
                DialogResult result = MessageBox.Show(this,
                    "npm 上已出现兼容边界之外的 dsh " + message.CandidateVersion
                    + "。为了避免界面损坏，本应用没有自动安装它。\r\n\r\n是否打开 DSH Desktop Shell 发布页检查新版？",
                    "需要更新桌面壳", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes) OpenExternal(ProductInfo.ReleasesUrl);
            }
        }

        private void ShowLoadingSurface()
        {
            webView.Visible = false;
            loadingView.Visible = true;
            loadingView.StartAnimation();
            loadingView.BringToFront();
        }

        private void ShowWebInterface()
        {
            loadingView.StopAnimation();
            loadingView.Visible = false;
            webView.Visible = true;
            webView.BringToFront();
            webInterfaceVisible = true;
        }

        private async Task PrepareWebViewAsync()
        {
            Directory.CreateDirectory(webViewData);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, webViewData);
            await webView.EnsureCoreWebView2Async(environment);
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            webView.CoreWebView2.NewWindowRequested += HandleNewWindowRequested;
            webView.CoreWebView2.NavigationStarting += HandleNavigationStarting;
            webView.CoreWebView2.HistoryChanged += delegate { UpdateNavigationUi(); };
            webView.CoreWebView2.NavigationCompleted += delegate { UpdateNavigationUi(); };
            webView.CoreWebView2.WebMessageReceived += HandleWebMessageReceived;
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "(function(){" +
                "function sendTheme(){if(!document.body)return;" +
                "window.chrome.webview.postMessage(document.body.hasAttribute('data-ds-dark-theme')" +
                "?'dsh-theme:dark':'dsh-theme:light');}" +
                "function attach(){if(!document.body)return;sendTheme();" +
                "new MutationObserver(sendTheme).observe(document.body," +
                "{attributes:true,attributeFilter:['data-ds-dark-theme']});}" +
                "if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',attach,{once:true});" +
                "else attach();})();");
            UpdateNavigationUi();
        }

        private void HandleWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();
                if (String.Equals(message, "dsh-theme:light", StringComparison.Ordinal))
                {
                    ApplyChromeTheme(true);
                }
                else if (String.Equals(message, "dsh-theme:dark", StringComparison.Ordinal))
                {
                    ApplyChromeTheme(false);
                }
            }
            catch
            {
            }
        }

        private void HandleFirstNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (firstNavigation == null || firstNavigation.Task.IsCompleted) return;
            if (e.IsSuccess)
            {
                firstNavigation.SetResult(true);
            }
            else
            {
                firstNavigation.SetException(new InvalidOperationException("应用界面加载失败：" + e.WebErrorStatus));
            }
        }

        private void HandleNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (IsLocalAppUri(e.Uri))
            {
                webView.CoreWebView2.Navigate(e.Uri);
            }
            else
            {
                OpenExternal(e.Uri);
            }
        }

        private void HandleNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsLocalAppUri(e.Uri) || e.Uri == "about:blank") return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        }

        private bool IsLocalAppUri(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            Uri expected;
            if (String.IsNullOrWhiteSpace(appUrl) || !Uri.TryCreate(appUrl, UriKind.Absolute, out expected)) return false;
            return uri.Scheme == Uri.UriSchemeHttp
                && String.Equals(uri.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
                && uri.Port == expected.Port;
        }

        private static void OpenExternal(string value)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(value);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch
            {
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(path);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch
            {
            }
        }

        private void SetStatus(string message)
        {
            loadingView.DetailText = message;
            loadingView.AccessibleDescription = message;
        }

        private void ShowStartupError(Exception error)
        {
            webView.Visible = false;
            loadingView.Visible = true;
            loadingView.DetailText = "启动失败：" + error.Message;
            loadingView.StartAnimation();
            loadingView.BringToFront();
            MessageBox.Show(this, error.Message + "\r\n\r\n日志：" + stdoutLog,
                ProductInfo.Name + " 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void CreateTrayIcon()
        {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = Icon;
            trayIcon.Text = ProductInfo.Name;
            trayIcon.Visible = true;
            trayIcon.MouseClick += delegate(object mouseSender, MouseEventArgs mouse)
            {
                if (mouse.Button == MouseButtons.Left) RestoreFromTray();
                else if (mouse.Button == MouseButtons.Right) ShowNativeTrayMenu();
            };
        }

        private void ShowNativeTrayMenu()
        {
            NativeMethods.ApplyNativeMenuTheme(Handle, !lightTheme);
            IntPtr menu = NativeMethods.CreateNativePopupMenu();
            if (menu == IntPtr.Zero) return;
            try
            {
                bool runtimeReady = bootstrap != null && bootstrap.IsRunning && !String.IsNullOrWhiteSpace(appUrl);
                bool reloadEnabled = webView.CoreWebView2 != null && runtimeReady;
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandOpen, "打开 DSH Desktop Shell", true);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandReload, "重新加载界面", reloadEnabled);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandRestartBackend, "重启本地 dsh 服务", runtimeReady);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandCheckDshUpdate, "检查 dsh 更新", runtimeReady);
                NativeMethods.AppendNativeMenuSeparator(menu);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandProject, "打开应用目录", true);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandData, "打开 Harness 数据目录", true);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandLog, "查看后台日志", true);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandCopyAddress, "复制本地访问地址", runtimeReady);
                NativeMethods.AppendNativeMenuSeparator(menu);
                NativeMethods.AppendNativeMenuItem(menu, TrayCommandExit, "退出 DSH Desktop Shell", true);
                NativeMethods.SetNativeDefaultMenuItem(menu, TrayCommandOpen);

                int command = NativeMethods.ShowNativePopupMenu(menu, Handle);
                switch (command)
                {
                    case TrayCommandOpen:
                        RestoreFromTray();
                        break;
                    case TrayCommandReload:
                        RestoreFromTray();
                        ReloadWebView();
                        break;
                    case TrayCommandRestartBackend:
                        RestoreFromTray();
                        ShowLoadingSurface();
                        if (bootstrap != null) bootstrap.RequestRestart();
                        break;
                    case TrayCommandCheckDshUpdate:
                        if (bootstrap != null)
                        {
                            ShowTransientNotice("正在检查 dsh 更新…");
                            bootstrap.RequestUpdateCheck();
                        }
                        break;
                    case TrayCommandProject:
                        OpenPath(appPaths.ApplicationDirectory);
                        break;
                    case TrayCommandData:
                        Directory.CreateDirectory(appPaths.DshHome);
                        OpenPath(appPaths.DshHome);
                        break;
                    case TrayCommandLog:
                        OpenLog();
                        break;
                    case TrayCommandCopyAddress:
                        try
                        {
                            if (!String.IsNullOrWhiteSpace(appUrl)) Clipboard.SetText(appUrl);
                            trayIcon.ShowBalloonTip(1800, ProductInfo.Name, "已复制 " + appUrl, ToolTipIcon.Info);
                        }
                        catch
                        {
                        }
                        break;
                    case TrayCommandExit:
                        ExitFromTray();
                        break;
                }
            }
            finally
            {
                NativeMethods.DestroyNativePopupMenu(menu);
            }
        }

        private void HideToTray()
        {
            if (WindowState != FormWindowState.Minimized) restoreWindowState = WindowState;
            ShowInTaskbar = false;
            Hide();
            if (!backgroundNoticeShown)
            {
                backgroundNoticeShown = true;
                trayIcon.ShowBalloonTip(3500, ProductInfo.Name + " 仍在后台运行",
                    "点击托盘图标可重新打开，右键可查看更多选项或退出。", ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            if (IsDisposed) return;
            ShowInTaskbar = true;
            Show();
            WindowState = restoreWindowState == FormWindowState.Minimized ? FormWindowState.Normal : restoreWindowState;
            Activate();
            NativeMethods.BringWindowToFront(Handle);
        }

        private void ExitFromTray()
        {
            exitRequested = true;
            Close();
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            closing = true;
            if (trayIcon != null) trayIcon.Visible = false;
            if (bootstrap != null)
            {
                bootstrap.Dispose();
                bootstrap = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (trayIcon != null) trayIcon.Dispose();
                if (bootstrap != null) bootstrap.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Image LoadLogoImage()
        {
            try
            {
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DshDesktopShell.Icon.png");
                if (stream != null)
                {
                    using (stream)
                    using (Image temporary = Image.FromStream(stream))
                    using (Bitmap source = new Bitmap(temporary))
                    using (Bitmap silhouette = ExtractWhaleMask(source))
                    {
                        return CreateOutlineMask(silhouette);
                    }
                }
            }
            catch
            {
            }

            using (Bitmap fallback = CreateFallbackWhaleMask())
            {
                return CreateOutlineMask(fallback);
            }
        }

        private static Bitmap ExtractWhaleMask(Bitmap source)
        {
            Bitmap fullMask = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            int minX = source.Width;
            int minY = source.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Color pixel = source.GetPixel(x, y);
                    if (pixel.A < 8) continue;

                    // The packaged mark is a white whale on a blue tile.  The
                    // minimum RGB channel cleanly isolates white while keeping
                    // the original anti-aliased silhouette and its cut-outs.
                    int lightestChannel = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                    int maskAlpha = (lightestChannel - 45) * 255 / 205;
                    maskAlpha = Math.Max(0, Math.Min(255, maskAlpha));
                    maskAlpha = maskAlpha * pixel.A / 255;
                    if (maskAlpha < 7) continue;

                    fullMask.SetPixel(x, y, Color.FromArgb(maskAlpha, 255, 255, 255));
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                fullMask.Dispose();
                return CreateFallbackWhaleMask();
            }

            const int padding = 2;
            minX = Math.Max(0, minX - padding);
            minY = Math.Max(0, minY - padding);
            maxX = Math.Min(source.Width - 1, maxX + padding);
            maxY = Math.Min(source.Height - 1, maxY + padding);
            Rectangle crop = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            Bitmap whale = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(whale))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(fullMask, new Rectangle(0, 0, whale.Width, whale.Height),
                    crop, GraphicsUnit.Pixel);
            }
            fullMask.Dispose();
            return whale;
        }

        private static Bitmap CreateOutlineMask(Bitmap silhouette)
        {
            Bitmap outline = new Bitmap(silhouette.Width, silhouette.Height, PixelFormat.Format32bppArgb);
            const int radius = 5;
            int radiusSquared = radius * radius;
            for (int y = 0; y < silhouette.Height; y++)
            {
                for (int x = 0; x < silhouette.Width; x++)
                {
                    int alpha = silhouette.GetPixel(x, y).A;
                    if (alpha < 4) continue;

                    int erodedAlpha = alpha;
                    for (int offsetY = -radius; offsetY <= radius && erodedAlpha > 0; offsetY++)
                    {
                        for (int offsetX = -radius; offsetX <= radius; offsetX++)
                        {
                            if (offsetX * offsetX + offsetY * offsetY > radiusSquared) continue;
                            int sampleX = x + offsetX;
                            int sampleY = y + offsetY;
                            int sampleAlpha = 0;
                            if (sampleX >= 0 && sampleX < silhouette.Width && sampleY >= 0 && sampleY < silhouette.Height)
                            {
                                sampleAlpha = silhouette.GetPixel(sampleX, sampleY).A;
                            }
                            erodedAlpha = Math.Min(erodedAlpha, sampleAlpha);
                            if (erodedAlpha == 0) break;
                        }
                    }

                    int outlineAlpha = Math.Min(255, (alpha - erodedAlpha) * 5 / 4);
                    if (outlineAlpha >= 4)
                    {
                        outline.SetPixel(x, y, Color.FromArgb(outlineAlpha, 255, 255, 255));
                    }
                }
            }
            return outline;
        }

        private static Bitmap CreateFallbackWhaleMask()
        {
            Bitmap fallback = new Bitmap(180, 122, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(fallback))
            using (SolidBrush metal = new SolidBrush(Color.White))
            using (GraphicsPath body = new GraphicsPath())
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                body.StartFigure();
                body.AddBezier(15, 67, 24, 29, 72, 19, 118, 39);
                body.AddBezier(118, 39, 133, 46, 143, 51, 153, 47);
                body.AddBezier(153, 47, 151, 69, 137, 88, 110, 94);
                body.AddBezier(110, 94, 76, 102, 34, 99, 15, 67);
                body.CloseFigure();
                graphics.FillPath(metal, body);
                graphics.FillPolygon(metal, new PointF[]
                {
                    new PointF(127, 41), new PointF(151, 14), new PointF(153, 48),
                    new PointF(178, 25), new PointF(170, 65), new PointF(143, 68)
                });
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using (SolidBrush clear = new SolidBrush(Color.Transparent))
                {
                    graphics.FillEllipse(clear, 111, 54, 8, 8);
                }
            }
            return fallback;
        }

        private static string ReadLogTail(string path, int lineCount)
        {
            try
            {
                if (!File.Exists(path)) return "没有生成错误日志。";
                string[] lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - lineCount);
                return String.Join(Environment.NewLine, lines, start, lines.Length - start);
            }
            catch
            {
                return "无法读取错误日志。";
            }
        }
    }

    internal sealed class AnimatedLoadingView : Control
    {
        private readonly Image logo;
        private readonly Image darkMetalLogo;
        private readonly Image lightMetalLogo;
        private readonly System.Windows.Forms.Timer animationTimer;
        private double phase;
        private string detailText;
        private bool lightTheme;

        internal AnimatedLoadingView(Image image)
        {
            logo = image;
            darkMetalLogo = CreateMetalOutline(image, false);
            lightMetalLogo = CreateMetalOutline(image, true);
            detailText = String.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            animationTimer = new System.Windows.Forms.Timer();
            animationTimer.Interval = 16;
            animationTimer.Tick += delegate
            {
                phase += 0.0075;
                if (phase >= 1.0) phase -= 1.0;
                Invalidate();
            };
            animationTimer.Start();
        }

        internal string DetailText
        {
            get { return detailText; }
            set
            {
                detailText = value ?? String.Empty;
                Invalidate();
            }
        }

        internal void ApplyTheme(bool useLightTheme)
        {
            lightTheme = useLightTheme;
            BackColor = lightTheme ? Color.White : Color.FromArgb(21, 21, 23);
            Invalidate();
        }

        internal void StartAnimation()
        {
            if (!animationTimer.Enabled) animationTimer.Start();
        }

        internal void StopAnimation()
        {
            animationTimer.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(BackColor);

            float logoWidth = Math.Max(44.0f, Math.Min(58.0f, ClientSize.Width * 0.04f));
            float logoHeight = logoWidth * logo.Height / logo.Width;
            RectangleF logoBounds = new RectangleF(
                (ClientSize.Width - logoWidth) / 2.0f,
                (ClientSize.Height - logoHeight) / 2.0f,
                logoWidth,
                logoHeight);

            // A tiny recessed edge gives the outline a laser-cut metal depth,
            // but it is deliberately not a glow or a breathing halo.
            RectangleF recessedBounds = logoBounds;
            recessedBounds.Offset(0.8f, 1.0f);
            DrawTintedLogo(graphics, logo, recessedBounds,
                lightTheme ? Color.FromArgb(196, 198, 201) : Color.FromArgb(35, 36, 38),
                lightTheme ? 120 : 185);
            graphics.DrawImage(lightTheme ? lightMetalLogo : darkMetalLogo, logoBounds);

            float sweepCenter = logoBounds.Left - 25.0f + (float)phase * (logoBounds.Width + 50.0f);
            for (int strip = -5; strip <= 5; strip++)
            {
                float normalized = 1.0f - Math.Abs(strip) / 6.0f;
                int alpha = (int)((lightTheme ? 115.0f : 150.0f) * normalized * normalized);
                float center = sweepCenter + strip * 2.0f;
                const float halfWidth = 1.55f;
                using (GraphicsPath shinePath = new GraphicsPath())
                {
                    shinePath.AddPolygon(new PointF[]
                    {
                        new PointF(center - halfWidth - 10.0f, logoBounds.Top - 8.0f),
                        new PointF(center + halfWidth - 10.0f, logoBounds.Top - 8.0f),
                        new PointF(center + halfWidth + 10.0f, logoBounds.Bottom + 8.0f),
                        new PointF(center - halfWidth + 10.0f, logoBounds.Bottom + 8.0f)
                    });
                    GraphicsState state = graphics.Save();
                    graphics.SetClip(shinePath, CombineMode.Intersect);
                    DrawTintedLogo(graphics, logo, logoBounds,
                        lightTheme ? Color.FromArgb(180, 184, 189) : Color.FromArgb(238, 241, 244), alpha);
                    graphics.Restore(state);
                }
            }
        }

        private static Image CreateMetalOutline(Image maskImage, bool forLightTheme)
        {
            using (Bitmap mask = new Bitmap(maskImage))
            {
                Bitmap metal = new Bitmap(mask.Width, mask.Height, PixelFormat.Format32bppArgb);
                for (int y = 0; y < mask.Height; y++)
                {
                    for (int x = 0; x < mask.Width; x++)
                    {
                        int alpha = mask.GetPixel(x, y).A;
                        if (alpha == 0) continue;
                        float position = mask.Width <= 1 ? 0.0f : x / (float)(mask.Width - 1);
                        float profile = position < 0.46f
                            ? position / 0.46f
                            : (1.0f - position) / 0.54f;
                        profile = Math.Max(0.0f, Math.Min(1.0f, profile));
                        double brushed = Math.Sin((x + y * 0.35) * 0.31) * 3.2
                            + Math.Sin(y * 0.77) * 1.7;
                        int value = forLightTheme
                            ? (int)(66 + profile * 47 + brushed)
                            : (int)(112 + profile * 46 + brushed);
                        value = forLightTheme
                            ? Math.Max(52, Math.Min(124, value))
                            : Math.Max(82, Math.Min(174, value));
                        metal.SetPixel(x, y, Color.FromArgb(alpha, value, value + 1, value + 3));
                    }
                }
                return metal;
            }
        }

        private static void DrawTintedLogo(Graphics graphics, Image mask, RectangleF bounds, Color color, int alpha)
        {
            float red = color.R / 255.0f;
            float green = color.G / 255.0f;
            float blue = color.B / 255.0f;
            float opacity = Math.Max(0, Math.Min(255, alpha)) / 255.0f;
            ColorMatrix matrix = new ColorMatrix(new float[][]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, opacity, 0 },
                new float[] { red, green, blue, 0, 1 }
            });
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                Rectangle destination = Rectangle.Round(bounds);
                graphics.DrawImage(mask, destination, 0, 0, mask.Width, mask.Height,
                    GraphicsUnit.Pixel, attributes);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Dispose();
                darkMetalLogo.Dispose();
                lightMetalLogo.Dispose();
                logo.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool lightTheme;
        private readonly Color background;
        private readonly Color selection;
        private readonly Color pressed;
        private readonly Color border;
        private readonly Color separator;

        internal DarkMenuRenderer(bool useLightTheme)
            : base(new DarkMenuColorTable(useLightTheme))
        {
            lightTheme = useLightTheme;
            background = lightTheme ? Color.White : Color.FromArgb(35, 35, 36);
            selection = lightTheme ? Color.FromArgb(241, 243, 245) : Color.FromArgb(53, 54, 56);
            pressed = lightTheme ? Color.FromArgb(233, 236, 242) : Color.FromArgb(67, 69, 74);
            border = lightTheme ? Color.FromArgb(220, 224, 229) : Color.FromArgb(67, 69, 74);
            separator = lightTheme ? Color.FromArgb(225, 229, 238) : Color.FromArgb(60, 60, 61);
            RoundedEdges = true;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Color fill = e.ToolStrip is MenuStrip ? e.ToolStrip.BackColor : background;
            using (SolidBrush brush = new SolidBrush(fill))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            Rectangle bounds = new Rectangle(3, 2, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 4));
            using (GraphicsPath path = RoundedRectangle(bounds, 6))
            using (SolidBrush brush = new SolidBrush(e.Item.Pressed ? pressed : selection))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripButton button = e.Item as ToolStripButton;
            if (button == null || (!button.Selected && !button.Pressed && !button.Checked)) return;
            Rectangle bounds = new Rectangle(2, 2, Math.Max(1, button.Width - 4), Math.Max(1, button.Height - 4));
            using (GraphicsPath path = RoundedRectangle(bounds, 6))
            using (SolidBrush brush = new SolidBrush(button.Pressed || button.Checked ? pressed : selection))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen pen = new Pen(separator))
            {
                if (e.Vertical)
                {
                    int x = e.Item.Width / 2;
                    e.Graphics.DrawLine(pen, x, 7, x, Math.Max(7, e.Item.Height - 7));
                }
                else
                {
                    int y = e.Item.Height / 2;
                    e.Graphics.DrawLine(pen, 12, y, Math.Max(12, e.Item.Width - 12), y);
                }
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is MenuStrip)
            {
                using (Pen pen = new Pen(separator))
                {
                    e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
                }
                return;
            }
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, e.ToolStrip.Width - 1), Math.Max(1, e.ToolStrip.Height - 1));
            using (Pen pen = new Pen(border))
            {
                e.Graphics.DrawRectangle(pen, bounds);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? e.Item.ForeColor
                : (lightTheme ? Color.FromArgb(162, 164, 166) : Color.FromArgb(97, 102, 107));
            base.OnRenderItemText(e);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        private readonly Color background;
        private readonly Color selection;
        private readonly Color border;
        private readonly Color separator;

        internal DarkMenuColorTable(bool lightTheme)
        {
            background = lightTheme ? Color.White : Color.FromArgb(35, 35, 36);
            selection = lightTheme ? Color.FromArgb(241, 243, 245) : Color.FromArgb(53, 54, 56);
            border = lightTheme ? Color.FromArgb(220, 224, 229) : Color.FromArgb(67, 69, 74);
            separator = lightTheme ? Color.FromArgb(225, 229, 238) : Color.FromArgb(60, 60, 61);
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground { get { return background; } }
        public override Color ImageMarginGradientBegin { get { return background; } }
        public override Color ImageMarginGradientMiddle { get { return background; } }
        public override Color ImageMarginGradientEnd { get { return background; } }
        public override Color MenuBorder { get { return border; } }
        public override Color MenuItemBorder { get { return selection; } }
        public override Color MenuItemSelected { get { return selection; } }
        public override Color MenuItemSelectedGradientBegin { get { return selection; } }
        public override Color MenuItemSelectedGradientEnd { get { return selection; } }
        public override Color MenuItemPressedGradientBegin { get { return selection; } }
        public override Color MenuItemPressedGradientMiddle { get { return selection; } }
        public override Color MenuItemPressedGradientEnd { get { return selection; } }
        public override Color SeparatorDark { get { return separator; } }
        public override Color SeparatorLight { get { return separator; } }
    }

    internal static class NativeMethods
    {
        private const int SwRestore = 9;
        private const int PreferredAppModeForceDark = 2;
        private const int PreferredAppModeForceLight = 3;
        private static readonly object ThemeApiLock = new object();
        private static bool themeApiInitialized;
        private static SetPreferredAppModeDelegate setPreferredAppMode;
        private static AllowDarkModeForWindowDelegate allowDarkModeForWindow;
        private static FlushMenuThemesDelegate flushMenuThemes;
        internal static readonly int ShowWindowMessage = unchecked((int)RegisterWindowMessage("DshDesktopShell.Show.1"));

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPreferredAppModeDelegate(int appMode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool AllowDarkModeForWindowDelegate(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool allow);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FlushMenuThemesDelegate();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr itemId, string text);

        [DllImport("user32.dll")]
        private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, bool byPosition);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        internal static void SignalExistingWindow(string title)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                IntPtr window = FindWindow(null, title);
                if (window != IntPtr.Zero)
                {
                    PostMessage(window, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Thread.Sleep(100);
            }
        }

        internal static void BringWindowToFront(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            ShowWindow(window, SwRestore);
            SetForegroundWindow(window);
        }

        internal static void BeginWindowDrag(IntPtr window)
        {
            ReleaseCapture();
            SendMessage(window, 0x00A1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION.
        }

        internal static IntPtr CreateNativePopupMenu()
        {
            return CreatePopupMenu();
        }

        internal static void AppendNativeMenuItem(IntPtr menu, int command, string text, bool enabled)
        {
            uint flags = enabled ? 0x0000u : 0x0001u; // MF_STRING / MF_GRAYED.
            AppendMenu(menu, flags, (UIntPtr)(uint)command, text);
        }

        internal static void AppendNativeMenuSeparator(IntPtr menu)
        {
            AppendMenu(menu, 0x0800u, UIntPtr.Zero, null); // MF_SEPARATOR.
        }

        internal static void SetNativeDefaultMenuItem(IntPtr menu, int command)
        {
            SetMenuDefaultItem(menu, (uint)command, false);
        }

        internal static int ShowNativePopupMenu(IntPtr menu, IntPtr owner)
        {
            Point cursor;
            if (!GetCursorPos(out cursor)) return 0;
            SetForegroundWindow(owner);
            const uint flags = 0x0100u | 0x0002u | 0x0020u | 0x0008u;
            int command = TrackPopupMenuEx(menu, flags, cursor.X, cursor.Y, owner, IntPtr.Zero);
            PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
            return command;
        }

        internal static int ShowNativePopupMenuAt(IntPtr menu, IntPtr owner, Point screenPoint)
        {
            SetForegroundWindow(owner);
            const uint flags = 0x0100u | 0x0002u; // TPM_RETURNCMD | TPM_RIGHTBUTTON.
            int command = TrackPopupMenuEx(menu, flags, screenPoint.X, screenPoint.Y, owner, IntPtr.Zero);
            PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
            return command;
        }

        internal static void DestroyNativePopupMenu(IntPtr menu)
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
        }

        internal static void SetDarkTitleBar(IntPtr window, bool dark)
        {
            int enabled = dark ? 1 : 0;
            int result = DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int));
            if (result != 0) DwmSetWindowAttribute(window, 19, ref enabled, sizeof(int));
        }

        internal static void RemoveStandardFrameInset(IntPtr window)
        {
            const uint flags = 0x0001u | 0x0002u | 0x0004u | 0x0010u | 0x0020u;
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, flags); // SWP_FRAMECHANGED.
        }

        internal static void ApplyNativeMenuTheme(IntPtr window, bool dark)
        {
            if (window == IntPtr.Zero) return;
            try
            {
                EnsureThemeApis();
                if (setPreferredAppMode != null)
                {
                    setPreferredAppMode(dark ? PreferredAppModeForceDark : PreferredAppModeForceLight);
                }
                if (allowDarkModeForWindow != null) allowDarkModeForWindow(window, dark);
                SetWindowTheme(window, dark ? "DarkMode_Explorer" : "Explorer", null);
                if (flushMenuThemes != null) flushMenuThemes();
            }
            catch
            {
                // Dark menu APIs are optional and undocumented on older Windows builds.
            }
        }

        private static void EnsureThemeApis()
        {
            if (themeApiInitialized) return;
            lock (ThemeApiLock)
            {
                if (themeApiInitialized) return;
                themeApiInitialized = true;
                if (Environment.OSVersion.Version.Build < 18362) return;

                IntPtr module = LoadLibrary("uxtheme.dll");
                if (module == IntPtr.Zero) return;
                setPreferredAppMode = GetDelegate<SetPreferredAppModeDelegate>(module, 135);
                allowDarkModeForWindow = GetDelegate<AllowDarkModeForWindowDelegate>(module, 133);
                flushMenuThemes = GetDelegate<FlushMenuThemesDelegate>(module, 136);
            }
        }

        private static T GetDelegate<T>(IntPtr module, int ordinal) where T : class
        {
            IntPtr address = GetProcAddress(module, (IntPtr)ordinal);
            if (address == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        internal static void EnableRoundedCorners(IntPtr window)
        {
            int preference = 2; // DWMWCP_ROUND.
            DwmSetWindowAttribute(window, 33, ref preference, sizeof(int));
        }

        internal static void SetAppUserModelId(string appId)
        {
            SetCurrentProcessExplicitAppUserModelID(appId);
        }

    }
}
