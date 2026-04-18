using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Solana.Unity.SDK
{
    /// <summary>
    /// Default MWA auth cache using Unity PlayerPrefs.
    /// Persists auth tokens and public keys across app restarts.
    /// For production apps requiring security, implement IMwaAuthCache
    /// with Android Keystore or EncryptedSharedPreferences instead.
    /// </summary>
    public class PlayerPrefsAuthCache : IMwaAuthCache
    {
        private const string AuthTokenKey = "solana_mwa_auth_token";
        private const string PublicKeyKey = "solana_mwa_public_key";

        public void SaveToken(string publicKey, string authToken)
        {
            if (string.IsNullOrEmpty(authToken)) return;
            PlayerPrefs.SetString(AuthTokenKey, authToken);
            PlayerPrefs.Save();
            Debug.Log($"[MwaAuthCache] SaveToken | pubkey={publicKey} token_len={authToken.Length}");
        }

        public string LoadToken(string publicKey)
        {
            var token = PlayerPrefs.GetString(AuthTokenKey, null);
            Debug.Log($"[MwaAuthCache] LoadToken | pubkey={publicKey} found={!string.IsNullOrEmpty(token)} token_len={token?.Length ?? 0}");
            return token;
        }

        public void SavePublicKey(string publicKey)
        {
            if (string.IsNullOrEmpty(publicKey)) return;
            PlayerPrefs.SetString(PublicKeyKey, publicKey);
            PlayerPrefs.Save();
            Debug.Log($"[MwaAuthCache] SavePublicKey | pubkey={publicKey}");
        }

        public string LoadPublicKey()
        {
            var pk = PlayerPrefs.GetString(PublicKeyKey, null);
            Debug.Log($"[MwaAuthCache] LoadPublicKey | found={!string.IsNullOrEmpty(pk)} pubkey={pk}");
            return pk;
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(AuthTokenKey);
            PlayerPrefs.DeleteKey(PublicKeyKey);
            PlayerPrefs.Save();
            Debug.Log("[MwaAuthCache] Clear | all cached auth data removed");
        }
    }
}
