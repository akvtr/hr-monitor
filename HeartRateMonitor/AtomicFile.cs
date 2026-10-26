using System;
using System.IO;

namespace HeartRateMonitor
{
    internal static class AtomicFile
    {
        internal static void Write(string path, Action<Stream> write, bool keepBackup = false)
        {
            string destination = Path.GetFullPath(path);
            // A private file on the same volume keeps incomplete writes away from the destination.
            string temporary = Path.Combine(Path.GetDirectoryName(destination), ".hr-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownsTemporary = false;
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    ownsTemporary = true;
                    write(stream);
                    stream.Flush(true);
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, keepBackup ? destination + ".bak" : null);
                else File.Move(temporary, destination);
            } catch (Exception writeError) {
                if (ownsTemporary) {
                    try { File.Delete(temporary); }
                    catch (Exception cleanupError) {
                        throw new AggregateException(Words.T("ファイル保存と一時ファイルの削除に失敗しました。", "The file write and temporary-file cleanup both failed."), writeError, cleanupError);
                    }
                }
                throw;
            }
        }
    }
}
