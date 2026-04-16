using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Solana.Unity.SDK;
using UnityEngine;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
[Preserve]
public class MobileWalletAdapterClient: JsonRpc20Client, IAdapterOperations, IMessageReceiver
{
    private const string TAG = "[MWAClient]";

    private int _mNextMessageId = 1;

    public MobileWalletAdapterClient(IMessageSender messageSender) : base(messageSender)
    {
    }

    // ─── AUTHORIZE (MWA 1.x — cluster) ─────────────────────────────────

    [Preserve]
    public Task<AuthorizationResult> Authorize(Uri identityUri, Uri iconUri, string identityName, string cluster)
    {
        Debug.Log($"{TAG} Authorize | ENTRY identityUri={identityUri} iconUri={iconUri} identityName={identityName} cluster={cluster}");

        var request = PrepareAuthRequest(
            identityUri,
            iconUri,
            identityName,
            cluster,
            "authorize");

        var json = JsonConvert.SerializeObject(request);
        Debug.Log($"{TAG} Authorize | REQUEST json={json}");

        return SendRequest<AuthorizationResult>(request, "Authorize");
    }

    // ─── AUTHORIZE (MWA 2.0 — chain + features + SIWS) ─────────────────

    [Preserve]
    public Task<AuthorizationResult> Authorize(
        Uri identityUri, Uri iconUri, string identityName,
        string chain, string[] features, string[] addresses,
        string authToken, JsonRequest.SignInPayload signInPayload)
    {
        Debug.Log($"{TAG} Authorize2 | ENTRY identityUri={identityUri} iconUri={iconUri} identityName={identityName} chain={chain} features=[{(features != null ? string.Join(",", features) : "null")}] addresses=[{(addresses != null ? string.Join(",", addresses) : "null")}] authToken={authToken ?? "null"} authToken_len={authToken?.Length ?? 0} signInPayload_null={signInPayload == null}");

        if (identityUri != null && !identityUri.IsAbsoluteUri)
        {
            throw new ArgumentException("If non-null, identityUri must be an absolute, hierarchical Uri");
        }
        if (iconUri != null && iconUri.IsAbsoluteUri)
        {
            throw new ArgumentException("If non-null, iconRelativeUri must be a relative Uri");
        }

        var request = new JsonRequest
        {
            JsonRpc = "2.0",
            Method = "authorize",
            Params = new JsonRequest.JsonRequestParams
            {
                Identity = new JsonRequest.JsonRequestIdentity
                {
                    Uri = identityUri,
                    Icon = iconUri,
                    Name = identityName
                },
                Chain = chain,
                Features = features?.ToList(),
                Addresses = addresses?.ToList(),
                AuthToken = authToken,
                SignInPayload = signInPayload
            },
            Id = NextMessageId()
        };

        var json = JsonConvert.SerializeObject(request);
        Debug.Log($"{TAG} Authorize2 | REQUEST json={json}");

        return SendRequest<AuthorizationResult>(request, "Authorize2");
    }

    // ─── REAUTHORIZE ────────────────────────────────────────────────────

    public Task<AuthorizationResult> Reauthorize(Uri identityUri, Uri iconUri, string identityName, string authToken)
    {
        var tokenPrefix = authToken != null && authToken.Length > 20 ? authToken[..20] : authToken ?? "null";
        Debug.Log($"{TAG} Reauthorize | ENTRY identityUri={identityUri} iconUri={iconUri} identityName={identityName} authToken_len={authToken?.Length ?? 0} authToken_prefix={tokenPrefix}");

        var request = PrepareAuthRequest(
            identityUri,
            iconUri,
            identityName,
            null,
            "reauthorize");

        request.Params.AuthToken = authToken;

        var json = JsonConvert.SerializeObject(request);
        Debug.Log($"{TAG} Reauthorize | REQUEST json={json}");

        return SendRequest<AuthorizationResult>(request, "Reauthorize");
    }

    // ─── SIGN TRANSACTIONS ──────────────────────────────────────────────

    public Task<SignedResult> SignTransactions(IEnumerable<byte[]> transactions)
    {
        var payloads = transactions.Select(Convert.ToBase64String).ToList();
        Debug.Log($"{TAG} SignTransactions | ENTRY payload_count={payloads.Count} payloads=[{string.Join(",", payloads)}]");

        var request = new JsonRequest
        {
            JsonRpc = "2.0",
            Method = "sign_transactions",
            Params = new JsonRequest.JsonRequestParams
            {
                Payloads = payloads
            },
            Id = NextMessageId()
        };

        var json = JsonConvert.SerializeObject(request);
        Debug.Log($"{TAG} SignTransactions | REQUEST json={json}");

        return SendRequest<SignedResult>(request, "SignTransactions");
    }

    // ─── SIGN MESSAGES ──────────────────────────────────────────────────

    public Task<SignedResult> SignMessages(IEnumerable<byte[]> messages, IEnumerable<byte[]> addresses)
    {
        var msgList = messages.ToList();
        var addrList = addresses.ToList();
        var msgPayloads = msgList.Select(Convert.ToBase64String).ToList();
        var addrPayloads = addrList.Select(Convert.ToBase64String).ToList();
        var msgSizes = string.Join(",", msgList.Select(m => m.Length));
        var addrSizes = string.Join(",", addrList.Select(a => a.Length));
        Debug.Log($"{TAG} SignMessages | ENTRY message_count={msgPayloads.Count} msg_byte_sizes=[{msgSizes}] address_count={addrPayloads.Count} addr_byte_sizes=[{addrSizes}]");

        var request = new JsonRequest
        {
            JsonRpc = "2.0",
            Method = "sign_messages",
            Params = new JsonRequest.JsonRequestParams
            {
                Payloads = msgPayloads,
                Addresses = addrPayloads
            },
            Id = NextMessageId()
        };

        var json = JsonConvert.SerializeObject(request);
        Debug.Log($"{TAG} SignMessages | REQUEST id={request.Id} json_len={json.Length} json={json}");

        return SendRequest<SignedResult>(request, "SignMessages");
    }

    // ─── SIGN AND SEND TRANSACTIONS (MWA 2.0) ──────────────────────────

    public Task<SignAndSendResult> SignAndSendTransactions(IEnumerable<byte[]> transactions, JsonRequest.SignAndSendOptions options)
    {
        var payloads = transactions.Select(Convert.ToBase64String).ToList();
        Debug.Log($"{TAG} SignAndSendTransactions | ENTRY payload_count={payloads.Count} payloads=[{string.Join(",", payloads)}] options_null={options == null} commitment={options?.Commitment ?? "null"} skip_preflight={options?.SkipPreflight?.ToString() ?? "null"} min_context_slot={options?.MinContextSlot?.ToString() ?? "null"} max_retries={options?.MaxRetries?.ToString() ?? "null"} wait_for_commitment={options?.WaitForCommitmentToSendNextTransaction?.ToString() ?? "null"}");

        var request = new JsonRequest
        {
            JsonRpc = "2.0",
            Method = "sign_and_send_transactions",
            Params = new JsonRequest.JsonRequestParams
            {
                Payloads = payloads,
                Options = options
            },
            Id = NextMessageId()
        };

        var json = JsonConvert.SerializeObject(request);
        Debug.Log($"{TAG} SignAndSendTransactions | REQUEST json={json}");

        return SendRequest<SignAndSendResult>(request, "SignAndSendTransactions");
    }

    // ─── GET CAPABILITIES (MWA 2.0 — non-privileged) ───────────────────

    public Task<CapabilitiesResult> GetCapabilities()
    {
        Debug.Log($"{TAG} GetCapabilities | ENTRY (non-privileged, no params)");

        var request = new JsonRequest
        {
            JsonRpc = "2.0",
            Method = "get_capabilities",
            Params = new JsonRequest.JsonRequestParams(),
            Id = NextMessageId()
        };

        var json = JsonConvert.SerializeObject(request);
        Debug.Log($"{TAG} GetCapabilities | REQUEST json={json}");

        return SendRequest<CapabilitiesResult>(request, "GetCapabilities");
    }

    // ─── HELPERS ────────────────────────────────────────────────────────

    private JsonRequest PrepareAuthRequest(Uri uriIdentity, Uri icon, string name, string cluster, string method)
    {
        if (uriIdentity != null && !uriIdentity.IsAbsoluteUri)
        {
            throw new ArgumentException("If non-null, identityUri must be an absolute, hierarchical Uri");
        }
        if (icon != null && icon.IsAbsoluteUri)
        {
            throw new ArgumentException("If non-null, iconRelativeUri must be a relative Uri");
        }
        var request = new JsonRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = new JsonRequest.JsonRequestParams
            {
                Identity = new JsonRequest.JsonRequestIdentity
                {
                    Uri = uriIdentity,
                    Icon = icon,
                    Name = name
                },
                Cluster = cluster
            },
            Id = NextMessageId()
        };
        return request;
    }

    private int NextMessageId()
    {
        return _mNextMessageId++;
    }

}
