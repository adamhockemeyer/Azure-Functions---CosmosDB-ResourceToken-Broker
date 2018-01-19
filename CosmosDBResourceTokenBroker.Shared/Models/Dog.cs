using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace CosmosDBResourceTokenBroker.Shared.Models
{
    public class Dog : TypedDocument<Dog>
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("breed")]
        public string Breed { get; set; }

    }
}
