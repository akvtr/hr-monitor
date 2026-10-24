using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeartRateMonitor
{
    public interface IHeartRateDiscovery
    {
        event Action<DeviceChoice> Connected;
        Task<List<DeviceChoice>> ScanAsync(CancellationToken token);
        Task ConnectOnceAsync(DeviceChoice choice, CancellationToken token);
    }

    // Orchestrates discovery independently of WinRT, so cancellation and retry behavior are testable.
    public sealed class AutoConnector
    {
        private readonly IHeartRateDiscovery transport;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private CancellationTokenSource lifetime;
        private Task running = Task.CompletedTask;
        public event Action<string> Status;
        public event Action<List<DeviceChoice>> Discovered;
        public event Action<ConnectionUpdate> StateChanged;
        public bool Active => !running.IsCompleted;
        public AutoConnector(IHeartRateDiscovery transport, Func<TimeSpan, CancellationToken, Task> delay = null)
        { this.transport = transport; this.delay = delay ?? ((span, token) => Task.Delay(span, token)); }

        public static List<DeviceChoice> Candidates(IEnumerable<DeviceChoice> found, ulong preferred)
        {
            return found.Where(d => (d.Address != 0 || !string.IsNullOrEmpty(d.DeviceId) || !string.IsNullOrEmpty(d.ServiceId)) && d.HasHeartRateService)
                .GroupBy(d => d.Address != 0 ? d.Address.ToString() : d.DeviceId ?? d.ServiceId).Select(g => g.First())
                .OrderByDescending(d => preferred != 0 && d.Address == preferred)
                .ThenBy(d => d.Address).ToList();
        }
        public void Start(Func<Settings> settings)
        {
            if (Active) return;
            lifetime?.Dispose(); lifetime = new CancellationTokenSource();
            running = RunAsync(settings, lifetime.Token);
        }
        public async Task StopAsync() { lifetime?.Cancel(); await running; }
        private async Task RunAsync(Func<Settings> settings, CancellationToken token)
        {
            DeviceChoice connected = null;
            Action<DeviceChoice> remember = choice => connected = choice;
            transport.Connected += remember;
            try {
                while (!token.IsCancellationRequested) {
                    try {
                        Status?.Invoke(connected == null ? "心拍センサーを自動検索しています…" : connected.Name + " を再検出しています…");
                        StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Searching });
                        var found = await transport.ScanAsync(token);
                        token.ThrowIfCancellationRequested();
                        var config = settings();
                        var choices = Candidates(found, connected?.Address ?? config.LastAddress);
                        // Once connected, stay on that device until the user stops or chooses another.
                        if (connected != null) choices = new List<DeviceChoice> { connected };
                        else if (config.LastDeviceVerified && (config.LastAddress != 0 || !string.IsNullOrEmpty(config.LastDeviceId))) {
                            var previous = found.FirstOrDefault(d =>
                                (config.LastAddress != 0 && d.Address == config.LastAddress) ||
                                (!string.IsNullOrEmpty(config.LastDeviceId) && d.DeviceId == config.LastDeviceId))
                                ?? new DeviceChoice { Address = config.LastAddress, Name = config.LastName, DeviceId = config.LastDeviceId, AddressType = config.LastAddressType < 0 ? (Windows.Devices.Bluetooth.BluetoothAddressType?)null : (Windows.Devices.Bluetooth.BluetoothAddressType)config.LastAddressType, HasHeartRateService = true };
                            choices.RemoveAll(d => ReferenceEquals(d, previous) ||
                                (previous.Address != 0 && d.Address == previous.Address) ||
                                (!string.IsNullOrEmpty(previous.DeviceId) && d.DeviceId == previous.DeviceId));
                            choices.Insert(0, previous);
                        }
                        Discovered?.Invoke(choices);
                        StateChanged?.Invoke(new ConnectionUpdate { Phase = choices.Count == 0 ? ConnectionPhase.Searching : ConnectionPhase.Detected, Problem = choices.Count == 0 ? ConnectionProblem.NotFound : ConnectionProblem.None });
                        if (choices.Count == 0) Status?.Invoke("センサーの電源を入れてください · 5秒後に再検索します");
                        foreach (var choice in choices) {
                            token.ThrowIfCancellationRequested();
                            Status?.Invoke(choice.Name + " を検出 · 心拍サービスに接続しています…");
                            try { await transport.ConnectOnceAsync(choice, token); }
                            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                            catch (UnauthorizedAccessException) { throw; }
                            catch (Exception ex) { Status?.Invoke(choice.Name + ": " + ex.Message); }
                            token.ThrowIfCancellationRequested();
                            if (connected != null) {
                                if (!settings().AutoReconnect) { Status?.Invoke("接続が終了しました · 自動再接続はオフです"); return; }
                                Status?.Invoke("接続が切れました · 5秒後に再検出します");
                                StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Retrying });
                                break;
                            }
                        }
                    } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (UnauthorizedAccessException ex) { Status?.Invoke("自動接続を停止 · " + ex.Message); StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Error, Problem = ConnectionUpdate.Classify(ex), Detail = ex.Message }); return; }
                    catch (Exception ex) { Status?.Invoke("自動接続: " + ex.Message + " · 再試行します"); StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Retrying, Problem = ConnectionUpdate.Classify(ex), Detail = ex.Message }); }
                    await delay(TimeSpan.FromSeconds(5), token);
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { transport.Connected -= remember; }
        }
    }
}
