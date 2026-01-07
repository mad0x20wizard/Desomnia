using MadWizard.Desomnia.Pipe.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace MadWizard.Desomnia.Service.Bridge.Notification
{
    public interface ITokenDescriptor<T> where T : UsageToken
    {
        public UsageTokenInfo DescribeToken(T token);
    }
}
