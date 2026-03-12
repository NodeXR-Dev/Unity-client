using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace NodeXR
{
    public class TextureDownloader : MonoBehaviour
    {
        public IEnumerator DownloadTexture(string url, Action<Texture2D> onOk)
        {
            if (string.IsNullOrWhiteSpace(url)) { onOk?.Invoke(null); yield break; }

            // 캐시 방지 타임스탬프 (백엔드와 중복되어도 상관없음, 확실히 하기 위함)
            string finalUrl = url.Contains("?") ? $"{url}&t={DateTime.Now.Ticks}" : $"{url}?t={DateTime.Now.Ticks}";

            using var req = UnityWebRequestTexture.GetTexture(finalUrl);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TextureDownloader] Failed: {url}");
                onOk?.Invoke(null);
                yield break;
            }

            Texture2D tex = DownloadHandlerTexture.GetContent(req);
            // [중요] 다운로드된 텍스처가 원래 요청한 URL의 결과인지 로그로 매칭 확인
            Debug.Log($"[TextureDownloader] Success: {url}");
            onOk?.Invoke(tex);
        }
    }
}