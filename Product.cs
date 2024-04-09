#nullable enable

// using System.ComponentModel.DataAnnotations;
// using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace AzureSQL.Tables
{

    public class Product
    {
        [JsonProperty(PropertyName = "id", Required = Required.Always)]
        public int id { get; set; } = -1;
        [JsonProperty("name")]
        public string name { get; set; } = "";
        [JsonProperty("price")]
        public Decimal price { get; set; } = 0.0m;
        [JsonProperty("description")]
        public string description { get; set; } ="";
    }
}