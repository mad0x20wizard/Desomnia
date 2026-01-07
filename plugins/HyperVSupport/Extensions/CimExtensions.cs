using MadWizard.Desomnia.Network.Manager;
using Microsoft.Management.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Management.Infrastructure
{
    internal static class CimExtensions
    {
        internal static IEnumerable<CimInstance> EnumerateAssociatedInstances(this CimSession session, string namespaceName, CimInstance sourceInstance, string resultClassName)
        {
            return session.EnumerateAssociatedInstances(namespaceName, sourceInstance,
                associationClassName: "",
                resultClassName: resultClassName,
                sourceRole: "",
                resultRole: "");
        }
    }
}
