using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HeartRateMonitor
{
    public sealed class ConnectionLog
    {
        private readonly Queue<string> lines = new Queue<string>();
        private readonly object gate = new object();
        public string Path { get; }
        public string WriteError { get; private set; }
        public ConnectionLog(string folder) { Path = System.IO.Path.Combine(folder, "connection.log"); }
        public void Add(string message)
        {
            lock (gate) {
                lines.Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + (message ?? "").Replace('\r', ' ').Replace('\n', ' '));
                while (lines.Count > 250) lines.Dequeue();
                try { File.WriteAllLines(Path, lines.ToArray(), new UTF8Encoding(true)); WriteError = null; }
                catch (Exception ex) { WriteError = ex.Message; }
            }
        }
        public string Snapshot() { lock (gate) return string.Join(Environment.NewLine, lines) + (WriteError == null ? "" : Environment.NewLine + "ログファイル保存失敗: " + WriteError); }
    }
}
