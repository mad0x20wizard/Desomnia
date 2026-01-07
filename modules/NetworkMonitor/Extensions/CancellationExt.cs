namespace System.Threading
{
    internal static class CancellationExt
    {
        internal static CancellationTokenSource WithTimeout(this CancellationTokenSource source, TimeSpan timeout)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
            cts.CancelAfter(timeout);
            return cts;
        }
    }
}
