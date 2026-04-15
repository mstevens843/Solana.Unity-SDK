using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace

[Preserve]
public class CapabilitiesResult
{
    [JsonProperty("max_transactions_per_request")]
    [RequiredMember]
    public int? MaxTransactionsPerRequest { get; set; }

    [JsonProperty("max_messages_per_request")]
    [RequiredMember]
    public int? MaxMessagesPerRequest { get; set; }

    [JsonProperty("supported_transaction_versions")]
    [RequiredMember]
    public List<string> SupportedTransactionVersions { get; set; }

    [JsonProperty("features")]
    [RequiredMember]
    public List<string> Features { get; set; }
}
