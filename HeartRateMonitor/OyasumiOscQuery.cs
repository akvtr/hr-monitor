using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HeartRateMonitor
{
    internal sealed class OyasumiOscQuery
    {
        internal const string SleepPath = "/OyasumiVR/SleepMode";
        internal bool Read(int port, CancellationToken token)
        {
            var info = Parse(Get(port, "/?HOST_INFO", token));
            object name;
            if (!info.TryGetValue("NAME", out name) || !string.Equals(name as string, "OyasumiVR", StringComparison.Ordinal))
                throw new InvalidDataException("OSCQuery server is not OyasumiVR.");
            return ParseSleep(Get(port, SleepPath, token));
        }
        internal static bool ParseSleep(string json)
        {
            var node = Parse(json); object path, access, value;
            if (!node.TryGetValue("FULL_PATH", out path) || !string.Equals(path as string, SleepPath, StringComparison.Ordinal)
                || !node.TryGetValue("ACCESS", out access) || !(access is int) || (((int)access & 1) == 0)
                || !node.TryGetValue("VALUE", out value))
                throw new InvalidDataException("OyasumiVR sleep mode is not readable.");
            var values = value as object[];
            if (values == null || values.Length != 1 || !(values[0] is bool))
                throw new InvalidDataException("OyasumiVR sleep mode VALUE must contain one Boolean.");
            return (bool)values[0];
        }
        private static Dictionary<string, object> Parse(string json)
        {
            var node = new JavaScriptSerializer { MaxJsonLength = 65536 }.DeserializeObject(json) as Dictionary<string, object>;
            if (node == null) throw new InvalidDataException("Invalid OSCQuery JSON object.");
            return node;
        }
        private static string Get(int port, string path, CancellationToken token)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            token.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + path);
            request.Proxy = null; request.AllowAutoRedirect = false; request.Timeout = 1000; request.ReadWriteTimeout = 1000;
            request.KeepAlive = false;
            using (token.Register(request.Abort))
            using (var response = (HttpWebResponse)request.GetResponse()) {
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("OSCQuery did not return HTTP 200.");
                using (var stream = response.GetResponseStream())
                using (var bytes = new MemoryStream()) {
                    byte[] buffer = new byte[4096]; int size;
                    while ((size = stream.Read(buffer, 0, buffer.Length)) > 0) {
                        token.ThrowIfCancellationRequested();
                        if (bytes.Length + size > 65536) throw new InvalidDataException("OSCQuery response is too large.");
                        bytes.Write(buffer, 0, size);
                    }
                    return new UTF8Encoding(false, true).GetString(bytes.ToArray());
                }
            }
        }
    }
}
