using Autofac.Builder;
using System;
using System.Collections.Generic;
using System.Text;

namespace MadWizard.Desomnia.Pipe
{
    public static class IRegistrationBuilderExtensions
    {
        public static IRegistrationBuilder<TLimit, TActivatorData, TRegistrationStyle>
            AttributedPropertiesAutowired<TLimit, TActivatorData, TRegistrationStyle>(
                this IRegistrationBuilder<TLimit, TActivatorData, TRegistrationStyle> registrationBuilder,
                bool preserveSetValues = true,
                bool allowCircularDependencies = false)

            => registrationBuilder.PropertiesAutowired(new CustomInjectAttributePropertySelector(preserveSetValues), allowCircularDependencies);
    }
}
