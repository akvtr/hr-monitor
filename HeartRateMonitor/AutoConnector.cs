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
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            this.transport = transport;
            this.delay = delay ?? ((span, token) => Task.Delay(span, token));
        }

        public static List<DeviceChoice> Candidates(IEnumerable<DeviceChoice> found, ulong preferred)
        {
            return found.Where(d => (d.Address != 0 || !string.IsNullOrEmpty(d.DeviceId) || !string.IsNullOrEmpty(d.ServiceId)) && d.HasHeartRateService)
                .GroupBy(CandidateIdentity).Select(g => g.First())
                .OrderByDescending(d => preferred != 0 && d.Address == preferred)
                .ThenBy(d => d.Address).ToList();
        }
        private static string CandidateIdentity(DeviceChoice choice)
        {
            if (choice.Address != 0) return "address:" + choice.Address;
            if (!string.IsNullOrEmpty(choice.DeviceId)) return "device:" + choice.DeviceId;
            return "service:" + choice.ServiceId;
        }
        private static List<DeviceChoice> SelectCandidates(List<DeviceChoice> found, Settings config, DeviceChoice connected)
        {
            // Once connected, stay on that device until the user stops or chooses another.
            if (connected != null) return new List<DeviceChoice> { connected };
            var choices = Candidates(found, config.LastAddress);
            if (!config.LastDeviceVerified || (config.LastAddress == 0 && string.IsNullOrEmpty(config.LastDeviceId))) return choices;

            var saved = new DeviceChoice {
                Address = config.LastAddress, Name = config.LastName, DeviceId = config.LastDeviceId,
                AddressType = config.LastAddressType < 0 ? (Windows.Devices.Bluetooth.BluetoothAddressType?)null : (Windows.Devices.Bluetooth.BluetoothAddressType)config.LastAddressType,
                HasHeartRateService = true
            };
            var previous = found.FirstOrDefault(d => d.Matches(saved)) ?? saved;
            choices.RemoveAll(d => d.Matches(previous));
            choices.Insert(0, previous);
            return choices;
        }
        private async Task<List<DeviceChoice>> DiscoverCandidatesAsync(Func<Settings> settings, DeviceChoice connected, CancellationToken token)
        {
            List<DeviceChoice> found;
            try { found = await transport.ScanAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex) {
                token.ThrowIfCancellationRequested();
                var fallback = SelectCandidates(new List<DeviceChoice>(), settings(), connected);
                if (fallback.Count == 0) throw;
                Status?.Invoke(Words.T("検索失敗: ", "Discovery failed: ") + ex.Message +
                    Words.T(" · 検証済み機器に直接接続します", " · Connecting directly to the verified sensor"));
                return fallback;
            }
            token.ThrowIfCancellationRequested();
            return SelectCandidates(found, settings(), connected);
        }
        public void Start(Func<Settings> settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (Active) return;
            lifetime?.Dispose(); lifetime = new CancellationTokenSource();
            // RunAsync can synchronously notify observers before its first await.
            // Publish the current operation first so reentrant Start/Stop see it.
            var startup = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            running = startup.Task.Unwrap();
            startup.SetResult(RunAsync(settings, lifetime.Token));
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
                        var choices = await DiscoverCandidatesAsync(settings, connected, token);
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
