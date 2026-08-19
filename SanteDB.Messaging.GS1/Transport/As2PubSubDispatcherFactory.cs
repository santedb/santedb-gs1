using SanteDB.Core;
using SanteDB.Core.Configuration.Http;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Http;
using SanteDB.Core.Http.Authentication;
using SanteDB.Core.Model;
using SanteDB.Core.Model.Acts;
using SanteDB.Core.Model.Constants;
using SanteDB.Core.Model.DataTypes;
using SanteDB.Core.Model.Entities;
using SanteDB.Core.PubSub;
using SanteDB.Core.Security;
using SanteDB.Core.Services;
using SanteDB.Messaging.GS1.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace SanteDB.Messaging.GS1.Transport
{
    /// <summary>
    /// Represents a dispatcher factory for AS2 messaging
    /// </summary>
    public class As2PubSubDispatcherFactory : IPubSubDispatcherFactory
    {

        // Client factory
        private readonly IRestClientFactory m_restClientFactory;
        private readonly Gs1Util m_gs1Util;

        /// <inheritdoc/>
        public string Id => "gs1-bms-as2";

        /// <inheritdoc/>
        public IEnumerable<string> Schemes => new string[]
        {
            $"{Id}-http", $"{Id}-https"
        };

        /// <summary>
        /// DI constructor
        /// </summary>
        public As2PubSubDispatcherFactory(IRestClientFactory restClientFactory, Gs1Util gs1Util = null)
        {
            this.m_restClientFactory = restClientFactory;
            this.m_gs1Util = gs1Util ?? typeof(Gs1Util).CreateInjected() as Gs1Util;
        }

        /// <summary>
        /// AS.2 Dispatcher using POX over HTTP/HTTPS
        /// </summary>
        private class As2Dispatcher : IPubSubDispatcher
        {
            private readonly As2ServiceClient m_as2Client;
            private readonly Gs1Util m_gs1Util;
            private readonly Tracer m_tracer = Tracer.GetTracer(typeof(As2Dispatcher));

            public As2Dispatcher(Guid channelKey, Uri endpoint, IRestClientFactory clientFactory, Gs1Util gs1Util, IDictionary<String, String> settings)
            {
                this.Endpoint = endpoint;
                this.Key = channelKey;
                this.Settings = settings;
                this.m_gs1Util = gs1Util;

                if (!settings.TryGetValue(Gs1Constants.PubsubAs2MimeEncodingSettingName, out var useMimeRaw) || !Boolean.TryParse(useMimeRaw, out var useMime))
                {
                    useMime = false;
                }

                var client = clientFactory.CreateRestClient(new RestClientDescriptionConfiguration()
                {
                    Accept = "application/xml",
                    Endpoint = new List<RestClientEndpointConfiguration>()
                    {
                        new RestClientEndpointConfiguration(endpoint.ToString())
                    },
                    Name = $"GS1_AS2_CLIENT_{channelKey}",
                    Binding = new RestClientBindingConfiguration()
                    {
                        CompressRequests = false,
                        ContentTypeMapper = new DefaultContentTypeMapper(),
                        OptimizationMethod = Core.Http.Description.HttpCompressionAlgorithm.None,
                        Security = new RestClientSecurityConfiguration()
                        {
                            PreemptiveAuthentication = true,
                            CredentialProvider = settings.TryGetValue(Gs1Constants.PubsubAuthenticatorSettingName, out var useAuthenticator) &&
                                clientFactory.TryGetCredentialFactory(useAuthenticator, out var provider) ? provider.GetCredentialProvider(settings) : null
                        }
                    }
                });

                this.m_as2Client = new As2ServiceClient(client, useMime);
            }

            /// <inheritdoc/>
            public Guid Key { get; }

            /// <inheritdoc/>
            public Uri Endpoint { get; }

            /// <inheritdoc/>
            public IDictionary<string, string> Settings { get; }


            /// <summary>
            /// Issue receive advice
            /// </summary>
            private ReceivingAdviceMessageType CreateReceiveAdvice(Act act)
            {
                var receiveMessage = new ReceivingAdviceMessageType();
                receiveMessage.StandardBusinessDocumentHeader = this.m_gs1Util.CreateDocumentHeader("receivingAdvice", act.LoadProperty(o => o.Participations).FirstOrDefault(o => o.ParticipationRoleKey == ActParticipationKeys.Authororiginator).LoadProperty<Entity>("PlayerEntity"));

                var originalOrder = act.LoadProperty(o => o.Relationships).FirstOrDefault(o => o.RelationshipTypeKey == ActRelationshipTypeKeys.Arrival)?.LoadProperty(o => o.TargetAct);

                Entity shipTo = act.LoadProperty(o => o.Participations).FirstOrDefault(o => o.ParticipationRoleKey == ActParticipationKeys.Location)?.LoadProperty(o => o.PlayerEntity),
                    shipFrom = originalOrder.LoadProperty(o => o.Participations).FirstOrDefault(o => o.ParticipationRoleKey == ActParticipationKeys.Destination)?.LoadProperty(o => o.PlayerEntity),
                    storedContainer = act.LoadProperty(o => o.Participations).FirstOrDefault(o => o.ParticipationRoleKey == ActParticipationKeys.Destination)?.LoadProperty(o => o.PlayerEntity);

                // Receive message advice
                receiveMessage.receivingAdvice = new ReceivingAdviceType[]
                {
                    new ReceivingAdviceType()
                    {
                        creationDateTime = act.CreationTime.DateTime,
                        documentStatusCode = DocumentStatusEnumerationType.ORIGINAL,
                        receivingAdviceIdentification = new Ecom_EntityIdentificationType()
                        {
                            entityIdentification = act.Key.ToString()
                        },
                        receivingDateTime = act.ActTime.GetValueOrDefault().DateTime,
                        despatchAdvice = new Ecom_DocumentReferenceType()
                        {
                            entityIdentification = originalOrder.Identifiers.FirstOrDefault()?.Value ?? originalOrder.Key.Value.ToString(),
                            creationDateTime = originalOrder.ActTime.GetValueOrDefault().DateTime,
                            creationDateTimeSpecified = true,
                            contentOwner = new Ecom_PartyIdentificationType()
                            {
                                additionalPartyIdentification = originalOrder.Identifiers.Count > 0 ? new AdditionalPartyIdentificationType[]
                                {
                                    new AdditionalPartyIdentificationType() { Value = originalOrder.Identifiers.FirstOrDefault()?.IdentityDomain.Oid, additionalPartyIdentificationTypeCode = "urn:oid:" },
                                } : null,
                            }
                        },
                        reportingCode = new GoodsReceiptReportingCodeType() { Value = "FULL_DETAILS" },
                        shipper = this.m_gs1Util.CreateLocation(shipFrom as Place),
                        shipTo = this.m_gs1Util.CreateLocation(shipTo as Place),
                        receiver = this.m_gs1Util.CreateLocation(shipTo as Place),
                        receivingAdviceLogisticUnit = act.Participations.Where(o=>o.ParticipationRoleKey == ActParticipationKeys.Consumable).Select(o=> this.m_gs1Util.CreateReceiveLineItem(o, originalOrder.Participations.FirstOrDefault(p=>p.PlayerEntityKey == o.PlayerEntityKey))).ToArray(),
                        inventoryLocation = this.m_gs1Util.CreateInventoryLocation(shipTo as Place, storedContainer as Container)
                    }
                };

                for (int i = 0; i < receiveMessage.receivingAdvice[0].receivingAdviceLogisticUnit.Length; i++)
                {
                    receiveMessage.receivingAdvice[0].receivingAdviceLogisticUnit[i].receivingAdviceLineItem[0].lineItemNumber = (i + 1).ToString();
                }

                // Queue The order on the file system
                return receiveMessage;
            }

            /// <summary>
            /// Issue order information
            /// </summary>
            private OrderMessageType CreateOrderMessage(Act act)
            {
                var orderMessage = new OrderMessageType();

                orderMessage.StandardBusinessDocumentHeader = this.m_gs1Util.CreateDocumentHeader("order", act.LoadCollection<ActParticipation>("Participations").FirstOrDefault(o => o.ParticipationRoleKey == ActParticipationKeys.Authororiginator).LoadProperty<Entity>("PlayerEntity"));

                var type = ApplicationServiceContext.Current.GetService<IConceptRepositoryService>().GetConceptReferenceTerm(act.TypeConceptKey.Value, "GS1");

                Place shipTo = act.LoadCollection<ActParticipation>("Participations").FirstOrDefault(o => o.ParticipationRoleKey == ActParticipationKeys.Location)?.LoadProperty<Place>("PlayerEntity"),
                    shipFrom = act.LoadCollection<ActParticipation>("Participations").FirstOrDefault(o => o.ParticipationRoleKey == ActParticipationKeys.Distributor)?.LoadProperty<Place>("PlayerEntity");

                OrderType order = new OrderType()
                {
                    creationDateTime = act.CreationTime.DateTime,
                    documentStatusCode = DocumentStatusEnumerationType.ORIGINAL,
                    orderIdentification = new Ecom_EntityIdentificationType()
                    {
                        entityIdentification = act.Key.Value.ToString()
                    },
                    orderTypeCode = new OrderTypeCodeType() { Value = type?.Mnemonic, codeListVersion = type?.LoadProperty<CodeSystem>("CodeSystem").Domain },
                    isApplicationReceiptAcknowledgementRequired = true,
                    isApplicationReceiptAcknowledgementRequiredSpecified = true,
                    note = new Description500Type() { Value = act.LoadCollection<ActNote>("Notes").FirstOrDefault()?.Text },
                    isOrderFreeOfExciseTaxDuty = false,
                    isOrderFreeOfExciseTaxDutySpecified = true,
                    orderLogisticalInformation = new OrderLogisticalInformationType()
                    {
                        shipFrom = this.m_gs1Util.CreateLocation(shipFrom),
                        shipTo = this.m_gs1Util.CreateLocation(shipTo),
                        orderLogisticalDateInformation = new OrderLogisticalDateInformationType()
                        {
                            requestedDeliveryDateTime = new DateOptionalTimeType()
                            {
                                date = act.ActTime.GetValueOrDefault().Date
                            }
                        }
                    },
                    orderLineItem = act.LoadCollection<ActParticipation>("Participations").Where(o => o.ParticipationRoleKey == ActParticipationKeys.Product).Select(o => this.m_gs1Util.CreateOrderLineItem(o)).ToArray()
                };

                for (int i = 0; i < order.orderLineItem.Length; i++)
                {
                    order.orderLineItem[i].lineItemNumber = (i + 1).ToString();
                }

                orderMessage.order = new OrderType[] { order };

                return orderMessage;
            }

            /// <inheritdoc/>
            public void NotifyCreated<TModel>(TModel data) where TModel : IdentifiedData
            {
                if (data is Act tact)
                {
                    if (tact.TypeConceptKey == Gs1Constants.ActTypeOrder &&
                        tact.ClassConceptKey == ActClassKeys.Supply &&
                        tact.MoodConceptKey == ActMoodKeys.Request) // Order Request
                    {
                        this.m_as2Client.IssueOrder(this.CreateOrderMessage(tact));
                    }
                    else if (tact.TypeConceptKey == Gs1Constants.ActTypeOrderReceipt &&
                        tact.ClassConceptKey == ActClassKeys.Supply &&
                        tact.MoodConceptKey == ActMoodKeys.Eventoccurrence &&
                        tact.StatusConceptKey == StatusKeys.Completed)
                    {
                        this.m_as2Client.IssueReceivingAdvice(this.CreateReceiveAdvice(tact));
                    }
                    else if (tact.TypeConceptKey == Gs1Constants.ActTypeOrderDespatch &&
                        tact.ClassConceptKey == ActClassKeys.Supply &&
                        tact.MoodConceptKey == ActMoodKeys.Eventoccurrence &&
                        tact.StatusConceptKey == StatusKeys.Completed)
                    {
                        this.m_as2Client.IssueDespatchAdvice(this.CreateDespatchAdvice(tact));
                    }
                    else
                    {
                        this.m_tracer.TraceWarning("GS1 interface cannot interpret act {0}", tact);
                    }
                }
                else
                {
                    this.m_tracer.TraceWarning("GS1 Processor cannot be used to subscrie to events of type {0}", data);
                }
            }

            /// <summary>
            /// Create a DESPATCH advice message
            /// </summary>
            private DespatchAdviceMessageType CreateDespatchAdvice(Act tact)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public void NotifyLinked<TModel>(TModel primary, TModel target) where TModel : IdentifiedData
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public void NotifyMerged<TModel>(TModel survivor, IEnumerable<TModel> subsumed) where TModel : IdentifiedData
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public void NotifyObsoleted<TModel>(TModel data) where TModel : IdentifiedData
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public void NotifyUnlinked<TModel>(TModel holder, TModel target) where TModel : IdentifiedData
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public void NotifyUnMerged<TModel>(TModel primary, IEnumerable<TModel> unMerged) where TModel : IdentifiedData
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public void NotifyUpdated<TModel>(TModel data) where TModel : IdentifiedData
            {
                throw new NotImplementedException();
            }
        }

        /// <inheritdoc/>
        public IPubSubDispatcher CreateDispatcher(Guid channelKey, Uri endpoint, IDictionary<string, string> settings)
        {
            return new As2Dispatcher(channelKey, endpoint, this.m_restClientFactory, this.m_gs1Util, settings);
        }
    }
}
