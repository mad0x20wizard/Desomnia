using Autofac;
using Autofac.Core.Resolving.Pipeline;
using MadWizard.Desomnia.Network.Configuration.Options;
using System.Runtime.InteropServices;

namespace MadWizard.Desomnia.Network.Middleware
{
    public sealed class DefaultBPFLimit : IResolveMiddleware
    {
        private readonly Func<int?> _detectLimit;

        public DefaultBPFLimit() : this(DetectPlatformLimit) { }

        internal DefaultBPFLimit(Func<int?> detectLimit)
        {
            _detectLimit = detectLimit;
        }

        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            if (context.FirstParameterOfType<BPFOptions>() is BPFOptions options)
            {
                if (options.InstructionLimit is null)
                {
                    options = options with { InstructionLimit = _detectLimit() };

                    context.ChangeParameterByType(options);

                    //context.ChangeParameters([TypedParameter.From(options),
                    //.. context.Parameters.Where(p => p is not TypedParameter typed || typed.Type != typeof(BPFOptions))]);
                }
            }

            //    var parameter = context.Parameters.OfType<TypedParameter>().FirstOrDefault(p => p.Type == typeof(BPFOptions));
            //var options = parameter?.Value is BPFOptions supplied ? supplied : default;

            next(context);
        }

        private static int? DetectPlatformLimit()
        {
            // Classic socket BPF: include/uapi/linux/bpf_common.h, BPF_MAXINSNS.
            if (OperatingSystem.IsLinux())
                return 4096;

            if (OperatingSystem.IsFreeBSD())
            {
                try
                {
                    nuint size = sizeof(int);
                    if (sysctlbyname("net.bpf.maxinsns", out int limit, ref size, IntPtr.Zero, 0) == 0
                        && size == sizeof(int) && limit > 0)
                        return limit;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }

            // In particular, Npcap has no documented limit-query API. Do not guess.
            return null;
        }

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sysctlbyname(string name, out int value, ref nuint size, IntPtr newValue, nuint newSize);
    }
}
