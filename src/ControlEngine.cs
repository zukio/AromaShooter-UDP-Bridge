using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AromaShooterUdpBridge
{
	public sealed class ControlEngine
	{
		private sealed class Work
		{
			public Command Command;
			public string Source;
			public HashSet<string> Excluded = new HashSet<string>(StringComparer.Ordinal);
			public Queue<string> Remaining;
			public bool Cancelled;
		}
		private readonly object gate = new object();
		private readonly LinkedList<Work> pending = new LinkedList<Work>();
		private readonly Dictionary<string, string> stops = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Queue<Func<Task>> maintenance = new Queue<Func<Task>>();
		private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
		private readonly IDevice device;
		private readonly Action<string, bool> log;
		private readonly Task worker;
		private Settings settings = new Settings();
		private string[] known = new string[0];
		private Work active;
		private bool accepting, quitting, shutdownRequested;
		public string[] Known { get { lock (gate) return (string[])known.Clone(); } }
		public int PendingCount { get { lock (gate) return pending.Count; } }

		public ControlEngine(IDevice device, Action<string, bool> log)
		{
			this.device = device; this.log = log;
			worker = Task.Run(Run);
		}
		public void UpdateSettings(Settings value) { lock (gate) settings = value.Clone(); }

		public bool Submit(Command command, string source)
		{
			lock (gate)
			{
				if (shutdownRequested || !accepting || known.Length == 0 || (!command.All && !known.Contains(command.Target, StringComparer.Ordinal)))
				{ log(source + " " + command.Text + " : 未接続・未知対象・切替中のため破棄", true); return false; }
				if (command.IsStop)
				{
					foreach (Work w in pending) Cancel(w, command);
					if (active != null) Cancel(active, command);
					var node = pending.First;
					while (node != null) { var next = node.Next; if (node.Value.Cancelled) pending.Remove(node); node = next; }
					foreach (string serial in command.All ? known : new[] { command.Target }) stops[serial] = source + " " + command.Text;
				}
				else
				{
					int remainingActive = active != null && !active.Cancelled && active.Remaining != null && active.Remaining.Count > 0 ? 1 : 0;
					if (pending.Count + remainingActive >= 100) { log(source + " " + command.Text + " : キュー上限100件のため破棄", true); return false; }
					pending.AddLast(new Work { Command = command, Source = source });
				}
				signal.Release(); return true;
			}
		}
		private static void Cancel(Work w, Command stop)
		{
			if (stop.All || (!w.Command.All && w.Command.Target == stop.Target)) w.Cancelled = true;
			else w.Excluded.Add(stop.Target);
		}
		private Task Maintain(Func<Task> action)
		{
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			lock (gate)
			{
				accepting = false;
				pending.Clear(); stops.Clear(); if (active != null) active.Cancelled = true;
				maintenance.Enqueue(async () =>
				{
					try { await action().ConfigureAwait(false); completion.TrySetResult(true); }
					catch (Exception e) { log(e.Message, true); completion.TrySetException(e); }
				});
				signal.Release();
			}
			return completion.Task;
		}
		public Task Reconnect(string transport) => Maintain(async () =>
		{
			StopAndDisconnect();
			try { await device.Connect(transport).ConfigureAwait(false); }
			finally { var detected = ReadDevices(); lock (gate) { known = detected; accepting = !shutdownRequested; } }
			log(transport + " 検出完了: " + string.Join(", ", Known), false);
		});
		public Task Disconnect() => Maintain(() => { StopAndDisconnect(); return Task.CompletedTask; });
		private string[] ReadDevices()
		{
			try { return device.Devices().Distinct(StringComparer.Ordinal).ToArray(); }
			catch (Exception e) { log("接続一覧: " + e.Message, true); return new string[0]; }
		}
		private void StopAndDisconnect()
		{
			foreach (string serial in ReadDevices().Union(Known, StringComparer.Ordinal)) SafeStop(serial, "終了/再接続");
			try { device.Disconnect(); }
			catch (Exception e) { log("切断失敗: " + e.Message, true); }
			finally { lock (gate) known = new string[0]; }
		}
		private bool SafeStop(string serial, string source)
		{
			try { device.Stop(serial); log(source + " 対象=" + serial + " 停止 SDK呼び出し完了", false); return true; }
			catch (Exception e) { log(source + " 対象=" + serial + " 停止失敗: " + e.Message, true); return false; }
		}
		private async Task Run()
		{
			while (true)
			{
				await signal.WaitAsync().ConfigureAwait(false);
				while (true)
				{
					Func<Task> action = null; Work work = null; string stop = null, source = null;
					lock (gate)
					{
						if (maintenance.Count > 0) action = maintenance.Dequeue();
						else if (stops.Count > 0) { var pair = stops.First(); stop = pair.Key; source = pair.Value; stops.Remove(stop); }
						else if (pending.Count > 0) { work = pending.First.Value; pending.RemoveFirst(); active = work; }
						else { if (quitting) return; break; }
					}
					if (action != null) await action().ConfigureAwait(false);
					else if (stop != null) SafeStop(stop, source);
					else if (work != null)
					{
						ExecuteOne(work);
						lock (gate)
						{
							if (!work.Cancelled && work.Remaining != null && work.Remaining.Count > 0) pending.AddFirst(work);
							active = null;
						}
					}
				}
			}
		}
		private void ExecuteOne(Work work)
		{
			try
			{
				string serial; Settings current;
				var detected = work.Remaining == null ? ReadDevices() : null;
				lock (gate)
				{
					if (work.Cancelled) return;
					if (work.Remaining == null)
					{
						known = detected;
						work.Remaining = new Queue<string>(work.Command.All ? known : known.Where(x => x == work.Command.Target));
					}
					if (work.Remaining.Count == 0) { log(work.Source + " " + work.Command.Text + " : 接続対象なし、破棄", true); return; }
					serial = work.Remaining.Dequeue();
					if (work.Excluded.Contains(serial)) return;
					current = settings.Clone();
				}
				string context = work.Source + " " + work.Command.Text;
				if (!SafeStop(serial, context)) return;
				var until = DateTime.UtcNow.AddMilliseconds(device.StopToShootDelayMs);
				while (true)
				{
					lock (gate) { if (work.Cancelled || work.Excluded.Contains(serial)) return; }
					var left = until - DateTime.UtcNow;
					if (left <= TimeSpan.Zero) break;
					Thread.Sleep(left < TimeSpan.FromMilliseconds(10) ? left : TimeSpan.FromMilliseconds(10));
				}
				int[] levels = work.Command.Intensities ?? work.Command.Chambers.Select(n => current.Intensities[n - 1]).ToArray();
				device.Shoot(serial, work.Command.Duration, work.Command.Chambers, levels, current.Internal, current.External);
				log(context + " 対象=" + serial + " 噴射 SDK呼び出し完了", false);
			}
			catch (Exception e) { log(work.Source + " " + work.Command.Text + " SDK失敗: " + e.Message, true); }
		}
		public async Task Shutdown()
		{
			RequestShutdown();
			await Disconnect().ConfigureAwait(false);
			lock (gate) { quitting = true; signal.Release(); }
			await worker.ConfigureAwait(false);
			signal.Dispose();
		}
		public void RequestShutdown()
		{
			lock (gate)
			{
				shutdownRequested = true; accepting = false;
				pending.Clear(); stops.Clear(); if (active != null) active.Cancelled = true;
			}
		}
	}
}
