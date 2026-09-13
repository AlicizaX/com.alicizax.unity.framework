using System;

namespace AlicizaX.Resource.Runtime
{
    public class ResourceLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            AlicizaX.Log.Info(message);
        }

        public void LogWarning(string message)
        {
            AlicizaX.Log.Warning(message);
        }

        public void LogError(string message)
        {
            AlicizaX.Log.Error(message);
        }

        public void LogException(Exception exception)
        {
            AlicizaX.Log.Exception(exception);
        }
    }
}
