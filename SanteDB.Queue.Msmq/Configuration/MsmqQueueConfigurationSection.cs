using SanteDB.Core.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    [ExcludeFromCodeCoverage]
    public class MsmqQueueConfigurationSection : IConfigurationSection
    {
        /// <summary>
        /// The root to the queue
        /// </summary>
        [XmlElement("queuePath")]
        [DisplayName("MSMQ Path")]
        [Description("The path where the Microsoft Message Queue resides (example: .\\$Private or \\ServerName\\Queue")]
        public string QueuePath { get; set; }
    }
}