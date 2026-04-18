// ReSharper disable once CheckNamespace

namespace Solana.Unity.SDK
{
    /// <summary>
    /// Extensible interface for MWA authorization token caching.
    /// Implement this to provide custom storage backends (PlayerPrefs, encrypted, cloud, etc.).
    /// </summary>
    public interface IMwaAuthCache
    {
        /// <summary>Save an auth token keyed by wallet public key.</summary>
        void SaveToken(string publicKey, string authToken);

        /// <summary>Load a cached auth token for a given public key. Returns null if not found.</summary>
        string LoadToken(string publicKey);

        /// <summary>Save the last connected public key.</summary>
        void SavePublicKey(string publicKey);

        /// <summary>Load the last connected public key. Returns null if not found.</summary>
        string LoadPublicKey();

        /// <summary>Clear all cached auth data (token + public key).</summary>
        void Clear();
    }
}
