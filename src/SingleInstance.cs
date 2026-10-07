using System;
using System.Threading;

namespace AromaShooterUdpBridge
{
    internal sealed class SingleInstance : IDisposable
    {
        private readonly Mutex mutex;
        public bool IsPrimary { get; }
        public EventWaitHandle ShowRequested { get; }
        public SingleInstance(string key)
        {
            bool created;
            ShowRequested = new EventWaitHandle(false, EventResetMode.AutoReset, key + "-Show");
            mutex = new Mutex(true, key, out created);
            IsPrimary = created;
            if (!created) ShowRequested.Set();
        }
        public void Dispose()
        {
            ShowRequested.Dispose();
            if (IsPrimary) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
