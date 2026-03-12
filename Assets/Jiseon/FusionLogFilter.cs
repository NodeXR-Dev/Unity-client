using UnityEngine;

public class FusionErrorSuppressor : MonoBehaviour, ILogger
{
    private static ILogger original;

    public ILogHandler logHandler { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public bool logEnabled { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public LogType filterLogType { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public bool IsLogTypeAllowed(LogType logType)
    {
        throw new System.NotImplementedException();
    }

    public void Log(LogType logType, object message)
    {
        throw new System.NotImplementedException();
    }

    public void Log(LogType logType, object message, Object context)
    {
        throw new System.NotImplementedException();
    }

    public void Log(LogType logType, string tag, object message)
    {
        throw new System.NotImplementedException();
    }

    public void Log(LogType logType, string tag, object message, Object context)
    {
        throw new System.NotImplementedException();
    }

    public void Log(object message)
    {
        throw new System.NotImplementedException();
    }

    public void Log(string tag, object message)
    {
        throw new System.NotImplementedException();
    }

    public void Log(string tag, object message, Object context)
    {
        throw new System.NotImplementedException();
    }

    public void LogError(string tag, object message)
    {
        throw new System.NotImplementedException();
    }

    public void LogError(string tag, object message, Object context)
    {
        throw new System.NotImplementedException();
    }

    public void LogException(System.Exception exception)
    {
        throw new System.NotImplementedException();
    }

    public void LogException(System.Exception exception, Object context)
    {
        throw new System.NotImplementedException();
    }

    public void LogFormat(LogType logType, string format, params object[] args)
    {
        throw new System.NotImplementedException();
    }

    public void LogFormat(LogType logType, Object context, string format, params object[] args)
    {
        throw new System.NotImplementedException();
    }

    public void LogWarning(string tag, object message)
    {
        throw new System.NotImplementedException();
    }

    public void LogWarning(string tag, object message, Object context)
    {
        throw new System.NotImplementedException();
    }

    void OnEnable()
    {
        if (original == null)
            original = Debug.unityLogger;

        Debug.unityLogger.logHandler = new CustomHandler(original.logHandler);
    }

    private class CustomHandler : ILogHandler
    {
        private ILogHandler inner;
        public CustomHandler(ILogHandler inner) { this.inner = inner; }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            string msg = string.Format(format, args);

            // 특정 Fusion 로그만 무시
            if (msg.Contains("DisconnectMessage. Code: 104") ||
                msg.Contains("Got DisconnectMessage. Code: 104"))
            {
                return;
            }

            inner.LogFormat(logType, context, format, args);
        }

        public void LogException(System.Exception exception, Object context)
        {
            inner.LogException(exception, context);
        }
    }
}
