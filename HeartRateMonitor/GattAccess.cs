using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace HeartRateMonitor
{
    public static class GattAccess
    {
        public static async Task EnsureAsync(Func<Task<DeviceAccessStatus>> request, Func<Task<GattOpenStatus>> open, Action<string> log, CancellationToken token)
        {
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
