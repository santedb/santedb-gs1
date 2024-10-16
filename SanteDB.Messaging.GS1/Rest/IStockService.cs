﻿/*
 * Portions Copyright 2019-2024, Fyfe Software Inc. and the SanteSuite Contributors (See NOTICE)
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
using SanteDB.Messaging.GS1.Model;

namespace SanteDB.Messaging.GS1.Rest
{
    /// <summary>
    /// GS1 Business Messaging Standard (BMS) 3.3
    /// </summary>
    /// <remarks>
    /// This contract represents an implementation of the GS1 Business Messaging Standard (BMS) 3.3 over REST.
    /// </remarks>
    [ServiceContract(Name = "GS1BMS")]
    [ServiceProduces("application/xml")]
    [ServiceConsumes("application/xml")]
    public interface IStockService
    {
        /// <summary>
        /// Request for issuance of an inventory report
        /// </summary>
        /// <remarks>
        /// This method requests the SanteDB server to compose an inventory report according to the parameters supplied.
        /// </remarks>
        /// <param name="parameters">The logistics inventory filters to use</param>
        [RestInvoke(Method = "POST", UriTemplate = "/inventoryReport")]
        LogisticsInventoryReportMessageType IssueInventoryReportRequest(LogisticsInventoryReportRequestMessageType parameters);

        /// <summary>
        /// Represents a request to issue despatch advice
        /// </summary>
        /// <remarks>
        /// This method is used to indicate that a previously issued order has been despatched from the sender (i.e. is on the way).
        /// </remarks>
        [RestInvoke(Method = "POST", UriTemplate = "/despatchAdvice")]
        void IssueDespatchAdvice(DespatchAdviceMessageType advice);

        /// <summary>
        /// Issues order response
        /// </summary>
        /// <remarks>
        /// This message is sent whenever the order receiver wishes to issue a response (confirmation, acceptance, etc.) of a previously sent order.
        /// </remarks>
        [RestInvoke(Method = "POST", UriTemplate = "/orderResponse")]
        void IssueOrderResponse(OrderResponseMessageType orderResponse);
    }
}