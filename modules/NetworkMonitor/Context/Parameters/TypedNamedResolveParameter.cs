using Autofac;
using Autofac.Core;

namespace MadWizard.Desomnia.Network.Context.Parameters
{
    internal class TypedNamedResolvedParameter<T>(string name) : ResolvedParameter(
        (pi, ctx) => pi.ParameterType == typeof(T),
        (pi, ctx) => ctx.ResolveNamed<T>(name)) where T : notnull
    {
        internal static TypedNamedResolvedParameter<T> FindBy(string name) => new(name);
    }
}
