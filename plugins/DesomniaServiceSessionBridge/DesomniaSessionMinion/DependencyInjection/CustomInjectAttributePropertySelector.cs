using Autofac.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace MadWizard.Desomnia.Pipe
{
    class CustomInjectAttributePropertySelector : DefaultPropertySelector
    {
        public CustomInjectAttributePropertySelector(bool preserveSetValues) : base(preserveSetValues) { }

        public override bool InjectProperty(PropertyInfo propertyInfo, object instance)
        {
            bool hasAttribute = propertyInfo.GetCustomAttribute<Autowired>(true) != null;

            if (!hasAttribute || !propertyInfo.CanWrite)
            {
                return false;
            }

            if (!PreserveSetValues || !propertyInfo.CanRead)
            {
                return true;
            }

            try
            {
                return propertyInfo.GetValue(instance, null) == null;
            }
            catch
            {
                // Issue #799: If getting the property value throws an exception
                // then assume it's set and skip it.
                return false;
            }
        }

    }
}
