using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace HeartRateMonitor
{
    public static class GattAccess
    {
        internal static void EnsureCommunication(GattCommunicationStatus status, string detail)
        {
            if (status == GattCommunicationStatus.Success) return;
            if (status == GattCommunicationStatus.AccessDenied)
                throw new UnauthorizedAccessException(detail + Words.T("。Windowsのデバイス利用許可と、他の心拍アプリの接続を確認してください。", ". Check device access permissions in Windows and connections from other heart-rate apps."));
            throw new InvalidOperationException(detail);
        }
        public static async Task EnsureAsync(Func<Task<DeviceAccessStatus>> request, Func<Task<GattOpenStatus>> open, Action<string> log, CancellationToken token)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (open == null) throw new ArgumentNullException(nameof(open));
            token.ThrowIfCancellationRequested();
            var access = await request();
            log?.Invoke("心拍サービスのアクセス要求: " + access);
            token.ThrowIfCancellationRequested();
            if (access != DeviceAccessStatus.Allowed)
                throw new UnauthorizedAccessException("心拍サービスへのアクセスが許可されませんでした: " + access + "。Windowsのデバイス利用許可と、他の心拍アプリの接続を確認してください。");
            var status = await open();
            log?.Invoke("心拍サービスの共有オープン: " + status);
            token.ThrowIfCancellationRequested();
            if (status == GattOpenStatus.AccessDenied)
                throw new UnauthorizedAccessException("心拍サービスを開く権限が拒否されました: AccessDenied。");
            if (status == GattOpenStatus.SharingViolation)
                throw new UnauthorizedAccessException("心拍サービスが別のアプリに占有されています: SharingViolation。Bekoなどの接続を終了してから再開してください。");
            if (status != GattOpenStatus.Success && status != GattOpenStatus.AlreadyOpened)
                throw new InvalidOperationException("心拍サービスを共有モードで開けません: " + status);
        }
    }
}
