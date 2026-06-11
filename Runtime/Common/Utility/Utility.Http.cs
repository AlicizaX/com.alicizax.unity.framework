using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AlicizaX
{
    public static partial class Utility
    {
        public static partial class Http
        {
            /// <summary>
            /// 发送 GET 请求
            /// </summary>
            public static async UniTask<string> Get(string url, float timeout = 5f)
            {
                using var request = UnityWebRequest.Get(url);
                return await SendRequest(request);
            }

            /// <summary>
            /// 发送 JSON 格式的 POST 请求（服务端需用 [FromBody] 接收）
            /// </summary>
            public static async UniTask<string> PostJson(string url, object jsonData, float timeout = 5f)
            {
                var json = Json.ToJson(jsonData);
                using var request = CreateJsonPostRequest(url, json);
                return await SendRequest(request);
            }

            /// <summary>
            /// 发送表单格式的 POST 请求（x-www-form-urlencoded）
            /// </summary>
            public static async UniTask<string> PostForm(string url, Dictionary<string, string> formData, float timeout = 5f)
            {
                using var request = UnityWebRequest.Post(url, formData);
                return await SendRequest(request);
            }

            /// <summary>
            /// 发送多部分表单的 POST 请求（multipart/form-data，支持文件上传）
            /// </summary>
            public static async UniTask<string> PostMultipart(string url, WWWForm formData, float timeout = 5f)
            {
                using var request = UnityWebRequest.Post(url, formData);
                return await SendRequest(request);
            }


            private static UnityWebRequest CreateJsonPostRequest(string url, string json)
            {
                var request = new UnityWebRequest(url, "POST");
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                return request;
            }

            private static async UniTask<string> SendRequest(UnityWebRequest request)
            {
                string url = request.url; // 提前获取请求地址

                try
                {
                    await request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        HandleRequestError(request, url);
                        return string.Empty;
                    }

                    return request.downloadHandler.text;
                }
                catch (UnityWebRequestException e)
                {
                    HandleRequestError(request, url);
                    return e.Message;
                }
            }

            private static void HandleRequestError(UnityWebRequest request, string url)
            {
                ;
                switch (request.result)
                {
                    case UnityWebRequest.Result.ConnectionError:
                        Log.Error($"无法访问地址:{url}");
                        break;
                    case UnityWebRequest.Result.ProtocolError:
                        Log.Error($"HTTP协议错误 ({request.responseCode}):{url}\n{request.error}");
                        break;
                    case UnityWebRequest.Result.DataProcessingError:
                        Log.Error($"数据处理错误:{url}\n{request.error}");
                        break;
                    default:
                        Log.Error($"未知网络错误:{url}\n{request.error}");
                        break;
                }
            }
        }
    }
}
