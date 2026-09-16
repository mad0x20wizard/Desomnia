using ConcurrentCollections;
using MadWizard.Desomnia.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace MadWizard.Desomnia.Events
{
    internal class ActionHandler(MethodInfo method)
    {
        private ActionHandlerAttribute Attribute { get; } = method.GetCustomAttribute<ActionHandlerAttribute>()!;

        public string Name => Attribute.Name;
        public bool MayRunInParallel => Attribute.Concurrent;
        public bool IsDetached => Attribute.Detached;

        private readonly Lock _gate = new();
        private int _running;

        /// <summary>Atomic reserve-and-check (the old count-then-add left a window in
        /// which a non-concurrent handler could run twice in parallel): reserves an
        /// execution slot, or reports the invocation as skipped.</summary>
        public bool TryBeginInvocation()
        {
            lock (_gate)
            {
                if (_running > 0 && !MayRunInParallel)
                    return false;

                _running++;
                return true;
            }
        }

        public void EndInvocation()
        {
            lock (_gate)
                _running--;
        }

        public ActionInvocation? PrepareWithContext(EventMetaObject actor, IReadOnlyList<object>? arguments, params object[] context)
        {
            var parameters = new object?[method.GetParameters().Length];

            var candidates = new List<object>(context);       // context objects are CONSUMED when
                                                              // bound (§9.3) — one object, one parameter
            var argsIndex = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramter = method.GetParameters()[i];

                var value = candidates.Where(obj => paramter.ParameterType.IsAssignableFrom(obj.GetType())).FirstOrDefault();

                if (value != null)
                {
                    candidates.Remove(value);
                }
                else if (arguments != null && arguments.Count > argsIndex)
                {
                    value = arguments[argsIndex++];
                }

                if (value == null)
                {
                    if (paramter.HasDefaultValue)
                    {
                        value = paramter.DefaultValue;
                    }
                    else if (!paramter.IsOptional) // TODO: When is a parameter truly optional? (string?) doesn't seem to count
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
