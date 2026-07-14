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
using System;

namespace SanteDB.Messaging.GS1
{
    /// <summary>
    /// GS1 Constants
    /// </summary>
    internal static class Gs1Constants
    {

        /// <summary>
        /// Pub/sub AS2 mime encoding
        /// </summary>
        public const string PubsubAs2MimeEncodingSettingName = "as2.useMimeEncoding";

        /// <summary>
        /// Authenticator
        /// </summary>
        public const string PubsubAuthenticatorSettingName = "$authenticator";

        /// <summary>
        /// Act type of ORDER
        /// </summary>
        public static readonly Guid ActTypeOrder = Guid.Parse("14d69b32-f6c4-4a49-a527-a74893dbcf4a");

        /// <summary>
        /// Order has been received in SanteDB
        /// </summary>
        public static readonly Guid ActTypeOrderReceipt = Guid.Parse("34b3e45f-f6c4-4a49-a527-a74893dbcf4a");

        /// <summary>
        /// Order has been despatched from SanteDB
        /// </summary>
        public static readonly Guid ActTypeOrderDespatch = Guid.Parse("32fedb45-f6c4-4a49-a527-a74893dbcf4a");

        /// <summary>
        /// Transfer event
        /// </summary>
        public static readonly Guid ActTypeTransfer = Guid.Parse("77C4002A-A0F4-43F0-9457-12DA5E11FA34");

        /// <summary>
        /// Trace source name
        /// </summary>
        public const string TraceSourceName = "SanteDB.Messaging.GS1";
    }
}