﻿using SanteDB.Core.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using SanteDB.Core.Model;
using SanteDB.Core.Model.Serialization;
using SanteDB.Core.Queue;
using SanteDB.Core.Exceptions;
using SanteDB.Core.Security.Services;
using SanteDB.Core.Security;
using System.Messaging;
using SanteDB.Queue.Msmq.Configuration;

namespace SanteDB.Queue.Msmq
{
    /// <summary>
    /// A persistent queue service which uses MSMQ
    /// </summary>
    public class MsmqPersistentQueueService : IDispatcherQueueManagerService, IDisposable
    {
        // Queues that are open
        private ConcurrentDictionary<String, MessageQueue> m_queues = new ConcurrentDictionary<string, MessageQueue>();

        // Callbacks
        private ConcurrentDictionary<DispatcherQueueCallback, PeekCompletedEventHandler> m_callbacks = new ConcurrentDictionary<DispatcherQueueCallback, PeekCompletedEventHandler>();

        // Formatter
        private IMessageFormatter m_formatter = new BinaryMessageFormatter();

        // Configuration
        private MsmqQueueConfigurationSection m_configuration;

        // PEP service
        private readonly IPolicyEnforcementService m_pepService;

        /// <summary>
        /// DI constructor for MQ
        /// </summary>
        public MsmqPersistentQueueService(IConfigurationManager configurationManager, IPolicyEnforcementService pepService)
        {
            this.m_configuration = configurationManager.GetSection<MsmqQueueConfigurationSection>();
            this.m_pepService = pepService;
        }

        /// <summary>
        /// Get the service name
        /// </summary>
        public string ServiceName => "Microsoft Message Queue (MSMQ)";

        /// <summary>
        /// De-queue an object
        /// </summary>
        public DispatcherQueueEntry Dequeue(string queueName)
        {
            return this.DequeueById(queueName, null);
        }

        /// <summary>
        /// De-queue by identifier
        /// </summary>
        public DispatcherQueueEntry DequeueById(String queueName, String correlationId)
        {
            // Read from the queue if it is open
            if (this.m_queues.TryGetValue(queueName, out MessageQueue mq))
            {
                try
                {
                    Message mqMessage = null;
                    if (String.IsNullOrEmpty(correlationId))
                    {
                        mqMessage = mq.Receive();
                    }
                    else
                    {
                        mqMessage = mq.ReceiveById(correlationId.Replace("~", "\\"), new TimeSpan(0, 0, 0, 5));
                    }

                    mqMessage.Formatter = this.m_formatter;
                    var body = mqMessage.Body as Byte[];
                    using (var str = new MemoryStream(body))
                    {
                        var type = Type.GetType(mqMessage.Label);
                        var xsz = XmlModelSerializerFactory.Current.CreateSerializer(type);
                        return new DispatcherQueueEntry(mqMessage.Id.Replace("\\", "~"), queueName, mqMessage.ArrivedTime, mqMessage.Label, xsz.Deserialize(str));
                    }
                }
                catch (Exception e)
                {
                    throw new DataPersistenceException($"Error de-queueing message from {queueName}", e);
                }
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Dispose of this object
        /// </summary>
        public void Dispose()
        {
            foreach (var q in this.m_queues.Values)
            {
                q.Dispose();
            }
        }

        /// <summary>
        /// Enqueue an object
        /// </summary>
        public void Enqueue(string queueName, object data)
        {
            if (!this.m_queues.TryGetValue(queueName, out MessageQueue mq))
            {
                mq = this.OpenQueueInternal(queueName);
            }

            try
            {
                using (var ms = new MemoryStream())
                {
                    XmlModelSerializerFactory.Current.CreateSerializer(data.GetType()).Serialize(ms, data);
                    var message = new Message(ms.GetBuffer());
                    message.Formatter = this.m_formatter;
                    message.Label = data.GetType().AssemblyQualifiedName;
                    mq.Send(message);
                }
            }
            catch (Exception e)
            {
                throw new DataPersistenceException($"Error enqueueing message to {queueName}", e);
            }
        }

        /// <summary>
        /// Gets all queue entries in the specified queue
        /// </summary>
        public IEnumerable<DispatcherQueueEntry> GetQueueEntries(string queueName)
        {
            if (!this.m_queues.TryGetValue(queueName, out MessageQueue mq))
            {
                throw new KeyNotFoundException($"No queue named {queueName} found");
            }

            using (var enu = mq.GetMessageEnumerator2())
            {
                while (enu.MoveNext())
                {
                    var msg = enu.Current;
                    msg.Formatter = this.m_formatter;
                    //enu.Current.Formatter = this.m_formatter;
                    yield return new DispatcherQueueEntry(enu.Current.Id.Replace("\\", "~"), queueName, msg.ArrivedTime, msg.Label, msg.Body);
                }
            }
        }

        /// <summary>
        /// Get a specific queue entry
        /// </summary>
        public DispatcherQueueEntry GetQueueEntry(string queueName, string correlationId)
        {
            if (this.m_queues.TryGetValue(queueName, out var mq))
            {
                var mqMessage = mq.PeekById(correlationId.Replace("~", "\\"), new TimeSpan(0, 0, 5));
                if (mqMessage == null)
                    throw new KeyNotFoundException($"No message with correlationId {correlationId} found");

                mqMessage.Formatter = this.m_formatter;

                return new DispatcherQueueEntry(mqMessage.Id.Replace("\\", "~"), queueName, mqMessage.ArrivedTime, mqMessage.Label, mqMessage.Body);
            }
            throw new KeyNotFoundException($"No queue named {queueName} found");
        }

        /// <summary>
        /// Get all queues
        /// </summary>
        public IEnumerable<DispatcherQueueInfo> GetQueues() => this.m_queues.Select(o => new DispatcherQueueInfo()
        {
            Id = o.Key,
            Name = o.Value.QueueName,
            QueueSize = o.Value.GetAllMessages().Count(),
            CreationTime = o.Value.CreateTime
        });

        /// <summary>
        /// Move a queue entry to another queue
        /// </summary>
        public DispatcherQueueEntry Move(DispatcherQueueEntry entry, string toQueue)
        {
            // Attempt a move
            if (!this.m_queues.TryGetValue(entry.SourceQueue, out MessageQueue sourceQueue))
            {
                sourceQueue = this.OpenQueueInternal(entry.SourceQueue);
            }

            if (!this.m_queues.TryGetValue(toQueue, out MessageQueue targetQueue))
            {
                targetQueue = this.OpenQueueInternal(toQueue);
            }

            try
            {
                var sourceMessage = sourceQueue.ReceiveById(entry.CorrelationId);
                sourceMessage.Formatter = this.m_formatter;
                var newMessage = new Message(sourceMessage.Body);
                newMessage.Formatter = this.m_formatter;

                newMessage.Label = sourceMessage.Label;
                targetQueue.Send(newMessage);
                return new DispatcherQueueEntry(newMessage.Id.Replace("\\", "~"), toQueue, newMessage.ArrivedTime, newMessage.Label, sourceMessage.Body);
            }
            catch (Exception e)
            {
                throw new DataPersistenceException($"Error moving message from {entry.SourceQueue} to {toQueue}", e);
            }
        }

        /// <summary>
        /// Open the configuration
        /// </summary>
        /// <param name="queueName"></param>
        public void Open(string queueName)
        {
            try
            {
                if (!this.m_queues.TryGetValue(queueName, out MessageQueue mq))
                {
                    mq = this.OpenQueueInternal(queueName);
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Error opening queue {queueName}", e);
            }
        }

        /// <summary>
        /// Open queue internal - open the queue but don't subscribe
        /// </summary>
        private MessageQueue OpenQueueInternal(string queueName)
        {
            if (!this.m_queues.TryGetValue(queueName, out var mq))
            {
                var queueConnection = this.m_configuration?.QueuePath ?? ".\\Private$";
                var queuePath = $"{queueConnection}\\sdb.{queueName}";
                // Do we need to create the queue?
                if (MessageQueue.Exists(queuePath))
                {
                    mq = new MessageQueue(queuePath);
                }
                else
                {
                    mq = MessageQueue.Create(queuePath);
                    mq.Label = $"SanteDB Queue {queueName}";
                }

                mq.MessageReadPropertyFilter.ArrivedTime = true;
                mq.MessageReadPropertyFilter.Id = true;

                this.m_queues.TryAdd(queueName, mq);
            }
            return mq;
        }

        /// <summary>
        /// Purge the entire queue
        /// </summary>
        public void Purge(string queueName)
        {
            this.m_pepService.Demand(PermissionPolicyIdentifiers.ManageDispatcherQueues);

            if (this.m_queues.TryGetValue(queueName, out var mq))
            {
                mq.Purge();
            }
        }

        /// <summary>
        /// Subscribe to the queue callback
        /// </summary>
        /// <remarks>This method exists because there is only one "queue manager" however
        /// not all subscribers are intersted in getting events for any old queue</remarks>
        public void SubscribeTo(string queueName, DispatcherQueueCallback callback)
        {
            if (!this.m_callbacks.TryGetValue(callback, out var eventHandler))
            {
                var mq = this.OpenQueueInternal(queueName);
                eventHandler = (o, e) =>
                {
                    callback(new DispatcherMessageEnqueuedInfo(queueName, e.Message.Id.Replace("\\", "~")));
                    mq.BeginPeek();
                };
                mq.PeekCompleted += eventHandler;
                mq.BeginPeek();

                this.m_callbacks.TryAdd(callback, eventHandler);
            }
        }

        /// <summary>
        /// Un-subscribe to the event
        /// </summary>
        /// <param name="queueName"></param>
        /// <param name="callback"></param>
        public void UnSubscribe(string queueName, DispatcherQueueCallback callback)
        {
            if (this.m_callbacks.TryGetValue(callback, out var eventHandler))
            {
                var mq = this.OpenQueueInternal(queueName);
                mq.PeekCompleted -= eventHandler;
            }
        }
    }
}