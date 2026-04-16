using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Solana.Unity.Rpc.Models;
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

        protected override async Task<Account> _Login(string password = null)
        {
            if (_walletOptions.keepConnectionAlive)
            {
                string pk = PlayerPrefs.GetString("pk", null);
                if (!pk.IsNullOrEmpty()) return new Account(string.Empty, new PublicKey(pk));
            }

            AuthorizationResult authorization = null;
            var localAssociationScenario = new LocalAssociationScenario();
            var cluster = RPCNameMap[(int)RpcCluster];
            var useSiws = !string.IsNullOrEmpty(_walletOptions.siwsDomain);

            SignedResult siwsFallbackSig = null;
            string siwsMessageText = null;

            var actions = new List<Action<IAdapterOperations>>();

            if (useSiws)
            {
                var chain = ClusterToChain.TryGetValue(cluster, out var c) ? c : "solana:mainnet";
                var signInPayload = new JsonRequest.SignInPayload
                {
                    Domain = _walletOptions.siwsDomain,
                    Statement = _walletOptions.siwsStatement,
                    Uri = _walletOptions.identityUri
                };

                // Action 1: Authorize with sign_in_payload (MWA 2.0 SIWS)
                actions.Add(async client =>
                {
                    authorization = await client.Authorize(
                        new Uri(_walletOptions.identityUri),
                        new Uri(_walletOptions.iconUri, UriKind.Relative),
                        _walletOptions.name,
                        chain, null, null, null, signInPayload);
                });

                // Action 2: Fallback — if wallet didn't return sign_in_result,
                // construct CAIP-122 SIWS message and sign via sign_messages
                actions.Add(async client =>
                {
                    if (authorization?.SignInResult != null) return;
                    if (authorization?.PublicKey == null) return;

                    var pubkeyBase58 = new PublicKey(authorization.PublicKey).Key;
                    siwsMessageText = $"{_walletOptions.siwsDomain} wants you to sign in with your Solana account:\n{pubkeyBase58}";
                    if (!string.IsNullOrEmpty(_walletOptions.siwsStatement))
                        siwsMessageText += $"\n\n{_walletOptions.siwsStatement}";
                    siwsMessageText += $"\n\nURI: {_walletOptions.identityUri}";
                    var messageBytes = System.Text.Encoding.UTF8.GetBytes(siwsMessageText);

                    siwsFallbackSig = await client.SignMessages(
                        messages: new List<byte[]> { messageBytes },
                        addresses: new List<byte[]> { authorization.PublicKey }
                    );
                });
            }
            else
            {
                actions.Add(async client =>
                {
                    authorization = await client.Authorize(
                        new Uri(_walletOptions.identityUri),
                        new Uri(_walletOptions.iconUri, UriKind.Relative),
                        _walletOptions.name, cluster);
                });
            }

            var result = await localAssociationScenario.StartAndExecute(actions);

            if (!result.WasSuccessful)
            {
                Debug.LogError(result.Error.Message);
                throw new Exception(result.Error.Message);
            }

            _authToken = authorization.AuthToken;
            LastSignInResult = authorization.SignInResult;
            var publicKey = new PublicKey(authorization.PublicKey);

            // If wallet returned sign_in_result natively (e.g. Backpack), it's already set.
            // If not, build it from the fallback sign_messages result (e.g. Phantom, Jupiter).
            if (useSiws && LastSignInResult == null && siwsFallbackSig?.SignedPayloadsBytes?.Count > 0)
            {
                var signedBytes = siwsFallbackSig.SignedPayloadsBytes[0];
                var sigBytes = new byte[64];
                var msgBytes = new byte[signedBytes.Length - 64];
                Array.Copy(signedBytes, 0, msgBytes, 0, msgBytes.Length);
                Array.Copy(signedBytes, signedBytes.Length - 64, sigBytes, 0, 64);

                LastSignInResult = new SignInResult
                {
                    Address = authorization.Accounts[0].Address,
                    SignedMessage = Convert.ToBase64String(msgBytes),
                    Signature = Convert.ToBase64String(sigBytes),
                    SignatureType = "ed25519"
                };
            }

            if (useSiws && LastSignInResult == null)
            {
                throw new Exception("SIWS authorization failed: wallet did not return sign_in_result and fallback signing failed");
            }

            if (_walletOptions.keepConnectionAlive)
            {
                PlayerPrefs.SetString("pk", publicKey.ToString());
            }
            return new Account(string.Empty, publicKey);
        }

        protected override async Task<Transaction> _SignTransaction(Transaction transaction)
        {
            var result = await _SignAllTransactions(new Transaction[] { transaction });
            return result[0];
        }


        protected override async Task<Transaction[]> _SignAllTransactions(Transaction[] transactions)
        {

            var cluster = RPCNameMap[(int)RpcCluster];
            SignedResult res = null;
            var localAssociationScenario = new LocalAssociationScenario();
            AuthorizationResult authorization = null;
            var result = await localAssociationScenario.StartAndExecute(
                new List<Action<IAdapterOperations>>
                {
                    async client =>
                    {
                        if (_authToken.IsNullOrEmpty())
                        {
                            authorization = await client.Authorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, cluster);
                        }
                        else
                        {
                            authorization = await client.Reauthorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, _authToken);   
                        }
                    },
                    async client =>
                    {
                        res = await client.SignTransactions(transactions.Select(transaction => transaction.Serialize()).ToList());
                    }
                }
            );
            if (!result.WasSuccessful)
            {
                Debug.LogError(result.Error.Message);
                throw new Exception(result.Error.Message);
            }
            _authToken = authorization.AuthToken;
            return res.SignedPayloads.Select(transaction => Transaction.Deserialize(transaction)).ToArray();
        }


        public override void Logout()
        {
            base.Logout();
            PlayerPrefs.DeleteKey("pk");
            PlayerPrefs.Save();
        }

        public override async Task<byte[]> SignMessage(byte[] message)
        {
            SignedResult signedMessages = null;
            var localAssociationScenario = new LocalAssociationScenario();
            AuthorizationResult authorization = null;
            var cluster = RPCNameMap[(int)RpcCluster];
            var result = await localAssociationScenario.StartAndExecute(
                new List<Action<IAdapterOperations>>
                {
                    async client =>
                    {
                        if (_authToken.IsNullOrEmpty())
                        {
                            authorization = await client.Authorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, cluster);
                        }
                        else
                        {
                            authorization = await client.Reauthorize(
                                new Uri(_walletOptions.identityUri),
                                new Uri(_walletOptions.iconUri, UriKind.Relative),
                                _walletOptions.name, _authToken);   
                        }
                    },
                    async client =>
                    {
                        signedMessages = await client.SignMessages(
                            messages: new List<byte[]> { message },
                            addresses: new List<byte[]> { Account.PublicKey.KeyBytes }
                        );
                    }
                }
            );
            if (!result.WasSuccessful)
            {
                Debug.LogError(result.Error.Message);
                throw new Exception(result.Error.Message);
            }
            _authToken = authorization.AuthToken;
            return signedMessages.SignedPayloadsBytes[0];
        }

        protected override Task<Account> _CreateAccount(string mnemonic = null, string password = null)
        {
            throw new NotImplementedException("Can't create a new account in phantom wallet");
        }
    }
}
