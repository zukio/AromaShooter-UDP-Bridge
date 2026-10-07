using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AromaShooterUdpBridge
{
    public sealed class BridgeHost
    {
        private readonly SemaphoreSlim changes = new SemaphoreSlim(1);
        private UdpListener listener;
        private volatile bool stopping;
        public readonly SettingsStore Store;
        public readonly ControlEngine Engine;
        public readonly BridgeLog Log;
        public Settings Current { get; private set; }
        public string Version { get; private set; }
        public string UdpState => listener == null ? "待受停止" : "待受中 " + listener.Address + ":" + listener.Port;
        public BridgeHost(SettingsStore store, IDevice device, BridgeLog log)
        { Store = store; Log = log; Engine = new ControlEngine(device, log.Write); }

        public async Task<bool> Start()
        {
            bool first = !File.Exists(Store.Path);
            try
            {
                if (first) Store.Save(new Settings(), null, false);
                string version; Settings next = Store.Read(out version);
                await Apply(next, false, false, version);
                return first;
            }
            catch (Exception e) { Log.Write("起動設定エラー: " + e.Message, true); return true; }
        }
        public async Task Reload()
        {
            string version; var next = Store.Read(out version);
            await Apply(next, false, false, version);
        }
        public async Task Apply(Settings next, bool save, bool overwrite, string version)
        {
            next.Validate(); next = next.Clone();
            await changes.WaitAsync();
            try
            {
                if (stopping) return;
                Settings previous = Current;
                UdpListener old = listener, candidate = old;
                bool same = old != null && old.Address == next.Address && old.Port == next.Port;
                bool closedOld = false;
                string appliedVersion = version;
                try
                {
                    if (!same)
                    {
                        // An overlapping wildcard binding cannot coexist with the old listener.
                        if (old != null && old.Port == next.Port && (old.Address == "0.0.0.0" || next.Address == "0.0.0.0"))
                        { old.Dispose(); listener = null; closedOld = true; }
                        candidate = new UdpListener(next.Address, next.Port);
                    }
                    if (save) appliedVersion = Store.Save(next, version, overwrite);
                }
                catch
                {
                    if (candidate != old) candidate?.Dispose();
                    if (closedOld)
                    {
                        try { listener = new UdpListener(old.Address, old.Port); StartListener(listener); }
                        catch (Exception restore) { listener = null; Log.Write("旧待受の復旧失敗。待受停止: " + restore.Message, true); }
                    }
                    throw;
                }
                bool switchTransport = previous != null && previous.Transport != next.Transport;
                // Suspend command acceptance before publishing the new transport's configuration.
                Task disconnect = switchTransport ? Engine.Disconnect() : Task.CompletedTask;
                Current = next; Version = appliedVersion;
                Engine.UpdateSettings(next);
                if (!same)
                {
                    if (!closedOld) old?.Dispose();
                    listener = candidate; StartListener(candidate);
                }
                await disconnect;
                Log.Write("設定適用: " + UdpState + " " + next.Transport);
                if ((previous == null || switchTransport) && next.AutoConnect)
                {
                    try { await Engine.Reconnect(next.Transport); }
                    catch (Exception e) { Log.Write("接続失敗: " + e.Message, true); }
                }
            }
            finally { changes.Release(); }
        }
        private void StartListener(UdpListener value)
        {
            value.Start((bytes, source) => { if (ReferenceEquals(listener, value) && !stopping) Receive(bytes, source); }, Log.Write);
        }
        public bool Receive(byte[] bytes, string source)
        {
            if (stopping) return false;
            try { var command = Command.Parse(bytes); Log.Write(source + " 受信 " + command.Text); return Engine.Submit(command, source); }
            catch (Exception e) { Log.Write(source + " 不正UDP破棄: " + e.Message, true); return false; }
        }
        public void SubmitText(string text) => Receive(Encoding.UTF8.GetBytes(text), "UI");
        public async Task Reconnect()
        {
            await changes.WaitAsync();
            try { if (!stopping && Current != null) await Engine.Reconnect(Current.Transport); }
            finally { changes.Release(); }
        }
        public async Task Shutdown()
        {
            stopping = true;
            Engine.RequestShutdown();
            await changes.WaitAsync();
            try { listener?.Dispose(); listener = null; await Engine.Shutdown(); }
            finally { changes.Release(); }
        }
    }
}
