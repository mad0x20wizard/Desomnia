namespace MadWizard.Desomnia.Processes.Manager
{
    public class ProcessNotFoundException(int pid) : SystemException("Process with pid = " + pid + " not found")
    {
        public int PID => pid;
    }
}
