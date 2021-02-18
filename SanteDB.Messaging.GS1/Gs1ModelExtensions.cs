﻿/*
 * Portions Copyright 2019-2020, Fyfe Software Inc. and the SanteSuite Contributors (See NOTICE)
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
 * User: fyfej (Justin Fyfe)
 * Date: 2019-11-27
 */
using System;

namespace SanteDB.Messaging.GS1
{
    /// <summary>
    /// GS1 extensions to core model attributes
    /// </summary>
    public static class Gs1ModelExtensions
    {
        /// <summary>
        /// Expeted delivery date
        /// </summary>
        public static readonly Guid ExpectedDeliveryDate = Guid.Parse("6b2ae7b5-158f-46d0-9ff9-0e3fa999dcaa");
        /// <summary>
        /// Actual shipment date
        /// </summary>
        public static readonly Guid ActualShipmentDate = Guid.Parse("34073745-018b-48d1-82da-c545391d294a");
        /// <summary>
        /// Packaging unit
        /// </summary>
        public static readonly Guid PackagingUnit = Guid.Parse("4F417368-1C96-4F5E-BDAC-CA51C4BABF3B");
    }
}
