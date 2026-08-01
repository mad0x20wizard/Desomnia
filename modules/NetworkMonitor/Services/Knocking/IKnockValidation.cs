using MadWizard.Desomnia.Network.Knocking.Secrets;

namespace MadWizard.Desomnia.Network.Services.Knocking
{
    public interface IKnockValidation
    {
        /// <summary>
        /// Rejects a configured secret this method cannot use, with a message naming the reason.
        /// Called before the method is first used, so that a misconfigured secret surfaces as a
        /// configuration error instead of failing opaquely somewhere inside <see cref="Knock"/>.
        /// Methods that place no constraints on the secret keep the default no-op.
        /// </summary>
        void ValidateSecret(SharedSecret secret);
    }
}
