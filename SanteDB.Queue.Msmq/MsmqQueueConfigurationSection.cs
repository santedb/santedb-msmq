using SanteDB.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace SanteDB.Queue.Msmq.Configuration
{
    /// <summary>
    /// Microsoft Messaging Queue Configuration
    /// </summary>
    [XmlType(nameof(MsmqQueueConfigurationSection), Namespace = "http://santedb.org/configuration")]
    public class MsmqQueueConfigurationSection : IConfigurationSection
    {
        /// <summary>
        /// The root to the queue
        /// </summary>
        [XmlElement("queuePath")]
        public string QueuePath { get; set; }
    }
}