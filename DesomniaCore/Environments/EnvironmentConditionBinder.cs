using Autofac;
using Autofac.Core;
using MadWizard.Desomnia.Configuration.Binding;

namespace MadWizard.Desomnia.Environments
{
    /// <summary>
    /// Constructs the <see cref="IEnvironmentCondition"/>s of parsed environment blocks out
    /// of the persistent container — a pipeline stage that runs BEFORE the blocks reach the
    /// <see cref="EnvironmentMonitor"/>, which therefore never touches the container itself.
    /// A plain condition attribute resolves <c>Named&lt;IEnvironmentCondition&gt;(attribute)</c>
    /// with the value as a constructor parameter; a namespaced attribute resolves the
    /// <c>Named&lt;IEnvironmentConditionProvider&gt;(namespaceUri)</c>, which receives the
    /// local name and the value.
    /// </summary>
    internal static class EnvironmentConditionBinder
    {
        /// <summary>Resolves the conditions of all enabled blocks (disabled ones behave like
        /// commented-out: neither watched nor required to resolve). Any failure is a
        /// configuration error and aborts the generation.</summary>
        public static void ResolveConditions(IComponentContext context, IReadOnlyList<EnvironmentBlock> blocks)
        {
            foreach (var block in blocks)
            {
                if (block.MergeMode == EnvironmentMergeMode.Never)
                    continue;

                block.Conditions = block.ConditionAttributes
                    .Select(attribute => CreateCondition(context, block, attribute))
                    .ToList();
            }
        }

        private static IEnvironmentCondition CreateCondition(IComponentContext context, EnvironmentBlock block, ConditionAttribute attribute)
        {
            // a namespaced attribute resolves the provider registered for the namespace URI,
            // which receives the local name AND the value (an open set of keys per provider)
            if (attribute.Namespace is string ns)
            {
                var provider = context.ResolveOptionalNamed<IEnvironmentConditionProvider>(ns)
                    ?? throw new ConfigurationValueException($"Environment '{block.DisplayName}': no condition provider " +
                        $"is registered for namespace '{ns}' (attribute '{attribute.DisplayName}').");

                try
                {
                    return provider.CreateCondition(attribute.Name, attribute.Value);
                }
                catch (ConfigurationValueException error)
                {
                    throw InvalidCondition(block, attribute, error);
                }
                catch (DependencyResolutionException ex) when (FindConfigurationError(ex) is ConfigurationValueException error)
                {
                    throw InvalidCondition(block, attribute, error); // a provider may resolve components itself
                }
            }

            try
            {
                if (context.ResolveOptionalNamed<IEnvironmentCondition>(attribute.Name.ToLowerInvariant(), TypedParameter.From(attribute.Value))
                    is IEnvironmentCondition condition)
                {
                    return condition;
                }
            }
            // a condition constructor rejecting the attribute value surfaces wrapped in the
            // container's resolution exception - report it as the configuration problem it is
            catch (DependencyResolutionException ex) when (FindConfigurationError(ex) is ConfigurationValueException error)
            {
                throw InvalidCondition(block, attribute, error);
            }

            throw new ConfigurationValueException($"Environment '{block.DisplayName}': " +
                $"no condition is registered for attribute '{attribute.DisplayName}'.");
        }

        private static ConfigurationValueException InvalidCondition(EnvironmentBlock block, ConditionAttribute attribute, ConfigurationValueException error)
            => new($"Environment '{block.DisplayName}': " +
                $"invalid condition {attribute.DisplayName} = \"{attribute.Value}\" ({error.Message})", error);

        private static ConfigurationValueException? FindConfigurationError(Exception exception)
        {
            for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
                if (inner is ConfigurationValueException error)
                    return error;

            return null;
        }
    }
}
