using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AromaShooterUdpBridge
{
	internal static class Program
	{
		[STAThread]
		private static void Main()
		{
			string key = "Local\\AromaShooterUdpBridge-" + WindowsIdentity.GetCurrent().User.Value;
			using (var instance = new SingleInstance(key))
			{
				if (!instance.IsPrimary) return;
				Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
				using (var context = new TrayContext(instance.ShowRequested)) Application.Run(context);
			}
		}
	}

	// Adapted from trayIconAppTemplate's Component + NotifyIcon + ContextMenuStrip pattern.
	// ApplicationContext owns the lifetime so closing SettingsForm keeps the tray alive.
	internal sealed class TrayContext : ApplicationContext
	{
		private readonly System.ComponentModel.IContainer components = new System.ComponentModel.Container();
		private readonly BridgeLog log;
		private readonly BridgeHost host;
		private readonly SettingsForm form;
		private readonly NotifyIcon tray;
		private readonly System.Windows.Forms.Timer showTimer;
		private readonly System.Windows.Forms.Timer statusTimer;
		private readonly ToolStripMenuItem statusItem;
		private readonly Icon icon;
		private Color? statusColor;
		private bool exiting, disposed;

		internal SettingsForm SettingsWindow => form;
		internal NotifyIcon TrayIcon => tray;
		public TrayContext(EventWaitHandle show) : this(CreateHost(), show) { }
		private static BridgeHost CreateHost()
		{
			string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AromaShooterUdpBridge");
			var logger = new BridgeLog(Path.Combine(folder, "logs"));
			return new BridgeHost(new SettingsStore(Path.Combine(folder, "settings.json")), new OfficialDevice(), logger);
		}
		internal TrayContext(BridgeHost host, EventWaitHandle show)
		{
			this.host = host; log = host.Log;
			form = new SettingsForm(host);
			// Create the handle without showing a window at normal startup.
			var handle = form.Handle;
			using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AromaShooterUdpBridge.Assets.tray.ico")) icon = new Icon(stream);
			form.Icon = icon;
			var menu = new ContextMenuStrip(components);
			statusItem = (ToolStripMenuItem)menu.Items.Add("設定・状態", null, (s, e) => ShowSettings());
			menu.Opening += (s, e) => UpdateStatusDot(statusItem);
			statusTimer = new System.Windows.Forms.Timer(components) { Interval = 500 };
			statusTimer.Tick += (s, e) => UpdateStatusDot(statusItem);
			menu.Opened += (s, e) => statusTimer.Start();
			menu.Closed += (s, e) => statusTimer.Stop();
			menu.Items.Add("全停止", null, (s, e) => host.SubmitText("STOP ALL"));
			menu.Items.Add("再接続", null, async (s, e) => await form.Run(host.Reconnect));
			menu.Items.Add("終了", null, async (s, e) => await Exit());
			tray = new NotifyIcon(components) { Icon = icon, Text = "AromaShooter UDP Bridge", ContextMenuStrip = menu, Visible = true };
			tray.DoubleClick += (s, e) => ShowSettings();
			showTimer = new System.Windows.Forms.Timer(components) { Interval = 200 };
			showTimer.Tick += (s, e) => { if (show.WaitOne(0)) ShowSettings(); }; showTimer.Start();
			form.FormClosing += async (s, e) =>
			{
				if (!form.AllowClose && e.CloseReason != CloseReason.UserClosing) { e.Cancel = true; await Exit(); }
			};
			form.BeginInvoke(new Action(async () =>
			{
				await host.Start(); form.LoadSettings();
				if (ShouldShowSettingsAtStartup(host.Current, log.LastError)) ShowSettings();
				await host.ConnectAtStartup();
			}));
		}
		internal static bool ShouldShowSettingsAtStartup(Settings settings, string lastError)
		{
			return settings == null || settings.ShowWindowOnStartup || lastError != "なし";
		}
		private void UpdateStatusDot(ToolStripMenuItem item)
		{
			string error = log.LastError;
			Color color = !string.IsNullOrEmpty(error) && error != "なし" ? Color.FromArgb(210, 50, 50)
					: host.Engine.Known.Length > 0 ? Color.FromArgb(34, 150, 83) : Color.FromArgb(156, 163, 175);
			if (statusColor.HasValue && statusColor.Value == color) return;
			var bitmap = new Bitmap(16, 16);
			using (var g = Graphics.FromImage(bitmap))
			using (var brush = new SolidBrush(color))
			{
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				g.FillEllipse(brush, 3, 3, 10, 10);
			}
			var old = item.Image; item.Image = bitmap; old?.Dispose(); statusColor = color;
		}
		private void ShowSettings() { if (!exiting) { form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); } }
		internal async Task Exit()
		{
			if (exiting) return; exiting = true; tray.ContextMenuStrip.Enabled = false; form.Enabled = false;
			try { await host.Shutdown(); }
			catch (Exception e) { log.Write("終了処理: " + e.Message, true); }
			finally { tray.Visible = false; form.AllowClose = true; form.Close(); ExitThread(); }
		}
		protected override void Dispose(bool disposing)
		{
			if (disposing && !disposed) { disposed = true; statusTimer.Stop(); statusItem.Image?.Dispose(); components.Dispose(); form.Dispose(); icon.Dispose(); log.Dispose(); }
			base.Dispose(disposing);
		}
	}
}
