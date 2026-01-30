using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows;
using Application = System.Windows.Application;

namespace EliteWhisper.Services
{
    public class TrayIconService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
        
        public event EventHandler? SettingsRequested;
        public event EventHandler? ExitRequested;
        public event EventHandler? ShowWidgetRequested;

        public void Initialize()
        {
            // Create context menu
            _contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("Show Widget (F2)");
            showItem.Click += (s, e) => ShowWidgetRequested?.Invoke(this, EventArgs.Empty);
            showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
            _contextMenu.Items.Add(showItem);
            
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            _contextMenu.Items.Add(settingsItem);
            
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);
            _contextMenu.Items.Add(exitItem);

            // Create tray icon
            _notifyIcon = new NotifyIcon
            {
                Icon = CreateDefaultIcon(),
                Text = "Elite Whisper - AI Dictation",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };

            // Double-click to show widget
            _notifyIcon.DoubleClick += (s, e) => ShowWidgetRequested?.Invoke(this, EventArgs.Empty);
        }

        private Icon CreateDefaultIcon()
        {
            // Create a simple colored icon programmatically
            // In production, you'd use an embedded resource
            try
            {
                using var bitmap = new Bitmap(32, 32);
                using var g = Graphics.FromImage(bitmap);
                
                // Background circle
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(Brushes.DodgerBlue, 2, 2, 28, 28);
                
                // Mic icon (simplified)
                g.FillRectangle(Brushes.White, 12, 6, 8, 12);
                g.FillEllipse(Brushes.White, 10, 14, 12, 8);
                g.FillRectangle(Brushes.White, 15, 22, 2, 4);
                g.FillRectangle(Brushes.White, 11, 26, 10, 2);

                var iconHandle = bitmap.GetHicon();
                return Icon.FromHandle(iconHandle);
            }
            catch
            {
                // Fallback to system icon
                return SystemIcons.Application;
            }
        }

        public void SetStatus(string status)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = $"Elite Whisper - {status}";
            }
        }

        public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            _contextMenu?.Dispose();
            _contextMenu = null;
        }
    }
}
