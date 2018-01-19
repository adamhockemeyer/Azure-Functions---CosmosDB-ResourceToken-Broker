using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace CosmosDBResourceTokenBroker.Shared
{
    /// <summary>
    /// Returned to the client to be able to use the token call CosmosDB resources.
    /// </summary>
    public class PermissionToken //: TypedDocument<PermissionToken>
    {

        /// <summary>
        /// Token to call the protected CosmosDB resource with.
        /// </summary>
        [JsonProperty(PropertyName = "token")]
        public string Token { get; set; }
        /// <summary>
        /// Epoch time in seconds the token expires.
        /// </summary>
        [JsonProperty(PropertyName = "expires")]
        public long Expires { get; set; }
        /// <summary>
        /// User that was created in CosmosDB who has permissions to resources.
        /// </summary>
        [JsonProperty(PropertyName = "userId")]
        public string UserId { get; set; }
       
    }
}
