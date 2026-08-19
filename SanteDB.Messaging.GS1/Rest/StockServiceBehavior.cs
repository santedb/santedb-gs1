/*
 * Portions Copyright 2019-2025, Fyfe Software Inc. and the SanteSuite Contributors (See NOTICE)
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you 
 * may not use this file except in compliance with the License. You may 
 * obtain a copy of the License at 
 * 
 * http://www.apache.org/licenses/LICENSE-2.0 
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations under 
 * the License.
 * 
 */
using RestSrvr.Attributes;
using SanteDB.Core;
using SanteDB.Core.Diagnostics;
using SanteDB.Core.Extensions;
using SanteDB.Core.i18n;
using SanteDB.Core.Model.Acts;
using SanteDB.Core.Model.Collection;
using SanteDB.Core.Model.Constants;
using SanteDB.Core.Model.DataTypes;
using SanteDB.Core.Model.Entities;
using SanteDB.Core.Security;
using SanteDB.Core.Services;
using SanteDB.Messaging.GS1.Configuration;
using SanteDB.Messaging.GS1.Model;
using SanteDB.Rest.Common.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;

namespace SanteDB.Messaging.GS1.Rest
{
    /// <summary>
    /// GS1 BMS 3.3
    /// </summary>
    /// <remarks>The SanteDB server implementation of the GS1 BMS 3.3 interface over REST</remarks>
    [ServiceBehavior(Name = StockServiceMessageHandler.ConfigurationName, InstanceMode = ServiceInstanceMode.Singleton)]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class StockServiceBehavior : IStockService, IServiceImplementation
    {

        /// <summary>
        /// Used for conveying logistics inventory report filters
        /// </summary>
        private struct LogisticsReportFilter
        {
            public Place Place { get; set; }

            public DateTime? FromDate { get; set; }

            public DateTime? ToDate { get; set; }

        }

        // Configuration
        private Gs1ConfigurationSection m_configuration = ApplicationServiceContext.Current.GetService<IConfigurationManager>().GetSection<Gs1ConfigurationSection>();
        private readonly IIdentityDomainRepositoryService m_identityDomain;
        private readonly IConceptRepositoryService m_conceptRepository;
        private readonly IdentityDomain m_gln;
        private readonly IdentityDomain m_gtin;

        // Act repository
        private IRepositoryService<Act> m_actRepository;
        private readonly IRepositoryService<EntityRelationship> m_entityRelationshipRepository;

        // Material repository
        private IRepositoryService<Material> m_materialRepository;

        // Manufactured materials
        private IRepositoryService<ManufacturedMaterial> m_manufMaterialRepository;

        // Place repository
        private IRepositoryService<Place> m_placeRepository;

        // Stock service
        private IStockManagementService m_stockService;

        // GS1 Utility
        private Gs1Util m_gs1Util;

        // Tracer
        private readonly Tracer m_tracer = new Tracer(Gs1Constants.TraceSourceName);

        // Localization Service
        private readonly ILocalizationService m_localizationService;

        public StockServiceBehavior() :
            this(
                ApplicationServiceContext.Current.GetService<ILocalizationService>(),
                ApplicationServiceContext.Current.GetService<IRepositoryService<Act>>(),
                ApplicationServiceContext.Current.GetService<IRepositoryService<Material>>(),
                ApplicationServiceContext.Current.GetService<IRepositoryService<Place>>(),
                ApplicationServiceContext.Current.GetService<IStockManagementService>(),
                ApplicationServiceContext.Current.GetService<IRepositoryService<ManufacturedMaterial>>(),
                ApplicationServiceContext.Current.GetService<IRepositoryService<EntityRelationship>>(),
                ApplicationServiceContext.Current.GetService<IIdentityDomainRepositoryService>(),
                ApplicationServiceContext.Current.GetService<IConceptRepositoryService>(),
                ApplicationServiceContext.Current.GetService<Gs1Util>()
            )
        {

        }

        /// <summary>
        /// Default ctor setting services
        /// </summary>
        public StockServiceBehavior(ILocalizationService localizationService,
            IRepositoryService<Act> actRepository,
            IRepositoryService<Material> materialRepository,
            IRepositoryService<Place> placeRepository,
            IStockManagementService stockManagementService,
            IRepositoryService<ManufacturedMaterial> manufacturedMaterialRepository,
            IRepositoryService<EntityRelationship> entityRelationshipRepository,
            IIdentityDomainRepositoryService identityDomainRepositoryService,
            IConceptRepositoryService conceptRepositoryService,
            Gs1Util gs1Util = null)
        {

            this.m_identityDomain = identityDomainRepositoryService;

            this.m_conceptRepository = conceptRepositoryService;
            // Attempt to get the GLN and GTIN
            this.m_gln = identityDomainRepositoryService.Get(IdentityDomainKeys.Gs1GlobalLocationNumber);
            this.m_gtin = identityDomainRepositoryService.Get(IdentityDomainKeys.Gs1GlobalTradeIdentificationNumber);
            if (this.m_gln == null || this.m_gtin == null)
            {
                throw new InvalidOperationException(String.Format(ErrorMessages.DEPENDENT_CONFIGURATION_MISSING, "GTIN and GLN DOMAINS"));
            }

            this.m_actRepository = actRepository;
            this.m_entityRelationshipRepository = entityRelationshipRepository;
            this.m_materialRepository = materialRepository;
            this.m_placeRepository = placeRepository;
            this.m_stockService = stockManagementService;
            this.m_manufMaterialRepository = manufacturedMaterialRepository;
            this.m_gs1Util = gs1Util ?? typeof(Gs1Util).CreateInjected() as Gs1Util;
            this.m_localizationService = localizationService;

            if(this.m_stockService == null)
            {
                throw new InvalidOperationException(String.Format(ErrorMessages.SERVICE_NOT_FOUND, typeof(IStockManagementService)));
            }
        }

        // HDSI Trace host
        private readonly Tracer traceSource = new Tracer(Gs1Constants.TraceSourceName);

        /// <summary>
        /// Get service name
        /// </summary>
        public string ServiceName => "Stock Service Behavior";

        /// <summary>
        /// The issue despactch advice message will insert a new shipped order into the TImR system.
        /// </summary>
        [Demand(PermissionPolicyIdentifiers.LoginAsService)]
        public void IssueDespatchAdvice(DespatchAdviceMessageType advice)
        {
            if (advice == null || advice.despatchAdvice == null)
            {
                this.m_tracer.TraceError("Invalid message sent");
                throw new InvalidOperationException(this.m_localizationService.GetString("error.messaging.gs1.invalidMessage"));
            }

            // TODO: Validate the standard header
            // Loop
            Bundle orderTransaction = new Bundle();

            foreach (var adv in advice.despatchAdvice)
            {
                // Has this already been created?
                Place sourceLocation = this.m_gs1Util.GetLocation(adv.shipper),
                    destinationLocation = this.m_gs1Util.GetLocation(adv.receiver);
                if (sourceLocation == null)
                {
                    this.m_tracer.TraceError($"Shipper location not found");
                    throw new KeyNotFoundException(this.m_localizationService.GetString("error.messaging.gs1.locationNotFound", new
                    {
                        param = "Shipper"
                    }));
                }
                else if (destinationLocation == null)
                {
                    this.m_tracer.TraceError($"Shipper location not found");
                    throw new KeyNotFoundException(this.m_localizationService.GetString("error.messaging.gs1.locationNotFound", new
                    {
                        param = "Receiver"
                    }));
                }
                // Find the original order which this despatch advice is fulfilling
                Act orderRequestAct = null;
                if (adv.orderResponse != null || adv.purchaseOrder != null)
                {
                    orderRequestAct = this.m_gs1Util.GetOrder(adv.orderResponse ?? adv.purchaseOrder, ActMoodKeys.Request);
                    if (orderRequestAct != null) // Orderless despatch!
                    {
                        // If the original order request is not comlete, then complete it
                        orderRequestAct.StatusConceptKey = StatusKeys.Completed;
                        orderTransaction.Add(orderRequestAct);
                    }
                }

                // Find the author of the shipment

                var oidService = ApplicationServiceContext.Current.GetService<IIdentityDomainRepositoryService>();
                var gln = oidService.Get("GLN");
                IdentityDomain issuingAuthority = null;
                if (adv.despatchAdviceIdentification.contentOwner != null)
                {
                    issuingAuthority = oidService.Find(o => o.Oid == $"{gln.Oid}.{adv.despatchAdviceIdentification.contentOwner.gln}").FirstOrDefault();
                }

                if (issuingAuthority == null)
                {
                    issuingAuthority = oidService.Get(this.m_configuration.DefaultContentOwnerAssigningAuthority);
                }

                if (issuingAuthority == null)
                {
                    this.m_tracer.TraceError("Could not find default issuing authority for advice identification. Please configure a valid OID");
                    throw new KeyNotFoundException(this.m_localizationService.GetString("error.messaging.gs1.issuingAuthority"));
                }

                var existing = this.m_actRepository.Find(o => o.Identifiers.Any(i => i.IdentityDomainKey == issuingAuthority.Key && i.Value == adv.despatchAdviceIdentification.entityIdentification));
                if (existing.Any())
                {
                    this.m_tracer.TraceWarning("Duplicate despatch {0} will be ignored", adv.despatchAdviceIdentification.entityIdentification);
                    continue;
                }


                // Now we want to create a new Supply act which that fulfills the old act
                Act fulfillAct = new Act()
                {
                    CreationTime = DateTimeOffset.Now,
                    MoodConceptKey = ActMoodKeys.Eventoccurrence,
                    ClassConceptKey = ActClassKeys.Supply,
                    StatusConceptKey = StatusKeys.Active,
                    TypeConceptKey = Guid.Parse("14d69b32-f6c4-4a49-a527-a74893dbcf4a"), // Order
                    ActTime = adv.despatchInformation.despatchDateTimeSpecified ? adv.despatchInformation.despatchDateTime : DateTime.Now,
                    Extensions = new List<ActExtension>()
                    {
                        new ActExtension(Gs1ModelExtensions.ActualShipmentDate, typeof(DateExtensionHandler), adv.despatchInformation.actualShipDateTime),
                        new ActExtension(Gs1ModelExtensions.ExpectedDeliveryDate, typeof(DateExtensionHandler), adv.despatchInformation.estimatedDeliveryDateTime)
                    },
                    Tags = new List<ActTag>()
                    {
                        new ActTag("orderNumber", adv.despatchAdviceIdentification.entityIdentification),
                        new ActTag("orderStatus", "shipped"),
                        new ActTag("http://santedb.org/tags/contrib/importedData", "true")
                    },
                    Identifiers = new List<ActIdentifier>()
                    {
                        new ActIdentifier(issuingAuthority, adv.despatchAdviceIdentification.entityIdentification)
                    },
                    Participations = new List<ActParticipation>()
                    {
                        // TODO: Author
                        // TODO: Performer
                        new ActParticipation(ActParticipationKeys.Location, sourceLocation.Key),
                        new ActParticipation(ActParticipationKeys.Destination, destinationLocation.Key)
                    }
                };
                orderTransaction.Add(fulfillAct);

                // Fullfillment
                if (orderRequestAct != null)
                {
                    fulfillAct.Relationships = new List<ActRelationship>()
                    {
                        new ActRelationship(ActRelationshipTypeKeys.Fulfills, orderRequestAct.Key)
                    };
                }

                // Now add participations for each material in the despatch
                foreach (var dal in adv.despatchAdviceLogisticUnit)
                {
                    foreach (var line in dal.despatchAdviceLineItem)
                    {
                        if (line.despatchedQuantity.measurementUnitCode != "dose" &&
                            line.despatchedQuantity.measurementUnitCode != "unit")
                        {
                            this.m_tracer.TraceError("Despatched quantity must be reported in units or doses");
                            throw new InvalidOperationException(this.m_localizationService.GetString("error.messaging.gs1.despatchedQuantity"));
                        }
                        var material = this.m_gs1Util.GetManufacturedMaterial(line.transactionalTradeItem, this.m_configuration.AutoCreateMaterials);

                        // Add a participation
                        fulfillAct.Participations.Add(new ActParticipation(ActParticipationKeys.Consumable, material.Key)
                        {
                            Quantity = (int)line.despatchedQuantity.Value
                        });
                    }
                }
            }

            // insert transaction
            if (orderTransaction.Item.Count > 0)
            {
                try
                {
                    ApplicationServiceContext.Current.GetService<IRepositoryService<Bundle>>().Insert(orderTransaction);
                }
                catch (Exception e)
                {
                    this.m_tracer.TraceError("Error issuing despatch advice: {0}", e);
                    throw new Exception(this.m_localizationService.GetString("error.messaging.gs1.errorIssuing", new
                    {
                        param = e.Message
                    }), e);
                }
            }
        }

        /// <summary>
        /// Requests the issuance of a BMS1 inventory report request
        /// </summary>
        [Demand(PermissionPolicyIdentifiers.LoginAsService)]
        public LogisticsInventoryReportMessageType IssueInventoryReportRequest(LogisticsInventoryReportRequestMessageType parameters)
        {
            var retVal = new LogisticsInventoryReportMessageType()
            {
                StandardBusinessDocumentHeader = this.m_gs1Util.CreateDocumentHeader("logisticsInventoryReport", null)
            };

            var report = new LogisticsInventoryReportType()
            {
                creationDateTime = DateTime.Now,
                documentStatusCode = DocumentStatusEnumerationType.ORIGINAL,
                documentActionCode = DocumentActionEnumerationType.CHANGE_BY_REFRESH,
                logisticsInventoryReportIdentification = new Ecom_EntityIdentificationType() { entityIdentification = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0).ToString("X") },
                structureTypeCode = new StructureTypeCodeType() { Value = "LOCATION_BY_ITEM" },
                documentActionCodeSpecified = true,
                documentStructureVersion = "3.3",
            };

            // Next, we want to know which facilities for which we're getting the inventory report
            var filterPlaces = new List<LogisticsReportFilter>();
            foreach (var reportTarget in parameters.logisticsInventoryReportRequest ?? new LogisticsInventoryReportRequestType[0])
            {

                DateTime? reportFrom = reportTarget.reportingPeriod?.beginDate ?? DateTime.MinValue,
                    reportTo = reportTarget.reportingPeriod?.endDate ?? DateTime.Now;

                if (reportTarget.logisticsInventoryReportTypeCode != LogisticsInventoryReportTypeEnumerationType.FULL_STATUS_REPORT &&
                    reportTarget.logisticsInventoryReportTypeCode != LogisticsInventoryReportTypeEnumerationType.TRADE_ITEM_STATUS_REPORT)
                {
                    throw new ArgumentOutOfRangeException(string.Format(ErrorMessages.ARGUMENT_OUT_OF_RANGE, reportTarget.logisticsInventoryReportTypeCode, "FULL_STATUS_REPORT or TRADE_ITEM_STATUS_REPORT"));
                }

                foreach (var filter in reportTarget.logisticsInventoryRequestLocation)
                {
                    var id = filter.inventoryLocation.gln ?? filter.inventoryLocation.additionalPartyIdentification?.FirstOrDefault()?.Value;
                    var place = this.m_placeRepository.Find(o => o.Identifiers.Any(i => i.Value == id)).FirstOrDefault();
                    if (place == null)
                    {
                        Guid uuid = Guid.Empty;
                        if (Guid.TryParse(id, out uuid))
                        {
                            place = this.m_placeRepository.Get(uuid, Guid.Empty);
                        }

                        if (place == null)
                        {
                            this.m_tracer.TraceError($"Place {filter.inventoryLocation.gln} not found");
                            throw new FileNotFoundException(this.m_localizationService.GetString("error.messaging.gs1.placeNotFound",
                                new
                                {
                                    param = filter.inventoryLocation.gln
                                }));
                        }
                    }
                    filterPlaces.Add(new LogisticsReportFilter()
                    {
                        FromDate = reportFrom,
                        ToDate = reportTo,
                        Place = place
                    });
                }
            }

            // No query = get all
            if (!filterPlaces.Any())
            {
                filterPlaces = this.m_placeRepository.Find(o => o.ClassConceptKey == EntityClassKeys.ServiceDeliveryLocation).ToList()
                    .Select(o => new LogisticsReportFilter()
                    {
                        Place = o
                    }).ToList();
            }

            // Create the inventory report
            report.logisticsInventoryReportInventoryLocation = filterPlaces.AsParallel().Select(o => this.CreatePlaceInventoryReport(o.Place, o.FromDate, o.ToDate)).ToArray();

            retVal.logisticsInventoryReport = new LogisticsInventoryReportType[] { report };
            return retVal;

        }

        private LogisticsInventoryReportInventoryLocationType CreatePlaceInventoryReport(Place place, DateTime? reportFrom, DateTime? reportTo)
        {
            using (AuthenticationContext.EnterSystemContext())
            {
                var locationStockReport = new LogisticsInventoryReportInventoryLocationType();
                locationStockReport.inventoryLocation = this.m_gs1Util.CreateLocation(place);
                locationStockReport.tradeItemInventoryStatus = this.m_stockService.GetStockContainers(place.Key.Value).SelectMany(container =>
                {
                    // What are the relationships of held entities
                    return this.m_stockService.GetContainerContents(container.Key.Value, reportTo).ToList().SelectMany(rel =>
                    {
                        var lotHeld = this.m_manufMaterialRepository.Get(rel.MatierialKey);
                        var retVal = new List<TradeItemInventoryStatusType>()
                        {
                            this.m_gs1Util.CreateTradeItemStatus(container, lotHeld, "ON_HAND", rel.Quantity)
                        };

                        // Get the ledger entries 
                        var ledgerEntryGroups = this.m_stockService.GetLedgerEntries(container.Key.Value, lotHeld.Key.Value, reportFrom, reportTo).GroupBy(o => o.ReasonKey);
                        foreach (var lg in ledgerEntryGroups)
                        {
                            var wastageReason = this.m_conceptRepository.GetConceptReferenceTerm(lg.Key, Gs1Constants.Gs1StockStatusCodeSystem);
                            if (wastageReason == null)
                            {
                                this.m_tracer.TraceWarning("Could not translate wastage reason '{0}' to GS1 code", lg.Key);
                                continue;
                            }

                            retVal.Add(this.m_gs1Util.CreateTradeItemStatus(container, lotHeld, wastageReason.Mnemonic, lg.Sum(o => o.Quantity)));
                        }

                        return retVal;
                    });
                }).ToArray();
                return locationStockReport;
            }
        }

        /// <summary>
        /// Issues the order response message which will mark the requested order as underway
        /// </summary>
        [Demand(PermissionPolicyIdentifiers.LoginAsService)]
        public void IssueOrderResponse(OrderResponseMessageType orderResponse)
        {
            // TODO: Validate the standard header

            Bundle orderTransaction = new Bundle();

            // Loop
            foreach (var resp in orderResponse.orderResponse)
            {
                // Find the original order which this despatch advice is fulfilling
                Act orderRequestAct = this.m_gs1Util.GetOrder(resp.originalOrder, ActMoodKeys.Request);
                if (orderRequestAct == null)
                {
                    this.m_tracer.TraceError("Could not find originalOrder");
                    throw new KeyNotFoundException(this.m_localizationService.GetString("error.messaging.gs1.originalOrder"));
                }

                // Update the supplier if it exists
                Place sourceLocation = this.m_gs1Util.GetLocation(resp.seller);
                if (sourceLocation != null && !orderRequestAct.Participations.Any(o => o.ParticipationRoleKey == ActParticipationKeys.Distributor))
                {
                    // Add participation
                    orderRequestAct.Participations.Add(new ActParticipation()
                    {
                        ActKey = orderRequestAct.Key,
                        PlayerEntityKey = sourceLocation.Key,
                        ParticipationRoleKey = ActParticipationKeys.Distributor
                    });
                }
                else if (resp.seller != null && sourceLocation == null)
                {
                    this.m_tracer.TraceError($"Could not find seller id with {resp.seller?.additionalPartyIdentification?.FirstOrDefault()?.Value ?? resp.seller.gln}");
                    throw new KeyNotFoundException(this.m_localizationService.GetString("error.messaging.gs1.seller", new
                    {
                        param = resp.seller?.additionalPartyIdentification?.FirstOrDefault()?.Value ?? resp.seller.gln
                    }));
                }

                var oidService = ApplicationServiceContext.Current.GetService<IIdentityDomainRepositoryService>();
                var gln = oidService.Get("GLN");
                var issuingAuthority = oidService.Find(o => o.Oid == $"{gln.Oid}.{resp.orderResponseIdentification.contentOwner.gln}").FirstOrDefault();
                if (issuingAuthority == null)
                {
                    issuingAuthority = oidService.Get(this.m_configuration.DefaultContentOwnerAssigningAuthority);
                }

                if (issuingAuthority == null)
                {
                    this.m_tracer.TraceError("Could not find default issuing authority for advice identification. Please configure a valid OID");
                    throw new KeyNotFoundException(this.m_localizationService.GetString("error.messaging.gs1.issuingAuthority"));
                }
                orderRequestAct.Identifiers.Add(new ActIdentifier(issuingAuthority, resp.orderResponseIdentification.entityIdentification));

                // If the original order request is not comlete, then complete it
                var existingTag = orderRequestAct.Tags.FirstOrDefault(o => o.TagKey == "orderStatus");
                if (existingTag == null)
                {
                    existingTag = new ActTag("orderStatus", "");
                    orderRequestAct.Tags.Add(existingTag);
                }

                // Accepted or not
                if (resp.responseStatusCode?.Value == "ACCEPTED")
                {
                    existingTag.Value = "accepted";
                }
                else if (resp.responseStatusCode?.Value == "REJECTED")
                {
                    existingTag.Value = "rejected";
                }

                orderTransaction.Add(orderRequestAct);
            }

            // insert transaction
            try
            {
                ApplicationServiceContext.Current.GetService<IRepositoryService<Bundle>>().Insert(orderTransaction);
            }
            catch (Exception e)
            {
                this.m_tracer.TraceError("Error issuing despatch advice: {0}", e);
                throw new Exception(this.m_localizationService.GetString("error.messaging.gs1.errorIssuing", new
                {
                    param = e.Message
                }), e);
            }
        }
    }
}