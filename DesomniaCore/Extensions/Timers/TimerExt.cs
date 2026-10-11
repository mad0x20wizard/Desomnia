namespace System.Timers
{
    static class TimerExt
    {
        public static void Restart(this Timer timer)
        {
            timer.Stop();
            timer.Start();
        }
    }
}
