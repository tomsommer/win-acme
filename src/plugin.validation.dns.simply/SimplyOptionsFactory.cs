using ACMESharp.Authorizations;
using PKISharp.WACS.Configuration;
using PKISharp.WACS.DomainObjects;
using PKISharp.WACS.Plugins.Base.Factories;
using PKISharp.WACS.Services;
using PKISharp.WACS.Services.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PKISharp.WACS.Plugins.ValidationPlugins.Dns
{
    /// <summary>
    /// Simply DNS validation
    /// </summary>
    internal class SimplyOptionsFactory : PluginOptionsFactory<SimplyOptions>
    {
        private readonly ArgumentsInputService _arguments;

        public SimplyOptionsFactory(ArgumentsInputService arguments) => _arguments = arguments;

        private ArgumentResult<ProtectedString?> ApiKey => _arguments.
            GetProtectedString<SimplyArguments>(a => a.ApiKey).
            Required();

        public override async Task<SimplyOptions?> Aquire(IInputService input, RunLevel runLevel)
        {
            return new SimplyOptions()
            {
                ApiKey = await ApiKey.Interactive(input).GetValue()
            };
        }

        public override async Task<SimplyOptions?> Default()
        {
            return new SimplyOptions()
            {
                ApiKey = await ApiKey.GetValue()
            };
        }

        public override IEnumerable<(CommandLineAttribute, object?)> Describe(SimplyOptions options)
        {
            yield return (ApiKey.Meta, options.ApiKey);
        }
    }
}
