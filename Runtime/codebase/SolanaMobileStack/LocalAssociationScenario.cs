using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using Solana.Unity.SDK;
using UnityEngine;
using Random = UnityEngine.Random;
using WebSocket = NativeWebSocket.WebSocket;
using WebSocketState = NativeWebSocket.WebSocketState;

// ReSharper disable once CheckNamespace

public class LocalAssociationScenario
{
    private const string TAG = "[LocalAssoc]";

    private readonly TimeSpan _clientTimeoutMs;
    private readonly MobileWalletAdapterSession _session;
    private readonly int _port;
    private readonly IWebSocket _webSocket;
    private AndroidJavaObject _nativeLocalAssociationScenario;
    private TaskCompletionSource<Response<object>> _startAssociationTaskCompletionSource;

    private bool _didConnect;
    private bool _closed;
    private bool _handledEncryptedMessage;
    private MobileWalletAdapterClient _client;
    private readonly AndroidJavaObject _currentActivity;
    private Queue<Action<IAdapterOperations>> _actions;

    public LocalAssociationScenario(int clientTimeoutMs = 9000)
    {
        var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        _currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        _clientTimeoutMs = TimeSpan.FromSeconds(clientTimeoutMs);
        _port = Random.Range(WebSocketsTransportContract.WebsocketsLocalPortMin, WebSocketsTransportContract.WebsocketsLocalPortMax + 1);
        _session = new MobileWalletAdapterSession();
        var webSocketUri = WebSocketsTransportContract.WebsocketsLocalScheme + "://" + WebSocketsTransportContract.WebsocketsLocalHost + ":" + _port + WebSocketsTransportContract.WebsocketsLocalPath;
        _webSocket = WebSocket.Create(webSocketUri, WebSocketsTransportContract.WebsocketsProtocol);

        Debug.Log($"{TAG} Constructor | port={_port} wsUri={webSocketUri} timeout={clientTimeoutMs}ms associationToken={_session.AssociationToken}");

        _webSocket.OnOpen += () =>
        {
            if(_didConnect)return;
            _didConnect = true;
            Debug.Log($"{TAG} OnOpen | connected port={_port}");
            var helloReq = _session.CreateHelloReq();
            Debug.Log($"{TAG} OnOpen | sending HELLO_REQ len={helloReq.Length}");
            _webSocket.Send(helloReq);
            ListenKeyExchange();
        };
        _webSocket.OnClose += (e) =>
        {
            if (!_didConnect || _closed) return;
            _webSocket.Connect(awaitConnection: false);
        };
        _webSocket.OnError += (e) =>
        {
            Debug.Log($"{TAG} OnError | error={e} port={_port}");
        };
        _webSocket.OnMessage += ReceivePublicKeyHandler;
    }


    public Task<Response<object>> StartAndExecute(List<Action<IAdapterOperations>> actions)
    {
        if (actions == null || actions.Count == 0)
            throw new ArgumentException("Actions must be non-null and non-empty");

        Debug.Log($"{TAG} StartAndExecute | ENTRY action_count={actions.Count} port={_port}");

        _actions = new Queue<Action<IAdapterOperations>>(actions);
        var intent = LocalAssociationIntentCreator.CreateAssociationIntent(
            _session.AssociationToken,
            _port);

        Debug.Log($"{TAG} StartAndExecute | launching intent associationToken={_session.AssociationToken} port={_port}");

        _currentActivity.Call("startActivityForResult", intent, 0);
        _currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(TryConnectWs));
        _startAssociationTaskCompletionSource = new TaskCompletionSource<Response<object>>();
        return _startAssociationTaskCompletionSource.Task;
    }

    private async void TryConnectWs()
    {
        Debug.Log($"{TAG} TryConnectWs | START timeout={_clientTimeoutMs.TotalSeconds}s port={_port}");
        var timeout = _clientTimeoutMs;
        while (_webSocket.State != WebSocketState.Open && !_didConnect && timeout.TotalSeconds > 0)
        {
            await _webSocket.Connect(awaitConnection: false);
            var timeDelta = TimeSpan.FromMilliseconds(500);
            timeout -= timeDelta;
            await Task.Delay(timeDelta);
        }
        if (_webSocket.State != WebSocketState.Open)
        {
            Debug.Log($"{TAG} TryConnectWs | TIMEOUT wsState={_webSocket.State} didConnect={_didConnect} port={_port}");
        }
        else
        {
            Debug.Log($"{TAG} TryConnectWs | CONNECTED wsState={_webSocket.State} port={_port}");
        }
    }

    private async void ListenKeyExchange()
    {
        Debug.Log($"{TAG} ListenKeyExchange | START waiting for encrypted message");
        while (!_handledEncryptedMessage)
        {
            var timeDelta = TimeSpan.FromMilliseconds(300);
            await Task.Delay(timeDelta);
        }
        Debug.Log($"{TAG} ListenKeyExchange | DONE encrypted session established");
    }

    private void HandleEncryptedSessionPayload(byte[] e)
    {
        if (!_didConnect)
        {
            Debug.Log($"{TAG} HandleEncryptedSessionPayload | ERROR not connected, terminating");
            throw new InvalidOperationException("Invalid message received; terminating session");
        }

        var de = _session.DecryptSessionPayload(e);
        var message = System.Text.Encoding.UTF8.GetString(de);
        Debug.Log($"{TAG} HandleEncryptedSessionPayload | decrypted_len={message.Length} message={message}");

        _client.Receive(message);
        var receivedResponse = JsonConvert.DeserializeObject<Response<object>>(message);;
        Debug.Log($"{TAG} HandleEncryptedSessionPayload | parsed wasSuccessful={receivedResponse.WasSuccessful} failed={receivedResponse.Failed} error={receivedResponse.Error?.Message ?? "null"} error_code={receivedResponse.Error?.Code.ToString() ?? "null"}");

        ExecuteNextAction(receivedResponse);
    }


    private void ReceivePublicKeyHandler(byte[] m)
    {
        try
        {
            Debug.Log($"{TAG} ReceivePublicKeyHandler | ENTRY payload_len={m.Length}");
            _session.GenerateSessionEcdhSecret(m);
            var messageSender = new MobileWalletAdapterWebSocket(_webSocket, _session);
            _client = new MobileWalletAdapterClient(messageSender);
            _webSocket.OnMessage -= ReceivePublicKeyHandler;
            _webSocket.OnMessage += HandleEncryptedSessionPayload;
            Debug.Log($"{TAG} ReceivePublicKeyHandler | ECDH complete, client created, encrypted channel ready");

            // Executing the first action
            ExecuteNextAction();
        }
        catch (Exception e)
        {
            Debug.Log($"{TAG} ReceivePublicKeyHandler | EXCEPTION type={e.GetType().Name} msg={e.Message}");
            Console.WriteLine(e);
        }
    }

    private void ExecuteNextAction(Response<object> response = null)
    {
        Debug.Log($"{TAG} ExecuteNextAction | remaining_actions={_actions.Count} has_response={response != null} response_failed={response?.Failed ?? false} response_error={response?.Error?.Message ?? "null"}");

        if (_actions.Count == 0 || response is { Failed: true })
        {
            Debug.Log($"{TAG} ExecuteNextAction | CLOSING reason={(_actions.Count == 0 ? "no_more_actions" : "response_failed")}");
            CloseAssociation(response);
            return;
        }
        var action = _actions.Dequeue();
        Debug.Log($"{TAG} ExecuteNextAction | DEQUEUED remaining_after={_actions.Count}");
        action.Invoke(_client);
    }

    private async void CloseAssociation(Response<object> response)
    {
        Debug.Log($"{TAG} CloseAssociation | START was_successful={response?.WasSuccessful ?? true} error={response?.Error?.Message ?? "null"} port={_port}");
        _closed = true;
        _webSocket.OnMessage -= HandleEncryptedSessionPayload;
        _handledEncryptedMessage = true;
        await _webSocket.Close();
        Debug.Log($"{TAG} CloseAssociation | DONE ws_closed port={_port}");
        _startAssociationTaskCompletionSource.SetResult(response);
    }
}
