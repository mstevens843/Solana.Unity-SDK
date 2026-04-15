using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace

namespace Solana.Unity.SDK
{

    [Preserve]
    public class JsonRpc20Client
    {
        private const string TAG = "[JsonRpc20]";

        private delegate void MessageHandler(string message);

        private event MessageHandler MessageEvent;

        private readonly IMessageSender _messageSender;

        protected JsonRpc20Client(IMessageSender messageSender)
        {
            _messageSender = messageSender;
        }

        protected Task<T> SendRequest<T>(JsonRequest jsonRequest, string methodName = "Unknown")
        {
            var message = JsonConvert.SerializeObject(jsonRequest);
            Debug.Log($"{TAG} SendRequest | method={methodName} id={jsonRequest.Id} json_len={message.Length} json={message}");
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
            _messageSender.Send(messageBytes);
            var authTaskCompletionSource = new TaskCompletionSource<T>();

            // Register the message listener
            RegisterListener(authTaskCompletionSource, methodName);
            return authTaskCompletionSource.Task;
        }

        public void Receive(string message)
        {

            MessageEvent?.Invoke(message);
        }

        /// <summary>
        /// Register a listener for the message event
        /// </summary>
        /// <param name="task"></param>
        /// <param name="methodName"></param>
        /// <typeparam name="T"></typeparam>
        private void RegisterListener<T>(TaskCompletionSource<T> task, string methodName)
        {
            var listener = new Action<string>(msg => Receiver(task, msg, methodName));
            MessageEvent += listener.Invoke;
            task.Task.ContinueWith(_ => { MessageEvent -= listener.Invoke; });
        }

        /// <summary>
        /// Wrap the receiver listener
        /// </summary>
        /// <param name="task"></param>
        /// <param name="message"></param>
        /// <param name="methodName"></param>
        /// <typeparam name="T"></typeparam>
        private static void Receiver<T>(TaskCompletionSource<T> task, string message, string methodName)
        {
            Debug.Log($"{TAG} Receiver | method={methodName} raw_response_len={message.Length} raw_response={message}");
            try
            {
                var authorizationResult = JsonConvert.DeserializeObject<Response<T>>(message);
                if (authorizationResult.Error != null)
                {
                    Debug.Log($"{TAG} Receiver | method={methodName} RESULT=ERROR code={authorizationResult.Error.Code} message={authorizationResult.Error.Message}");
                    task.SetException(new Exception(authorizationResult.Error.Message));
                }
                else
                {
                    var resultJson = JsonConvert.SerializeObject(authorizationResult.Result);
                    Debug.Log($"{TAG} Receiver | method={methodName} RESULT=SUCCESS id={authorizationResult.Id} parsed_result={resultJson}");
                    task.SetResult(authorizationResult.Result);
                }
            }
            catch (JsonException e)
            {
                Debug.Log($"{TAG} Receiver | method={methodName} RESULT=PARSE_ERROR type={e.GetType().Name} message={e.Message}");
                task.SetException(e);
            }

        }
    }
}
