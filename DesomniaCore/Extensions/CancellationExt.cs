namespace System.Threading
{
    public static class CancellationExt
    {
        public static CancellationTokenSource WithTimeout(this CancellationTokenSource source, TimeSpan timeout)
            => WithTimeout(source.Token, timeout);

        public static CancellationTokenSource WithTimeout(this CancellationToken token, TimeSpan timeout)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            if (timeout != default)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(timeout, Timeout.InfiniteTimeSpan);

                cts.CancelAfter(timeout);
            }

            return cts;
        }
    }
}
