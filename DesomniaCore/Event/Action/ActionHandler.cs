using ConcurrentCollections;
using MadWizard.Desomnia.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace MadWizard.Desomnia
{
    internal class ActionHandler(MethodInfo method)
    {
        public string   Name => method.GetCustomAttribute<ActionHandlerAttribute>()!.Name;
        public bool     MayRunInParallel => method.GetCustomAttribute<ActionHandlerAttribute>()!.Concurrent;

        internal ConcurrentHashSet<ActionInvocation> Invocations = [];

        public bool ShouldSkipInvocation(Event @event)
        {
            if (Invocations.Count > 0 && !MayRunInParallel)
                return true;

            return false;
        }

        public ActionInvocation? PrepareWithContext(Actor actor, Arguments? arguments, params object[] context)
        {
            var parameters = new object?[method.GetParameters().Length];

            var argsIndex = 0;
            for (int i = 0; i < parameters.Length; i++) 
            {
                var paramter = method.GetParameters()[i];

                var value = context.Where(obj => paramter.ParameterType.IsAssignableFrom(obj.GetType())).FirstOrDefault();

                if (value == null && arguments != null && arguments.Length > argsIndex)
                {
                    value = arguments[argsIndex++];
                }

                if (value == null)
                {
                    if (paramter.HasDefaultValue)
                    {
                        value = paramter.DefaultValue;
                    }
                    else if (!paramter.IsOptional)
                    {
                        return null; // parameter cannot be satisfied, skip invocation
                    }
                }

                parameters[i] = value;
            } 

            return new ActionInvocation(actor, method, parameters);
        }
    }
}
