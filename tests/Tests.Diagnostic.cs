using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static partial class Tests
{
    private static void TestBluetoothDiagnosticFailures()
    {
        for (int failures = 0; failures < 4; failures++) {
            int pairedCalls = 0, serviceCalls = 0;
            var messages = new List<string>();
            bool succeeded = BluetoothDiagnostic.RunQueriesAsync(
                () => { pairedCalls++; return (failures & 1) == 0 ? Task.CompletedTask : Task.FromException(new InvalidOperationException("PAIRED FAILURE")); },
                () => { serviceCalls++; return (failures & 2) == 0 ? Task.CompletedTask : Task.FromException(new InvalidOperationException("SERVICE FAILURE")); },
                messages.Add).GetAwaiter().GetResult();
            Check(succeeded == (failures == 0) && pairedCalls == 1 && serviceCalls == 1,
                "Bluetooth diagnostic attempts both queries and reports any failure: " + failures);
            var expected = new List<string>();
            if ((failures & 1) != 0) expected.Add("Paired query: PAIRED FAILURE");
            if ((failures & 2) != 0) expected.Add("Service query: SERVICE FAILURE");
            Check(messages.SequenceEqual(expected), "Bluetooth diagnostic preserves each failed query and its error: " + failures);
        }
        int followingCalls = 0;
        var canceledMessages = new List<string>();
        bool completed = BluetoothDiagnostic.RunQueriesAsync(
            () => Task.FromCanceled(new CancellationToken(true)),
            () => { followingCalls++; return Task.CompletedTask; }, canceledMessages.Add).GetAwaiter().GetResult();
        Check(!completed && followingCalls == 1 && canceledMessages.Single().StartsWith("Paired query: "),
            "Bluetooth diagnostic treats a canceled query as a failure while still trying the other query");
    }
}
