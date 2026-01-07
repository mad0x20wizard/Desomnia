using Autofac.Core;
using Autofac.Core.Activators.Reflection;
using Autofac.Core.Resolving.Pipeline;

namespace Autofac
{
    public static class AutofacExtension
    {
        public static T? FirstParameterOfType<T>(this ResolveRequestContext context)
        {
            return (T?)context.Parameters.OfType<TypedParameter>().FirstOrDefault(p => p.Type.IsAssignableTo<T>())?.Value;
        }

        public static void ChangeParameterByType<T>(this ResolveRequestContext context, T value)
        {
            if (context.Parameters.OfType<TypedParameter>().FirstOrDefault(p => p.Type == typeof(T)) is TypedParameter param)
            {
                context.ChangeParameters([.. context.Parameters.Except([param]), new TypedParameter(typeof(T), value)]);
            }
        }

        public static bool IsLimitedTo<T>(this IComponentRegistration registration)
        {
            return registration.Activator is ReflectionActivator activator && activator.LimitType.IsAssignableTo<T>();
        }
    }
}
