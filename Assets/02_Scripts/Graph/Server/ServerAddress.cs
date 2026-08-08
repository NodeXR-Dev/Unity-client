using System;

// 서버 주소 문자열 하나로 REST/WebSocket URL 을 만든다.
//
// 기존에는 호출부마다 "http://" + host, $"ws://{host}" 처럼 스킴을 하드코딩해서,
// 사설망 주소(192.168.x.x:8000)만 쓸 수 있었다. 서로 다른 네트워크에서 붙으려면
// 터널/배포 주소(https://xxx.trycloudflare.com)가 필요한데 그때 전부 깨진다.
//
// 그래서 host 값이 스킴을 포함하면 그 스킴을 따르고, 없으면 기존대로 http/ws 를 쓴다.
//   "192.168.0.36:8000"              -> http://192.168.0.36:8000   ws://192.168.0.36:8000
//   "https://abc.trycloudflare.com"  -> https://abc.trycloudflare.com  wss://abc.trycloudflare.com
public static class ServerAddress
{
    // https 로 명시된 주소인가(= 보안 스킴을 써야 하는가).
    public static bool IsSecure(string host)
    {
        return !string.IsNullOrWhiteSpace(host) &&
               host.TrimStart().StartsWith(
                   "https://", StringComparison.OrdinalIgnoreCase);
    }

    // 스킴과 뒤따르는 슬래시를 걷어낸 순수 host[:port].
    public static string Authority(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "";

        string value = host.Trim();
        foreach (string scheme in
                 new[] { "https://", "http://", "wss://", "ws://" })
        {
            if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(scheme.Length);
                break;
            }
        }
        return value.TrimEnd('/');
    }

    // REST 기본 URL (끝에 슬래시 없음). 예: http://192.168.0.36:8000
    public static string Http(string host)
    {
        return (IsSecure(host) ? "https://" : "http://") + Authority(host);
    }

    // WebSocket 기본 URL (끝에 슬래시 없음). 예: ws://192.168.0.36:8000
    public static string Ws(string host)
    {
        return (IsSecure(host) ? "wss://" : "ws://") + Authority(host);
    }
}
