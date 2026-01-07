namespace MadWizard.Desomnia.Process.Manager
{
    public interface IProcess
    {
        int Id { get; }
        int SessionId { get; }

        string Name { get; }

        System.Diagnostics.Process NativeProcess { get; }

        IProcess? Parent { get; }
        bool HasParent(IProcess parent)
        {
            IProcess process = this;

            while (process.Parent != null)
            {
                if (process.Parent == parent)
                    return true;

                process = process.Parent;
            }

            return false;
        }

        Task Stop(TimeSpan? timeout = null);
    }
}
