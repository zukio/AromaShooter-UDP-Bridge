using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AromaShooterUdpBridge;

internal static class Program
{
	private static int passed;
	private static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
	private static void Reject(Action action, string label) { try { action(); } catch { Check(true, label); return; } Check(false, label); }
	private static Command Parse(string value) => Command.Parse(Encoding.UTF8.GetBytes(value));
	private static async Task Until(Func<bool> predicate)
	{
		for (int i = 0; i < 500; i++) { if (predicate()) return; await Task.Delay(10); }
		throw new TimeoutException("Timed out waiting for worker");
	}
	[STAThread]
	private static int Main(string[] args)
	{
		if (args.Length == 2 && args[0] == "--signal") { using (var instance = new SingleInstance(args[1])) return instance.IsPrimary ? 1 : 0; }
		try { if (args.Contains("--ui")) UiTests(); else Run().GetAwaiter().GetResult(); Console.WriteLine("ALL PASSED: " + passed); return 0; }
		catch (Exception e) { Console.Error.WriteLine(e); return 1; }
	}
	private static async Task Run()
	{
		Check(Parse("  shoot all 1,3 2147483647 0,100\r\n").Duration == int.MaxValue, "valid command, case, trim, int maximum, intensity zero");
		Check(Parse("STOP ASN3A").Target == "ASN3A", "STOP exact serial");
		foreach (string invalid in new[] { "", "SHOOT ALL 1", "SHOOT ALL 1 0", "SHOOT ALL 1 -1", "SHOOT ALL 1 +1", "SHOOT ALL 1 1.0",
						"SHOOT ALL 1 2147483648", "SHOOT ALL 0 10", "SHOOT ALL 7 10", "SHOOT ALL 1,1 10", "SHOOT ALL 1, 10", "SHOOT ALL 1,2 10 1",
						"SHOOT ALL 1 10 101", "SHOOT ALL 1 10 -1", "SHOOT ALL 1 10 1 2", "STOP ALL extra", "STOP\tALL", "STOP ALL\nSTOP ALL", "\uFEFFSTOP ALL" })
			Reject(() => Parse(invalid), "reject " + invalid.Replace("\n", "\\n"));
		Reject(() => Command.Parse(new byte[] { 0xff, 0xfe }), "invalid UTF-8");
		Reject(() => Command.Parse(new byte[1025]), "oversized datagram");
		string json = new Settings().ToJson();
		Check(Settings.Parse(json).Internal == 100, "settings round trip");
		foreach (string invalid in new[] { json.Replace("10000", "\"10000\""), json.Replace("10000", "10000.0"), json.Replace("10000", "65536"),
						json.Replace("schemaVersion", "unknown"), json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
						json.Replace("\"internalBoosterIntensity\": 100", "\"internalBoosterIntensity\": 0"), json.Replace("USB", "OSC"),
						json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"), json.Replace("true", "\"true\"") })
			Reject(() => Settings.Parse(invalid), "invalid settings schema/type/range");

		var fake = new Fake(); var errors = new List<string>();
		var engine = new ControlEngine(fake, (m, e) => { lock (errors) errors.Add(m); });
		Check(!engine.Submit(Parse("SHOOT ALL 1 10"), "test"), "disconnected SHOOT discarded");
		await engine.Reconnect("USB");
		Check(!engine.Submit(Parse("STOP a"), "test"), "unknown/case-mismatched target rejected without stopping");
		fake.Clear();
		engine.UpdateSettings(new Settings { Intensities = new[] { 11, 22, 33, 44, 55, 66 }, Internal = 70, External = 8 });
		engine.Submit(Parse("SHOOT A 1,3 3000"), "test");
		await Until(() => fake.Events.Length >= 2);
		Check(fake.Events.SequenceEqual(new[] { "STOP A", "SHOOT A 3000 1,3 11,33 70 8" }), "target-only stop then shoot, default per-chamber intensity, boosters");
		engine.Submit(Parse("SHOOT A 2 10 0"), "test"); await Until(() => fake.Events.Length >= 4);
		engine.Submit(Parse("SHOOT A 2 10"), "test"); await Until(() => fake.Events.Length >= 6);
		Check(fake.Events[3].Contains(" 2 0 ") && fake.Events[5].Contains(" 2 22 "), "explicit intensity does not mutate default");

		fake.BlockNextShoot(); engine.Submit(Parse("SHOOT A 1 10"), "test"); await Until(() => fake.Entered.IsSet);
		engine.Submit(Parse("SHOOT ALL 2 20"), "test");
		engine.Submit(Parse("SHOOT B 3 30"), "test");
		engine.Submit(Parse("STOP A"), "test");
		int before = fake.Events.Length; fake.Release.Set();
		await Until(() => fake.Events.Length >= before + 5);
		var after = fake.Events.Skip(before).ToArray();
		Check(after[0] == "STOP A" && !after.Any(x => x.StartsWith("SHOOT A")) && after.Count(x => x.StartsWith("SHOOT B")) == 2,
				"STOP priority, cancel A portion of pending ALL, retain B shoots");

		fake.BlockNextShoot(); engine.Submit(Parse("SHOOT A 1 10"), "test"); await Until(() => fake.Entered.IsSet);
		bool allAccepted = true;
		for (int i = 0; i < 100; i++) allAccepted &= engine.Submit(Parse("SHOOT B 1 10"), "test");
		Check(allAccepted, "100 pending commands accepted");
		Check(!engine.Submit(Parse("SHOOT B 1 10"), "test"), "queue cap 100");
		Check(engine.Submit(Parse("STOP ALL"), "test") && engine.PendingCount == 0, "STOP accepted when full and cancels all");
		before = fake.Events.Length; fake.Release.Set(); await Until(() => fake.Events.Length >= before + 2);
		Check(!fake.Events.Skip(before).Any(x => x.StartsWith("SHOOT")), "cancelled shoots never replay");
		fake.BlockNextShoot(); engine.Submit(Parse("SHOOT A 1 10"), "test"); await Until(() => fake.Entered.IsSet);
		engine.Submit(Parse("SHOOT B 1 10"), "test");
		var reconnecting = engine.Reconnect("BLE");
		Check(!engine.Submit(Parse("SHOOT ALL 1 10"), "test"), "commands during reconnect discarded");
		before = fake.Events.Length; fake.Release.Set(); await reconnecting;
		Check(!fake.Events.Skip(before).Any(x => x.StartsWith("SHOOT")) && engine.Known.Length == 2, "reconnect cancels queue, stops and disconnects before discovery");
		fake.FailStop = "A"; fake.Clear(); engine.Submit(Parse("SHOOT ALL 1 10"), "test"); await Until(() => fake.Events.Any(x => x.StartsWith("SHOOT B")));
		Check(!fake.Events.Any(x => x.StartsWith("SHOOT A")), "A stop failure prevents A shoot, B continues");
		fake.FailDisconnect = true; await engine.Shutdown(); Check(fake.Events.Contains("DISCONNECT"), "shutdown continues after stop/disconnect failure");

		string folder = Path.Combine(Path.GetTempPath(), "AromaBridgeTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
		var store = new SettingsStore(Path.Combine(folder, "settings.json"));
		var settings = new Settings { Address = "127.0.0.1", Port = FreePort(), AutoConnect = false };
		store.Save(settings, null, false); string version; store.Read(out version);
		Check(version == store.Fingerprint(), "settings saved/reloaded fingerprint");
		File.WriteAllText(store.Path, "broken"); Reject(() => store.Read(out version), "corrupt settings rejected");
		Check(File.ReadAllText(store.Path) == "broken", "corrupt settings preserved");
		Reject(() => store.Save(settings, version, false), "external modification conflict");
		using (var log = new BridgeLog(Path.Combine(folder, "logs")))
		{
			var host = new BridgeHost(store, new Fake(), log);
			Check(await host.Start() && host.Current == null && host.UdpState == "待受停止", "corrupt startup disables UDP/autoconnect");
			await host.Apply(settings, true, true, null); Check(host.UdpState.Contains(settings.Port.ToString()), "explicit recovery starts UDP");
			await host.Reconnect();
			using (var sender = new UdpClient()) { byte[] data = Encoding.UTF8.GetBytes("SHOOT A 1 10"); await sender.SendAsync(data, data.Length, "127.0.0.1", settings.Port); }
			await Until(() => log.Snapshot().Any(x => x.Contains("噴射 SDK呼び出し完了")));
			Check(true, "real localhost UDP receive -> parse -> mock SDK");
			using (var busy = new UdpListener("127.0.0.1", FreePort()))
			{
				var changed = settings.Clone(); changed.Port = busy.Port;
				try { await host.Apply(changed, true, false, host.Version); Check(false, "port conflict"); } catch (SocketException) { Check(true, "port conflict rejected"); }
				Check(host.Current.Port == settings.Port && host.UdpState.Contains(settings.Port.ToString()) && store.Read(out version).Port == settings.Port, "port conflict retains old settings/listener");
			}
			using (var otherAddress = new UdpListener("127.0.0.2", settings.Port))
			{
				var wildcard = settings.Clone(); wildcard.Address = "0.0.0.0";
				try { await host.Apply(wildcard, true, false, host.Version); Check(false, "overlapping bind conflict"); } catch (SocketException) { Check(true, "overlapping bind conflict rejected"); }
				Check(host.Current.Address == "127.0.0.1" && host.UdpState.Contains("127.0.0.1:"), "closed old socket reopens after overlapping bind failure");
			}
			var wildcardSuccess = settings.Clone(); wildcardSuccess.Address = "0.0.0.0";
			await host.Apply(wildcardSuccess, true, false, host.Version);
			Check(host.UdpState.Contains("0.0.0.0:"), "same-port overlapping address change succeeds");
			await host.Apply(settings, true, false, host.Version);
			var next = settings.Clone(); next.Port = FreePort();
			await host.Apply(next, true, false, host.Version); Check(host.Current.Port == next.Port, "port change succeeds");
			next.Intensities[0] = 27; store.Save(next, null, true); await host.Reload(); Check(host.Current.Intensities[0] == 27, "external edit reload");
			string saved = host.Version; File.AppendAllText(store.Path, " ");
			var rollback = next.Clone(); rollback.Port = FreePort();
			try { await host.Apply(rollback, true, false, saved); Check(false, "save rollback"); } catch (IOException) { Check(true, "save conflict after candidate bind"); }
			Check(host.Current.Port == next.Port && host.UdpState.Contains(next.Port.ToString()), "save failure restores active listener");
			await host.Shutdown(); Check(host.UdpState == "待受停止", "shutdown releases listener");
			using (var rebound = new UdpListener(next.Address, next.Port)) Check(true, "port released after shutdown");
		}
		Console.WriteLine("Test artifacts: " + folder);
	}
	private static int FreePort() { using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) return ((IPEndPoint)udp.Client.LocalEndPoint).Port; }
	private static void UiTests()
	{
		string key = "Local\\AromaBridgeTest-" + Guid.NewGuid().ToString("N");
		using (var instance = new SingleInstance(key))
		{
			var start = new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--signal " + key)
			{ UseShellExecute = false, CreateNoWindow = true };
			using (var child = System.Diagnostics.Process.Start(start))
			{
				Check(child.WaitForExit(5000) && child.ExitCode == 0 && instance.ShowRequested.WaitOne(1000), "second process signals first instance and exits");
			}
		}
		string folder = Path.Combine(Path.GetTempPath(), "AromaBridgeUiTests-" + Guid.NewGuid().ToString("N"));
		var store = new SettingsStore(Path.Combine(folder, "settings.json"));
		store.Save(new Settings { Address = "127.0.0.1", Port = FreePort(), AutoConnect = true }, null, false);
		var fake = new Fake();
		var host = new BridgeHost(store, fake, new BridgeLog(Path.Combine(folder, "logs")));
		System.Windows.Forms.Application.EnableVisualStyles();
		System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
		using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
		using (var context = new TrayContext(host, show))
		using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
		{
			int stage = 0; bool busy = false; Exception failure = null; DateTime deadline = DateTime.UtcNow.AddSeconds(10);
			timer.Tick += async (s, e) =>
			{
				if (busy) return; busy = true;
				try
				{
					if (DateTime.UtcNow > deadline) throw new TimeoutException("UI test timed out");
					if (stage == 0 && host.Current != null && host.Engine.Known.Length == 2)
					{
						Check(!context.SettingsWindow.Visible && context.TrayIcon.Visible, "normal startup shows tray only");
						show.Set(); stage = 1;
					}
					else if (stage == 1 && context.SettingsWindow.Visible)
					{
						Check(true, "instance notification opens settings window");
						var window = context.SettingsWindow;
						var advanced = window.Controls.Find("詳細設定Content", true).Single();
						var testPanel = window.Controls.Find("テストContent", true).Single();
						Check(!advanced.Visible && !testPanel.Visible, "advanced and test sections initially collapsed");
						using (var bitmap = new System.Drawing.Bitmap(window.Width, window.Height))
						{
							window.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, window.Size));
							bitmap.Save(Path.Combine(folder, "ui-collapsed.png"));
						}
						((System.Windows.Forms.Button)window.Controls.Find("詳細設定Toggle", true).Single()).PerformClick();
						((System.Windows.Forms.Button)window.Controls.Find("テストToggle", true).Single()).PerformClick();
						Check(advanced.Visible && testPanel.Visible, "disclosures open on click");
						Console.WriteLine("Expanded content heights: " + advanced.Height + ", " + testPanel.Height);
						Console.WriteLine("Sections: " + advanced.Parent.Bounds + ", " + testPanel.Parent.Bounds);
						using (var bitmap = new System.Drawing.Bitmap(window.Width, window.Height))
						{
							window.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, window.Size));
							bitmap.Save(Path.Combine(folder, "ui-expanded.png"));
						}
						Console.WriteLine("UI previews: " + folder);
						context.SettingsWindow.Close();
						Check(!context.SettingsWindow.Visible && !context.SettingsWindow.IsDisposed && context.TrayIcon.Visible, "settings close returns to tray");
						fake.Clear(); stage = 2;
					}
					else if (stage == 2)
					{
						timer.Stop(); context.TrayIcon.ContextMenuStrip.Items[context.TrayIcon.ContextMenuStrip.Items.Count - 1].PerformClick();
					}
				}
				catch (Exception error) { failure = error; timer.Stop(); await context.Exit(); }
				finally { busy = false; }
			};
			timer.Start(); System.Windows.Forms.Application.Run(context);
			if (failure != null) throw failure;
			Check(fake.Events.SequenceEqual(new[] { "STOP A", "STOP B", "DISCONNECT" }) && host.UdpState == "待受停止" && !context.TrayIcon.Visible,
					"tray exit stops devices, disconnects, releases UDP, hides icon");
		}
	}
	private sealed class Fake : IDevice
	{
		private readonly object gate = new object(); private readonly List<string> events = new List<string>();
		public string[] Events { get { lock (gate) return events.ToArray(); } }
		public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
		private bool block, connected;
		public string FailStop; public bool FailDisconnect;
		public void Clear() { lock (gate) events.Clear(); }
		private void Add(string s) { lock (gate) events.Add(s); }
		public void BlockNextShoot() { Entered.Reset(); Release.Reset(); block = true; }
		public Task Connect(string transport) { connected = true; return Task.CompletedTask; }
		public int StopToShootDelayMs { get; set; }
		public string[] Devices() => connected ? new[] { "A", "B" } : new string[0];
		public void Stop(string serial) { Add("STOP " + serial); if (FailStop == serial) throw new Exception("test stop failure"); }
		public void Shoot(string serial, int duration, int[] chambers, int[] intensities, int internalBooster, int externalBooster)
		{
			Add("SHOOT " + serial + " " + duration + " " + string.Join(",", chambers) + " " + string.Join(",", intensities) + " " + internalBooster + " " + externalBooster);
			if (block) { block = false; Entered.Set(); if (!Release.Wait(10000)) throw new TimeoutException(); }
		}
		public void Disconnect() { Add("DISCONNECT"); connected = false; if (FailDisconnect) throw new Exception("test disconnect failure"); }
	}
}
