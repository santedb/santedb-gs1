/*
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
 * User: fyfej (Justin Fyfe)
 * Date: 2023-6-21
 */
using Newtonsoft.Json;
using SanteDB.Core.Configuration.Http;
using System.ComponentModel;
using System.Xml.Serialization;

namespace SanteDB.Messaging.GS1.Configuration
{
    /// <summary>
    /// AS2 Service configuration
    /// </summary>
    [XmlType(nameof(As2ServiceElement), Namespace = "http://santedb.org/configuration")]
    public class As2ServiceElement
    {
        /// <summary>
        /// AS2 service configuration
        /// </summary>
        public As2ServiceElement()
        {
        }

        /// <summary>
        /// Gets or sets the name of the client to use - otherwise the default Gs1 broker is used
        /// </summary>
        [XmlElement("client"), JsonProperty("client")]
        [DisplayName("Rest Client"), TypeConverter(typeof(ExpandableObjectConverter)), Description("When set, specifies the endpoint where broker messages should be pushed. Otherwise the system default GS1 broker is used")]
        public RestClientDescriptionConfiguration ClientConfiguration { get; set; }

        /// <summary>
        /// Use AS2 standard mime based encoding
        /// </summary>
        [XmlAttribute("useAs2MimeEncoding"), JsonProperty("useAs2MimeEncoding")]
        [DisplayName("Use AS.2 MIME"), Description("When true, instructs the service to use AS.2 mime encoded messages instead of REST messages")]
        public bool UseAS2MimeEncoding
        {
            get; set;
        }


    }
}