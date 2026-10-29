namespace MadWizard.Desomnia.Network.Filter
{
    /// <summary>
    /// A retryable evaluation outcome: an applicable predicate requires a more specialized
    /// context. The evaluator defers this outcome until other rules have been considered.
    /// Malformed or unsupported payload is not an additional-data request.
    /// </summary>
    public abstract class AdditionalDataNeededException : Exception
    {
        protected AdditionalDataNeededException() : base()
        {

        }

        protected AdditionalDataNeededException(string message) : base(message)
        {

        }
    }
}
