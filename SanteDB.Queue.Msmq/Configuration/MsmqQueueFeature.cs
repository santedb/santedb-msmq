using SanteDB.Core.Configuration;
using SanteDB.Core.Configuration.Features;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SanteDB.Queue.Msmq.Configuration
{
    /// <summary>
    /// Microsoft Message Queue configuration tool feature
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class MsmqQueueFeature : GenericServiceFeature<MsmqPersistentQueueService>
    {

        /// <summary>
        /// Gets the name of the queue
        /// </summary>
        public override string Name => "Microsoft Message Queue";
        
        /// <summary>
        /// Gets the group 
        /// </summary>
        public override string Group => FeatureGroup.System;

        /// <summary>
        /// Gets the configuration service
        /// </summary>
        public override Type ConfigurationType => typeof(MsmqQueueConfigurationSection);

        /// <summary>
        /// Get the default configuration
        /// </summary>
        protected override object GetDefaultConfiguration()
        {
            return new MsmqQueueConfigurationSection()
            {
                QueuePath = ".\\Private$"
            };
        }
    }
}
