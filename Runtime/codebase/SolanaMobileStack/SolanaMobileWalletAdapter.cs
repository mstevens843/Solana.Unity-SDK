using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Merkator.BitCoin;
using Solana.Unity.Rpc.Core.Http;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Wallet;
using UnityEngine;
using WebSocketSharp;

// ReSharper disable once CheckNamespace

namespace Solana.Unity.SDK
{

    [Serializable]
    public class SolanaMobileWalletAdapterOptions
    {
        public string identityUri = "https://solana.unity-sdk.gg/";
        public string iconUri = "/favicon.ico";
        public string name = "Solana.Unity-SDK";
        public bool keepConnectionAlive = true;
        public string siwsDomain = null;
        public string siwsStatement = null;
    }


    [Obsolete("Use SolanaWalletAdapter class instead, which is the cross platform wrapper.")]
    public class SolanaMobileWalletAdapter : WalletBase
    {
        private const string TAG = "[MWAAdapter]";

        private static readonly Dictionary<string, string> ClusterToChain = new()
        {
            { "mainnet-beta", "solana:mainnet" },
            { "devnet", "solana:devnet" },
            { "testnet", "solana:testnet" },
        };

        private readonly SolanaMobileWalletAdapterOptions _walletOptions;

        private Transaction _currentTransaction;

        private TaskCompletionSource<Account> _loginTaskCompletionSource;
        private TaskCompletionSource<Transaction> _signedTransactionTaskCompletionSource;
        private readonly WalletBase _internalWallet;
        private string _authToken;

        public SignInResult LastSignInResult { get; private set; }

        public SolanaMobileWalletAdapter(
            SolanaMobileWalletAdapterOptions solanaWalletOptions,
            RpcCluster rpcCluster = RpcCluster.DevNet,
            string customRpcUri = null,
            string customStreamingRpcUri = null,
            bool autoConnectOnStartup = false) : base(rpcCluster, customRpcUri, customStreamingRpcUri, autoConnectOnStartup
        )
        {
            _walletOptions = solanaWalletOptions;
            if (Application.platform != RuntimePlatform.Android)
            {
                throw new Exception("SolanaMobileWalletAdapter can only be used on Android");
            }
        }

        // ─── LOGIN ──────────────────────────────────────────────────────────

        protected override async Task<Account> _Login(string password = null)
        {
            Debug.Log($"{TAG} _Login | ENTRY keepConnectionAlive={_walletOptions.keepConnectionAlive}");

            if (_walletOptions.keepConnectionAlive)
            {
                string pk = PlayerPrefs.GetString("pk", null);
                Debug.Log($"{TAG} _Login | cached_pk={pk ?? "null"} pk_empty={pk.IsNullOrEmpty()}");
                if (!pk.IsNullOrEmpty()) return new Account(string.Empty, new PublicKey(pk));
            }

            AuthorizationResult authorization = null;
            var localAssociationScenario = new LocalAssociationScenario();
            var cluster = RPCNameMap[(int)RpcCluster];
            var useSiws = !string.IsNullOrEmpty(_walletOptions.siwsDomain);

            Debug.Log($"{TAG} _Login | scenario_created cluster={cluster} rpcCluster={RpcCluster} useSiws={useSiws} siwsDomain={_walletOptions.siwsDomain ?? "null"} siwsStatement={_walletOptions.siwsStatement ?? "null"}");

            var result = await localAssociationScenario.StartAndExecute(
                new List<Action<IAdapterOperations>>
                {
                    async client =>
                    {
                        if (useSiws)
                        {
                            var chain = ClusterToChain.TryGetValue(cluster, out var c) ? c : "solana:mainnet";
                            var signInPayload = new JsonRequest.SignInPayload
                            {
                                Domain = _walletOptions.siwsDomain,
                                Statement = _walletOptions.siwsStatement,
                                Uri = _walletOptions.identityUri
                            };

                            Debug.Log($"{TAG} _Login | SIWS_PATH calling Authorize2 chain={chain} signInPayload_domain={signInPayload.Domain} signInPayload_statement={signInPayload.Statement} signInPayload_uri={signInPayload.Uri}");
                            authorization = await client.Authorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name,
                                chain,
                                null,
                                null,
                                null,
                                signInPayload);
                            Debug.Log($"{TAG} _Login | Authorize2 DONE authToken={authorization?.AuthToken ?? "null"} authToken_len={authorization?.AuthToken?.Length ?? 0} pubkey_null={authorization?.PublicKey == null} accounts_count={authorization?.Accounts?.Count ?? 0} accountLabel={authorization?.AccountLabel ?? "null"} walletUriBase={authorization?.WalletUriBase?.ToString() ?? "null"} signInResult_null={authorization?.SignInResult == null} signInResult_address={authorization?.SignInResult?.Address ?? "null"} signInResult_sig={authorization?.SignInResult?.Signature ?? "null"} signInResult_sig_type={authorization?.SignInResult?.SignatureType ?? "null"}");
                        }
                        else
                        {
                            Debug.Log($"{TAG} _Login | LEGACY_PATH calling Authorize cluster={cluster} identityUri={_walletOptions.identityUri} iconUri={_walletOptions.iconUri} name={_walletOptions.name}");
                            authorization = await client.Authorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, cluster);
                            Debug.Log($"{TAG} _Login | Authorize DONE authToken={authorization?.AuthToken ?? "null"} authToken_len={authorization?.AuthToken?.Length ?? 0} pubkey_null={authorization?.PublicKey == null} accounts_count={authorization?.Accounts?.Count ?? 0} accountLabel={authorization?.AccountLabel ?? "null"} walletUriBase={authorization?.WalletUriBase?.ToString() ?? "null"} signInResult_null={authorization?.SignInResult == null}");
                        }
                    }
                }
            );

            Debug.Log($"{TAG} _Login | scenario_result wasSuccessful={result.WasSuccessful} error={result.Error?.Message ?? "null"} error_code={result.Error?.Code.ToString() ?? "null"}");

            if (!result.WasSuccessful)
            {
                Debug.LogError($"{TAG} _Login | RESULT=FAIL error={result.Error.Message}");
                throw new Exception(result.Error.Message);
            }

            _authToken = authorization.AuthToken;
            LastSignInResult = authorization.SignInResult;
            var publicKey = new PublicKey(authorization.PublicKey);

            if (useSiws && LastSignInResult == null)
            {
                Debug.LogError($"{TAG} _Login | SIWS_REJECTED wallet did not return sign_in_result — SIWS was requested but wallet ignored it. Rejecting non-SIWS connection.");
                throw new Exception("SIWS authorization failed: wallet did not return sign_in_result. The wallet may not support Sign In With Solana.");
            }

            Debug.Log($"{TAG} _Login | RESULT=SUCCESS pubkey={publicKey} authToken={_authToken} authToken_len={_authToken?.Length ?? 0} usedSiws={useSiws} hasSignInResult={LastSignInResult != null}");

            if (_walletOptions.keepConnectionAlive)
            {
                PlayerPrefs.SetString("pk", publicKey.ToString());
                Debug.Log($"{TAG} _Login | cached pubkey={publicKey}");
            }
            return new Account(string.Empty, publicKey);
        }

        // ─── SIGN TRANSACTION ───────────────────────────────────────────────

        protected override async Task<Transaction> _SignTransaction(Transaction transaction)
        {
            Debug.Log($"{TAG} _SignTransaction | ENTRY delegating to _SignAllTransactions");
            var result = await _SignAllTransactions(new Transaction[] { transaction });
            Debug.Log($"{TAG} _SignTransaction | RESULT signed_tx_null={result?[0] == null}");
            return result[0];
        }

        // ─── SIGN ALL TRANSACTIONS ─────────────────────────────────────────

        protected override async Task<Transaction[]> _SignAllTransactions(Transaction[] transactions)
        {
            Debug.Log($"{TAG} _SignAllTransactions | ENTRY tx_count={transactions.Length} authToken={_authToken ?? "null"} authToken_len={_authToken?.Length ?? 0}");

            var cluster = RPCNameMap[(int)RpcCluster];
            SignedResult res = null;
            var localAssociationScenario = new LocalAssociationScenario();
            AuthorizationResult authorization = null;

            Debug.Log($"{TAG} _SignAllTransactions | scenario_created cluster={cluster} authToken_empty={_authToken.IsNullOrEmpty()}");

            var result = await localAssociationScenario.StartAndExecute(
                new List<Action<IAdapterOperations>>
                {
                    async client =>
                    {
                        Debug.Log($"{TAG} _SignAllTransactions | ACTION_1 auth/reauth authToken_empty={_authToken.IsNullOrEmpty()}");
                        if (_authToken.IsNullOrEmpty())
                        {
                            Debug.Log($"{TAG} _SignAllTransactions | calling Authorize cluster={cluster}");
                            authorization = await client.Authorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, cluster);
                            Debug.Log($"{TAG} _SignAllTransactions | Authorize DONE authToken={authorization?.AuthToken ?? "null"} accounts_count={authorization?.Accounts?.Count ?? 0}");
                        }
                        else
                        {
                            Debug.Log($"{TAG} _SignAllTransactions | calling Reauthorize authToken={_authToken}");
                            authorization = await client.Reauthorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, _authToken);
                            Debug.Log($"{TAG} _SignAllTransactions | Reauthorize DONE authToken={authorization?.AuthToken ?? "null"}");
                        }
                    },
                    async client =>
                    {
                        var serializedTxs = transactions.Select(transaction => transaction.Serialize()).ToList();
                        Debug.Log($"{TAG} _SignAllTransactions | ACTION_2 SignTransactions tx_count={serializedTxs.Count} serialized_sizes=[{string.Join(",", serializedTxs.Select(t => t.Length))}]");
                        res = await client.SignTransactions(serializedTxs);
                        Debug.Log($"{TAG} _SignAllTransactions | SignTransactions DONE signed_count={res?.SignedPayloads?.Count ?? 0} signed_payload_sizes=[{string.Join(",", res?.SignedPayloads?.Select(p => p.Length) ?? new List<int>())}]");
                    }
                }
            );

            Debug.Log($"{TAG} _SignAllTransactions | scenario_result wasSuccessful={result.WasSuccessful} error={result.Error?.Message ?? "null"}");

            if (!result.WasSuccessful)
            {
                Debug.LogError($"{TAG} _SignAllTransactions | RESULT=FAIL error={result.Error.Message}");
                throw new Exception(result.Error.Message);
            }

            _authToken = authorization.AuthToken;
            Debug.Log($"{TAG} _SignAllTransactions | RESULT=SUCCESS authToken={_authToken} signed_count={res.SignedPayloads.Count}");
            return res.SignedPayloads.Select(transaction => Transaction.Deserialize(transaction)).ToArray();
        }

        // ─── SIGN AND SEND TRANSACTIONS (MWA 2.0 native) ───────────────────

        private async Task<SignAndSendResult> _SignAndSendAllTransactions(
            Transaction[] transactions, JsonRequest.SignAndSendOptions options)
        {
            Debug.Log($"{TAG} _SignAndSendAllTransactions | ENTRY tx_count={transactions.Length} options_null={options == null} commitment={options?.Commitment ?? "null"} skip_preflight={options?.SkipPreflight?.ToString() ?? "null"} min_context_slot={options?.MinContextSlot?.ToString() ?? "null"} max_retries={options?.MaxRetries?.ToString() ?? "null"} wait_for_commitment={options?.WaitForCommitmentToSendNextTransaction?.ToString() ?? "null"}");

            var cluster = RPCNameMap[(int)RpcCluster];
            SignAndSendResult res = null;
            var localAssociationScenario = new LocalAssociationScenario();
            AuthorizationResult authorization = null;

            Debug.Log($"{TAG} _SignAndSendAllTransactions | scenario_created cluster={cluster} authToken={_authToken ?? "null"} authToken_len={_authToken?.Length ?? 0} authToken_empty={_authToken.IsNullOrEmpty()}");

            var result = await localAssociationScenario.StartAndExecute(
                new List<Action<IAdapterOperations>>
                {
                    async client =>
                    {
                        Debug.Log($"{TAG} _SignAndSendAllTransactions | ACTION_1 auth/reauth authToken_empty={_authToken.IsNullOrEmpty()}");
                        if (_authToken.IsNullOrEmpty())
                        {
                            Debug.Log($"{TAG} _SignAndSendAllTransactions | calling Authorize cluster={cluster}");
                            authorization = await client.Authorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, cluster);
                            Debug.Log($"{TAG} _SignAndSendAllTransactions | Authorize DONE authToken={authorization?.AuthToken ?? "null"} accounts_count={authorization?.Accounts?.Count ?? 0}");
                        }
                        else
                        {
                            Debug.Log($"{TAG} _SignAndSendAllTransactions | calling Reauthorize authToken={_authToken}");
                            authorization = await client.Reauthorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, _authToken);
                            Debug.Log($"{TAG} _SignAndSendAllTransactions | Reauthorize DONE authToken={authorization?.AuthToken ?? "null"}");
                        }
                    },
                    async client =>
                    {
                        try
                        {
                            Debug.Log($"{TAG} _SignAndSendAllTransactions | ACTION_2 ENTER serializing {transactions.Length} transactions");
                            var serializedTxs = transactions.Select(tx => tx.Serialize()).ToList();
                            Debug.Log($"{TAG} _SignAndSendAllTransactions | ACTION_2 SignAndSendTransactions tx_count={serializedTxs.Count} serialized_sizes=[{string.Join(",", serializedTxs.Select(t => t.Length))}]");
                            res = await client.SignAndSendTransactions(serializedTxs, options);
                            Debug.Log($"{TAG} _SignAndSendAllTransactions | SignAndSendTransactions DONE sig_count={res?.Signatures?.Count ?? 0} signatures=[{string.Join(",", res?.Signatures ?? new List<string>())}]");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"{TAG} _SignAndSendAllTransactions | ACTION_2 EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
                        }
                    }
                }
            );

            Debug.Log($"{TAG} _SignAndSendAllTransactions | scenario_result wasSuccessful={result.WasSuccessful} error={result.Error?.Message ?? "null"}");

            if (!result.WasSuccessful)
            {
                Debug.LogError($"{TAG} _SignAndSendAllTransactions | RESULT=FAIL error={result.Error.Message}");
                throw new Exception(result.Error.Message);
            }

            _authToken = authorization.AuthToken;
            Debug.Log($"{TAG} _SignAndSendAllTransactions | RESULT=SUCCESS authToken={_authToken} sig_count={res.Signatures.Count} signatures=[{string.Join(",", res.Signatures)}]");
            return res;
        }

        // ─── SIGN AND SEND TRANSACTION (override WalletBase — native MWA) ──

        public override async Task<RequestResult<string>> SignAndSendTransaction(
            Transaction transaction,
            bool skipPreflight = false,
            Commitment commitment = Commitment.Confirmed)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | ENTRY skipPreflight={skipPreflight} commitment={commitment} blockhash={transaction.RecentBlockHash} fee_payer={transaction.FeePayer} instructions={transaction.Instructions?.Count ?? 0}");

            // PartialSign initializes the Signatures array so Serialize() works.
            transaction.PartialSign(Account);
            Debug.Log($"{TAG} SignAndSendTransaction | partial_sign done sigs={transaction.Signatures?.Count ?? 0}");

            var options = new JsonRequest.SignAndSendOptions
            {
                SkipPreflight = skipPreflight,
                Commitment = commitment.ToString().ToLower()
            };

            Debug.Log($"{TAG} SignAndSendTransaction | options commitment={options.Commitment} skip_preflight={options.SkipPreflight}");

            try
            {
                var signAndSendResult = await _SignAndSendAllTransactions(new[] { transaction }, options);

                if (signAndSendResult?.Signatures == null || signAndSendResult.Signatures.Count == 0)
                {
                    Debug.Log($"{TAG} SignAndSendTransaction | RESULT=FAIL reason=no_signatures sig_null={signAndSendResult?.Signatures == null} sig_count={signAndSendResult?.Signatures?.Count ?? 0}");
                    return new RequestResult<string>
                    {
                        WasHttpRequestSuccessful = true,
                        WasRequestSuccessfullyHandled = false,
                        Reason = "Wallet returned no signatures"
                    };
                }

                var sigBase64 = signAndSendResult.Signatures[0];
                var sigBytes = Convert.FromBase64String(sigBase64);
                var sigBase58 = Base58Encoding.Encode(sigBytes);

                Debug.Log($"{TAG} SignAndSendTransaction | RESULT=SUCCESS sig_base64={sigBase64} sig_base64_len={sigBase64.Length} sig_bytes_len={sigBytes.Length} sig_base58={sigBase58} sig_base58_len={sigBase58.Length}");

                return new RequestResult<string>
                {
                    WasHttpRequestSuccessful = true,
                    WasRequestSuccessfullyHandled = true,
                    Result = sigBase58
                };
            }
            catch (Exception ex)
            {
                Debug.Log($"{TAG} SignAndSendTransaction | RESULT=EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
                return new RequestResult<string>
                {
                    WasRequestSuccessfullyHandled = false,
                    Reason = ex.Message
                };
            }
        }

        // ─── SIGN MESSAGE ──────────────────────────────────────────────────

        public override async Task<byte[]> SignMessage(byte[] message)
        {
            Debug.Log($"{TAG} SignMessage | ENTRY message_len={message.Length} message_hex={BitConverter.ToString(message).Replace("-", "").ToLower()} authToken={_authToken ?? "null"} authToken_len={_authToken?.Length ?? 0}");

            SignedResult signedMessages = null;
            var localAssociationScenario = new LocalAssociationScenario();
            AuthorizationResult authorization = null;
            var cluster = RPCNameMap[(int)RpcCluster];

            Debug.Log($"{TAG} SignMessage | scenario_created cluster={cluster} authToken_empty={_authToken.IsNullOrEmpty()}");

            var result = await localAssociationScenario.StartAndExecute(
                new List<Action<IAdapterOperations>>
                {
                    async client =>
                    {
                        Debug.Log($"{TAG} SignMessage | ACTION_1 auth/reauth authToken_empty={_authToken.IsNullOrEmpty()}");
                        if (_authToken.IsNullOrEmpty())
                        {
                            Debug.Log($"{TAG} SignMessage | calling Authorize cluster={cluster}");
                            authorization = await client.Authorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, cluster);
                            Debug.Log($"{TAG} SignMessage | Authorize DONE authToken={authorization?.AuthToken ?? "null"} accounts_count={authorization?.Accounts?.Count ?? 0}");
                        }
                        else
                        {
                            Debug.Log($"{TAG} SignMessage | calling Reauthorize authToken={_authToken}");
                            authorization = await client.Reauthorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, _authToken);
                            Debug.Log($"{TAG} SignMessage | Reauthorize DONE authToken={authorization?.AuthToken ?? "null"}");
                        }
                    },
                    async client =>
                    {
                        Debug.Log($"{TAG} SignMessage | ACTION_2 SignMessages message_len={message.Length} pubkey={Account.PublicKey}");
                        signedMessages = await client.SignMessages(
                            messages: new List<byte[]> { message },
                            addresses: new List<byte[]> { Account.PublicKey.KeyBytes }
                        );
                        Debug.Log($"{TAG} SignMessage | SignMessages DONE signed_count={signedMessages?.SignedPayloads?.Count ?? 0} signed_payload_sizes=[{string.Join(",", signedMessages?.SignedPayloads?.Select(p => p.Length) ?? new List<int>())}]");
                    }
                }
            );

            Debug.Log($"{TAG} SignMessage | scenario_result wasSuccessful={result.WasSuccessful} error={result.Error?.Message ?? "null"}");

            if (!result.WasSuccessful)
            {
                Debug.LogError($"{TAG} SignMessage | RESULT=FAIL error={result.Error.Message}");
                throw new Exception(result.Error.Message);
            }

            _authToken = authorization.AuthToken;
            var sigBytes = signedMessages.SignedPayloadsBytes[0];
            Debug.Log($"{TAG} SignMessage | RESULT=SUCCESS authToken={_authToken} sig_bytes_len={sigBytes.Length} sig_base64={Convert.ToBase64String(sigBytes)}");
            return sigBytes;
        }

        // ─── GET CAPABILITIES (MWA 2.0 — non-privileged) ───────────────────

        public async Task<CapabilitiesResult> GetCapabilities()
        {
            Debug.Log($"{TAG} GetCapabilities | ENTRY (non-privileged, opens own session)");

            CapabilitiesResult caps = null;
            var localAssociationScenario = new LocalAssociationScenario();

            var result = await localAssociationScenario.StartAndExecute(
                new List<Action<IAdapterOperations>>
                {
                    async client =>
                    {
                        Debug.Log($"{TAG} GetCapabilities | ACTION calling get_capabilities");
                        caps = await client.GetCapabilities();
                        Debug.Log($"{TAG} GetCapabilities | RESPONSE max_txs={caps?.MaxTransactionsPerRequest} max_msgs={caps?.MaxMessagesPerRequest} versions=[{string.Join(",", caps?.SupportedTransactionVersions ?? new List<string>())}] features=[{string.Join(",", caps?.Features ?? new List<string>())}]");
                    }
                }
            );

            Debug.Log($"{TAG} GetCapabilities | scenario_result wasSuccessful={result.WasSuccessful} error={result.Error?.Message ?? "null"}");

            if (!result.WasSuccessful)
            {
                Debug.LogError($"{TAG} GetCapabilities | RESULT=FAIL error={result.Error.Message}");
                throw new Exception(result.Error.Message);
            }

            Debug.Log($"{TAG} GetCapabilities | RESULT=SUCCESS max_txs={caps.MaxTransactionsPerRequest} max_msgs={caps.MaxMessagesPerRequest} versions=[{string.Join(",", caps.SupportedTransactionVersions ?? new List<string>())}] features=[{string.Join(",", caps.Features ?? new List<string>())}]");
            return caps;
        }

        // ─── LOGOUT ────────────────────────────────────────────────────────

        public override void Logout()
        {
            Debug.Log($"{TAG} Logout | ENTRY");
            base.Logout();
            PlayerPrefs.DeleteKey("pk");
            PlayerPrefs.Save();
            Debug.Log($"{TAG} Logout | DONE pk_cleared=true");
        }

        // ─── CREATE ACCOUNT (not supported) ─────────────────────────────────

        protected override Task<Account> _CreateAccount(string mnemonic = null, string password = null)
        {
            throw new NotImplementedException("Can't create a new account in phantom wallet");
        }
    }
}
