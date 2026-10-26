using System;

namespace HeartRateMonitor
{
    public static class Words
    {
        public static string Language = "ja";
        public static string T(string japanese, string english) => Language == "en" ? english : japanese;
    }
    public enum ConnectionPhase { Stopped, Searching, Detected, Connecting, AwaitingData, Receiving, NoSignal, Retrying, Error }
    public enum ConnectionProblem { None, NotFound, Adapter, Permission, Busy, Unreachable, Timeout, NoData, Other }
    public sealed class ConnectionUpdate
    {
        public ConnectionPhase Phase;
        public ConnectionProblem Problem;
        public string Detail;
        public static ConnectionProblem Classify(Exception error)
        {
            var text = error?.Message ?? "";
            if (text.Contains("SharingViolation")) return ConnectionProblem.Busy;
            if (error is UnauthorizedAccessException || text.Contains("AccessDenied") || text.Contains("DeniedBy")) return ConnectionProblem.Permission;
            if (text.Contains("Unreachable")) return ConnectionProblem.Unreachable;
            if (error is OperationCanceledException || error is TimeoutException || text.Contains("タイムアウト")) return ConnectionProblem.Timeout;
            if (text.Contains("アダプター")) return ConnectionProblem.Adapter;
            if (text.Contains("20秒")) return ConnectionProblem.NoData;
            return ConnectionProblem.Other;
        }
        public static string Name(ConnectionPhase phase)
        {
            switch (phase) {
                case ConnectionPhase.Searching: return Words.T("検索中", "Searching");
                case ConnectionPhase.Detected: return Words.T("センサー検出", "Sensor found");
                case ConnectionPhase.Connecting: return Words.T("接続中", "Connecting");
                case ConnectionPhase.AwaitingData: return Words.T("接続済み・受信待ち", "Connected · awaiting data");
                case ConnectionPhase.Receiving: return Words.T("心拍受信中", "Receiving heart rate");
                case ConnectionPhase.NoSignal: return Words.T("心拍信号なし", "No heart-rate signal");
                case ConnectionPhase.Retrying: return Words.T("再接続待ち", "Waiting to reconnect");
                case ConnectionPhase.Error: return Words.T("接続を確認してください", "Connection needs attention");
                default: return Words.T("停止中", "Stopped");
            }
        }
        public static string Advice(ConnectionProblem problem)
        {
            switch (problem) {
                case ConnectionProblem.NotFound: return Words.T("センサーを装着して電源を入れてください。見つかるまで再検索します。", "Wear and turn on your sensor. Discovery will continue automatically.");
                case ConnectionProblem.Adapter: return Words.T("WindowsのBluetoothを有効にし、BLE対応アダプターを確認してください。", "Enable Bluetooth in Windows and check your BLE adapter.");
                case ConnectionProblem.Permission: return Words.T("Windowsでのデバイス利用許可を確認してください。他の心拍アプリも終了し、再接続してください。", "Check device access permissions in Windows. Exit other heart-rate apps, then reconnect.");
                case ConnectionProblem.Busy: return Words.T("別のアプリが機器を使用しています。Bekoなどをタスクトレイから終了して再接続してください。", "Another app is using the sensor. Exit apps such as Beko from the system tray, then reconnect.");
                case ConnectionProblem.Unreachable: return Words.T("機器に通信できません。電源・距離を確認し、スマホなど別の接続を切ってください。", "The sensor is unreachable. Check power and range, and disconnect it from other devices.");
                case ConnectionProblem.Timeout: return Words.T("接続が時間切れになりました。センサーを近づけ、別アプリの接続を終了してください。", "The connection timed out. Move the sensor closer and disconnect other apps.");
                case ConnectionProblem.NoData: return Words.T("心拍通知が途絶えました。装着とセンサーの電源を確認してください。", "Heart-rate notifications stopped. Check skin contact and sensor power.");
                case ConnectionProblem.Other: return Words.T("接続ログで失敗した段階を確認できます。再接続を試してください。", "The connection log contains the failed step. Try reconnecting.");
                default: return Words.T("センサーの電源を入れると自動で接続します。", "Turn on your sensor to connect automatically.");
            }
        }
    }
}
