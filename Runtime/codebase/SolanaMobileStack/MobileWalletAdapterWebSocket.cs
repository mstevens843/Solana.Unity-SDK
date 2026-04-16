using System;
using NativeWebSocket;
using Solana.Unity.SDK;
using UnityEngine;

// ReSharper disable once CheckNamespace

public class MobileWalletAdapterWebSocket: IMessageSender
{
    private const string TAG = "[MWAWebSocket]";
    private readonly IWebSocket _webSocket;
    private readonly MobileWalletAdapterSession _session;

    public MobileWalletAdapterWebSocket(IWebSocket webSocket, MobileWalletAdapterSession session)
    {
        _webSocket = webSocket;
        _session = session;
    }

    public void Send(byte[] message)
    {
        if(message == null || message.Length == 0)
            throw new ArgumentException("Message cannot be null or empty");
        Debug.Log($"{TAG} Send | plaintext_len={message.Length} ws_state={_webSocket.State}");
        var encryptedMessage = _session.EncryptSessionPayload(message);
        Debug.Log($"{TAG} Send | encrypted_len={encryptedMessage.Length} sending");
        _webSocket.Send(encryptedMessage);
    }
}