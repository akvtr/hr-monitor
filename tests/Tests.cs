using System;
using System.IO;
using System.Text;
using System.Threading;

internal static partial class Tests
{
    private const long DayMilliseconds = 86400000L;
    private static int count;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }

    private static void Throws(Action action, string name) => Throws<Exception>(action, name);

    private static void Throws<TException>(Action action, string name) where TException : Exception
    {
        try { action(); }
        catch (TException) { Check(true, name); return; }
        throw new Exception("Expected " + typeof(TException).Name + ": " + name);
    }

    private static int Bits(byte[] packet) { int n = packet.Length; return (packet[n - 4] << 24) | (packet[n - 3] << 16) | (packet[n - 2] << 8) | packet[n - 1]; }

    private static string Address(byte[] packet) => Encoding.ASCII.GetString(packet, 0, Array.IndexOf(packet, (byte)0));

    private static char Type(byte[] packet) { int offset = (Array.IndexOf(packet, (byte)0) + 4) / 4 * 4; return (char)packet[offset + 1]; }

    private static void WaitUntil(Func<bool> condition)
    {
        if (!SpinWait.SpinUntil(condition, 3000)) throw new TimeoutException("Timed out waiting for the expected test state.");
    }

    public static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hr-monitor-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try {
            TestAutoConnection();
            TestAutomaticConnectionReentrancy();
            TestAutomaticConnectionCancellationFailures();
            TestBluetoothConnectionReentrancy();
            TestBluetoothRetryClassification();
            TestDeviceSelectionIdentity(folder);
            TestStartupOptions(folder);
            TestInstanceNotification();
            TestDiscoveryFailureFallback();
            TestGattAccess();
            TestConnectionInputValidation();
            TestBluetoothDiagnosticFailures();
            TestGattCancellation();
            TestReceptionTimer();
            TestPresentationAndOsc(folder);
            TestRecordingPipeline(folder);
            TestRecordingObserverFailures(folder);
            TestRetentionObserverFailure(folder);
            TestRecordingInputValidation(folder);
            TestRecordingReadingSnapshot(folder);
            TestRecordingShutdown(folder);
            TestWindowShutdownAfterConnectionFailure(folder);
            TestWindowShutdownWithStoppedWriter(folder);
            TestWindowShutdownWaitsForExport(folder);
            TestWindowShutdownWaitsForSleepDeletion(folder);
            TestRecordingErrorDiagnostics(folder);
            TestStartupErrorDiagnostics(folder);
            TestOscErrorDiagnostics(folder);
            TestOscReceiveTimeout(folder);
            TestHistoryRepository(folder);
            TestStoreBounds(folder);
            TestSleepRangeMerging(folder);
            TestHistoryRefreshPreservesNavigation(folder);
            TestHistoryReadSnapshot(folder);
            TestSnapshotFailurePreservation(folder);
            TestCsvExportFailure(folder);
            TestCsvCultureInvariance(folder);
            TestStoreLifetime(folder);
            TestStoreInputBoundaries(folder);
            TestSettingsPersistence(folder);
            TestSettingsApplyAfterSave(folder);
            TestSettingsRecoveryAfterConnection(folder);
            TestAtomicFileWrites(folder);
            TestAtomicFileCleanupFailure(folder);
            TestConnectionLog(folder);
            TestCustomOscTransmission(folder);
            TestHistorySelection();
            TestChartAggregationBoundaries(folder);
            TestChartInteraction();
            TestChartDragReturnsToOrigin();
            TestChartTimeTicks();
            TestDailyActivity(folder);
            TestMidnightTransitions(folder);
            TestActivityCalendarUi();
            TestCalendarEmptyState();
            TestHistoryDateNavigation();
            TestRetentionPolicy(folder);
            TestMeasurements();
            TestMeasurementFlagCombinations();
            TestSettingsValidation(folder);
            TestSettingsDeviceMetadata(folder);
            TestSettingsUnicodeDecoding(folder);
            TestOscPackets();
            TestOscPacketBoundaries();
            TestOscSenderLifecycle();
            TestStorePersistence(folder);
            TestStoreTextRoundTrip(folder);
            TestStoreTextValidation(folder);
            TestOyasumiSleep(folder);
            TestSleepManagement(folder);
            TestSleepDeletion(folder);
            TestOyasumiDiscoveryCancellation(folder);
            TestRetentionBoundary(folder);
            TestStartupRetention(folder);
            TestIdleRetention(folder);
            TestQueueDrain(folder);
            Console.WriteLine($"All {count} checks passed. Test artifacts: {folder}"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Artifacts: " + folder); return 1; }
    }
}
