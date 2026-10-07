using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AromaShooterUdpBridge
{
    public sealed class BridgeLog : IDisposable
    {
        private readonly object gate = new object();
        private readonly Queue<string> recent = new Queue<string>();
        private readonly BlockingCollection<Tuple<DateTime, string>> writes = new BlockingCollection<Tuple<DateTime, string>>(2000);
        private readonly Task writer;
        private bool disposed;
        public string LastError { get; private set; } = "なし";
        public BridgeLog(string directory)
        {
            writer = Task.Run(() => {
                DateTime lastDay = DateTime.MinValue;
                foreach (var entry in writes.GetConsumingEnumerable())
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        if (lastDay != entry.Item1.Date)
                        {
                            foreach (string path in Directory.GetFiles(directory, "*.log"))
                            {
                                DateTime day;
                                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", null,
                                    System.Globalization.DateTimeStyles.None, out day) && day < entry.Item1.Date.AddDays(-6)) File.Delete(path);
                            }
                            lastDay = entry.Item1.Date;
                        }
                        File.AppendAllText(Path.Combine(directory, entry.Item1.ToString("yyyy-MM-dd") + ".log"), entry.Item2 + Environment.NewLine, new UTF8Encoding(false));
                    }
                    catch (Exception e) { Memory("ログ保存失敗: " + e.Message, true); }
                }
            });
        }
        private void Memory(string text, bool error)
        {
            lock (gate) { recent.Enqueue(text); while (recent.Count > 500) recent.Dequeue(); if (error) LastError = text; }
        }
        public void Write(string message, bool error = false)
        {
            DateTime now = DateTime.Now;
            string line = now.ToString("yyyy-MM-dd HH:mm:ss.fff") + (error ? " ERROR " : " INFO ") + message.Replace("\r", "\\r").Replace("\n", "\\n");
            Memory(line, error);
            if (!writes.TryAdd(Tuple.Create(now, line))) Memory("ログ書込キュー上限のためファイルログを破棄", true);
        }
        public string[] Snapshot() { lock (gate) return recent.ToArray(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            writes.CompleteAdding(); writer.GetAwaiter().GetResult(); writes.Dispose();
        }
    }
}
