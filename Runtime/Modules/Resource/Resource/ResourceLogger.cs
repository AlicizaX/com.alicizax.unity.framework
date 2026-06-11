using System;

namespace AlicizaX.Resource.Runtime
{
    public class ResourceLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            AlicizaX.Log.Info(message);
        }

        public void Warning(string message)
        {
            AlicizaX.Log.Warning(message);
        }

        public void Error(string message)
        {
            AlicizaX.Log.Error(message);
        }

        public void Exception(Exception exception)
        {
            AlicizaX.Log.Exception(exception);
        }
    }
}
