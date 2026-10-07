using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AromaShooterUdpBridge
{
	public sealed class SettingsForm : Form
	{
		private readonly BridgeHost host;
		private readonly TextBox address = new TextBox { Width = 150 };
		private readonly NumericUpDown port = Number(1, 65535, 10000);
		private readonly ComboBox transport = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
		private readonly CheckBox auto = new CheckBox { Text = "起動時自動接続", AutoSize = true };
		private readonly NumericUpDown[] levels = Enumerable.Range(0, 6).Select(_ => Number(0, 100, 100)).ToArray();
		private readonly NumericUpDown internalLevel = Number(1, 100, 100), externalLevel = Number(0, 100, 0);
		private readonly ComboBox target = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
		private readonly CheckedListBox chambers = new CheckedListBox { Width = 240, Height = 65, MultiColumn = true, ColumnWidth = 78, CheckOnClick = true };
		private readonly NumericUpDown duration = Number(1, int.MaxValue, 3000);
		private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(900, 0) };
		private readonly Label deviceDot = new Label { Text = "●", AutoSize = true, Margin = Padding.Empty };
		private readonly Label deviceStatus = new Label { AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
		private readonly Label errorDot = new Label { Text = "●", AutoSize = true, Margin = Padding.Empty };
		private readonly Label errorStatus = new Label { AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
		private readonly TextBox logs = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false };
		private readonly Timer refresh = new Timer { Interval = 500 };
		private readonly Button save, reload, defaults, reconnect;
		private readonly ToolTip hints = new ToolTip();
		private string editVersion;
		private string[] displayedDevices = new string[0];
		private string lastLog;
		[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
		public bool AllowClose { get; set; }

		public SettingsForm(BridgeHost host)
		{
			this.host = host;
			Text = "AromaShooter UDP Bridge — 設定・状態";
			Font = new Font("Yu Gothic UI", 10F);
			BackColor = Color.White; ForeColor = Color.FromArgb(38, 43, 50);
			ClientSize = new Size(900, 720); MinimumSize = new Size(780, 620);
			StartPosition = FormStartPosition.CenterScreen;
			AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
			var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(28, 20, 28, 20) };
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
			reconnect = Button("⟳", host.Reconnect);
			reconnect.Name = "Reconnect"; reconnect.AccessibleName = "再接続";
			reconnect.AutoSize = false; reconnect.MinimumSize = Size.Empty; reconnect.Size = new Size(40, 40);
			reconnect.Padding = Padding.Empty; reconnect.Margin = Padding.Empty;
			reconnect.Font = new Font("Segoe UI Symbol", 19F); reconnect.FlatAppearance.BorderSize = 0;
			hints.SetToolTip(reconnect, "再接続 — 機器を停止・切断して再検出");
			var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 16) };
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			header.Controls.Add(new Label { Text = "AromaShooter", AutoSize = true, Font = new Font(Font.FontFamily, 20F, FontStyle.Bold), Margin = Padding.Empty });
			header.Controls.Add(reconnect); root.Controls.Add(header, 0, 0);
			var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
			var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
			body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			viewport.Controls.Add(body); root.Controls.Add(viewport, 0, 1);
			transport.Items.AddRange(new object[] { "USB", "BLE" });
			address.Width = 190; port.Width = 100; transport.Width = 110;
			body.Controls.Add(Row(Field("待受IP（このPC）", address), Field("UDPポート", port), Field("接続方式", transport)));
			// body.Controls.Add(new Label { Text = "0.0.0.0：すべてのネットワーク  /  127.0.0.1：このPCのみ", AutoSize = true, ForeColor = Color.FromArgb(100, 108, 119), Margin = new Padding(0, 2, 0, 0) });
			auto.Margin = new Padding(0, 12, 0, 4); body.Controls.Add(auto);
			var advanced = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(12, 8, 0, 10) };
			var strength = Row();
			for (int i = 0; i < 6; i++) strength.Controls.Add(Field("Ch " + (i + 1), levels[i]));
			advanced.Controls.Add(Label("チャンバー既定強度")); advanced.Controls.Add(strength);
			advanced.Controls.Add(Row(Field("内部ブースター", internalLevel), Field("外部ブースター", externalLevel)));
			for (int i = 0; i < 3; i++) advanced.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			body.Controls.Add(Disclosure("詳細設定", advanced));
			save = Button("適用して保存", () => Save(false));
			save.Name = "SaveSettings"; save.AutoSize = false; save.Size = new Size(154, 48);
			save.BackColor = Color.FromArgb(37, 99, 180); save.ForeColor = Color.White;
			save.Font = new Font(Font, FontStyle.Bold); save.FlatAppearance.BorderSize = 0;
			save.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 150);
			save.FlatAppearance.MouseDownBackColor = Color.FromArgb(24, 64, 125);
			reload = Button("設定を再読み込み", Reload);
			defaults = Button("リセット", async () =>
			{
				if (MessageBox.Show(this, "既存の設定ファイルを既定値で上書きします。続行しますか？", "リセット", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
				await host.Apply(new Settings(), true, true, editVersion); LoadSettings();
			});
			reload.Margin = defaults.Margin = new Padding(0, 7, 10, 7); save.Margin = Padding.Empty;
			var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 18, 0, 0), Margin = Padding.Empty };
			footer.Controls.Add(save); footer.Controls.Add(defaults); footer.Controls.Add(reload);
			var folderLink = new LinkLabel
			{
				Text = "設定フォルダーを開く",
				AutoSize = true,
				Anchor = AnchorStyles.Left,
				Margin = new Padding(0, 18, 16, 0),
				LinkColor = Color.FromArgb(82, 92, 106),
				ActiveLinkColor = Color.FromArgb(37, 99, 180),
				LinkBehavior = LinkBehavior.HoverUnderline
			};
			folderLink.LinkClicked += async (s, e) => await Run(() =>
			{
				string folder = Path.GetDirectoryName(host.Store.Path);
				Directory.CreateDirectory(folder);
				Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
				return Task.CompletedTask;
			});
			var bottom = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			bottom.Controls.Add(folderLink, 0, 0); bottom.Controls.Add(footer, 1, 0);
			root.Controls.Add(bottom, 0, 2);
			target.Items.Add("ALL"); target.SelectedIndex = 0;
			for (int i = 1; i <= 6; i++) chambers.Items.Add("Ch " + i, i == 1);
			chambers.BorderStyle = BorderStyle.None; chambers.BackColor = Color.White;
			var test = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(12, 8, 0, 10) };
			test.Controls.Add(Row(Field("対象機器", target), Field("チャンバー", chambers), Field("噴射長（ms）", duration)));
			test.Controls.Add(Row(Button("既定強度で噴射", () =>
			{
				string list = string.Join(",", chambers.CheckedIndices.Cast<int>().Select(i => i + 1));
				host.SubmitText("SHOOT " + target.Text + " " + list + " " + duration.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
				return Task.CompletedTask;
			}), Button("全停止", () => { host.SubmitText("STOP ALL"); return Task.CompletedTask; })));
			body.Controls.Add(Disclosure("テスト", test));
			for (int i = 0; i < 2; i++) test.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			var statusRow = Row();
			statusRow.Margin = new Padding(0, 20, 0, 12);
			statusRow.ForeColor = Color.FromArgb(82, 92, 106);
			statusRow.Font = new Font("Yu Gothic UI", 9F);
			status.Margin = new Padding(0, 0, 16, 0);
			var deviceGroup = Row(deviceDot, deviceStatus);
			var errorGroup = Row(errorDot, errorStatus);
			deviceGroup.Dock = errorGroup.Dock = DockStyle.None;
			deviceGroup.WrapContents = errorGroup.WrapContents = false;
			deviceGroup.Margin = new Padding(0, 0, 16, 0); errorGroup.Margin = Padding.Empty;
			statusRow.Controls.Add(status); statusRow.Controls.Add(deviceGroup); statusRow.Controls.Add(errorGroup);
			body.Controls.Add(statusRow);
			body.SizeChanged += (s, e) =>
			{
				int width = Math.Max(1, body.ClientSize.Width - 50);
				status.MaximumSize = deviceStatus.MaximumSize = errorStatus.MaximumSize = new Size(width, 0);
			};
			hints.SetToolTip(deviceStatus, "機器一覧は最後の検出結果です。");
			logs.BorderStyle = BorderStyle.None; logs.BackColor = Color.FromArgb(247, 248, 250);
			logs.ForeColor = Color.FromArgb(75, 85, 99); logs.Font = new Font("Yu Gothic UI", 9F);
			var logFrame = new Panel { Dock = DockStyle.Top, Height = 190, Padding = new Padding(12), BackColor = logs.BackColor, Margin = Padding.Empty };
			logFrame.Controls.Add(logs); body.Controls.Add(logFrame);
			for (int i = 0; i < body.Controls.Count; i++) body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			refresh.Tick += (s, e) => RefreshStatus(); refresh.Start();
			FormClosing += (s, e) => { if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
			LoadSettings();
		}
		private static Label Label(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
		private static Control Field(string title, Control input)
		{
			var field = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 20, 6) };
			input.Margin = Padding.Empty; field.Controls.Add(Label(title)); field.Controls.Add(input); return field;
		}
		private static Control Disclosure(string title, Control content)
		{
			var section = new Panel { Dock = DockStyle.Top, Height = 42, Margin = new Padding(0, 4, 0, 0) };
			var toggle = new Button { Name = title + "Toggle", Text = "＋  " + title, AccessibleName = title, AccessibleDescription = "折りたたみ。クリックまたはSpaceで開く", Dock = DockStyle.Top, Height = 42, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(247, 248, 250), Margin = Padding.Empty };
			toggle.FlatAppearance.BorderSize = 0; content.Name = title + "Content"; content.Visible = false;
			bool expanded = false;
			Action resize = () => { int height = toggle.Height + (expanded ? content.Height : 0); if (section.Height != height) section.Height = height; };
			content.SizeChanged += (s, e) => resize();
			toggle.SizeChanged += (s, e) => resize();
			toggle.Click += (s, e) =>
			{
				expanded = !expanded; content.Visible = expanded;
				toggle.Text = (expanded ? "−  " : "＋  ") + title;
				toggle.AccessibleDescription = expanded ? "展開中。クリックまたはSpaceで閉じる" : "折りたたみ。クリックまたはSpaceで開く";
				resize();
			};
			section.Controls.Add(content); section.Controls.Add(toggle); return section;
		}
		private static NumericUpDown Number(int min, int max, int value) => new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = max > 65535 ? 110 : 70 };
		private static FlowLayoutPanel Row(params Control[] controls)
		{
			var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
			row.Controls.AddRange(controls); return row;
		}
		private Button Button(string text, Func<Task> action)
		{
			var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(80, 34), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 10, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(55, 65, 81) };
			b.FlatAppearance.BorderColor = Color.FromArgb(220, 224, 230);
			b.Click += async (s, e) => await Run(action);
			return b;
		}
		public async Task Run(Func<Task> action)
		{
			SetBusy(true);
			try { await action(); }
			catch (Exception e) { host.Log.Write(e.Message, true); Show(); Activate(); MessageBox.Show(this, e.Message, "処理エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
			finally { SetBusy(false); RefreshStatus(); }
		}
		private void SetBusy(bool value) { save.Enabled = reload.Enabled = defaults.Enabled = reconnect.Enabled = !value; }
		public void LoadSettings()
		{
			Settings settings = host.Current ?? new Settings();
			address.Text = settings.Address; port.Value = settings.Port; transport.SelectedItem = settings.Transport; auto.Checked = settings.AutoConnect;
			for (int i = 0; i < 6; i++) levels[i].Value = settings.Intensities[i];
			internalLevel.Value = settings.Internal; externalLevel.Value = settings.External;
			editVersion = host.Version;
			RefreshStatus();
		}
		public async Task Reload() { await host.Reload(); LoadSettings(); }
		private async Task Save(bool overwrite)
		{
			if (!overwrite && host.Store.Fingerprint() != editVersion)
			{
				var result = MessageBox.Show(this, "設定ファイルが外部で変更されています。\n［はい］再読み込み\n［いいえ］画面の値で明示的に上書き\n［キャンセル］保存中止", "設定の競合", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
				if (result == DialogResult.Yes) { await Reload(); return; }
				if (result != DialogResult.No) return;
				overwrite = true;
			}
			var value = new Settings
			{
				Address = address.Text.Trim(),
				Port = (int)port.Value,
				Transport = (string)transport.SelectedItem,
				AutoConnect = auto.Checked,
				Intensities = levels.Select(n => (int)n.Value).ToArray(),
				Internal = (int)internalLevel.Value,
				External = (int)externalLevel.Value
			};
			await host.Apply(value, true, overwrite, editVersion); LoadSettings();
		}
		private void RefreshStatus()
		{
			if (IsDisposed) return;
			string[] devices = host.Engine.Known;
			status.Text = "UDP " + host.UdpState;
			deviceDot.ForeColor = devices.Length == 0 ? Color.FromArgb(156, 163, 175) : Color.FromArgb(34, 150, 83);
			deviceStatus.Text = (host.Current?.Transport ?? "設定エラー") + " · 検出機器" +
				(devices.Length == 0 ? "なし" : ": " + string.Join(", ", devices));
			string lastError = host.Log.LastError;
			bool hasError = !string.IsNullOrEmpty(lastError) && lastError != "なし";
			errorDot.ForeColor = Color.FromArgb(210, 50, 50);
			errorStatus.Text = hasError ? "エラーあり: " + lastError : string.Empty;
			errorStatus.Parent.Visible = hasError;
			if (!devices.SequenceEqual(displayedDevices))
			{
				string selected = target.Text; target.Items.Clear(); target.Items.Add("ALL"); target.Items.AddRange(devices);
				target.SelectedIndex = Math.Max(0, target.Items.IndexOf(selected)); displayedDevices = devices;
			}
			string[] lines = host.Log.Snapshot();
			if (lines.LastOrDefault() != lastLog) { logs.Lines = lines; logs.SelectionStart = logs.TextLength; logs.ScrollToCaret(); lastLog = lines.LastOrDefault(); }
		}
		protected override void Dispose(bool disposing) { if (disposing) { refresh.Dispose(); hints.Dispose(); } base.Dispose(disposing); }
	}
}
