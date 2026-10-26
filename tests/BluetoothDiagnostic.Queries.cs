using System;
using System.Threading.Tasks;

internal static partial class BluetoothDiagnostic
{
    internal static async Task<bool> RunQueriesAsync(Func<Task> paired, Func<Task> services, Action<string> report)
    {
        bool succeeded = true;
        try { await paired(); }
        catch (Exception ex) { report("Paired query: " + ex.Message); succeeded = false; }
        try { await services(); }
        catch (Exception ex) { report("Service query: " + ex.Message); succeeded = false; }
        return succeeded;
    }
}
