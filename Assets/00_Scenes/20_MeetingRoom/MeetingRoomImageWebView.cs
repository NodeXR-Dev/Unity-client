using System;
using UnityEngine;
using UnityEngine.UI;
using Vuplex.WebView;

[DisallowMultipleComponent]
public class MeetingRoomImageWebView : MonoBehaviour
{
    private const string LoadingKey = "meetingroom-image-webview";
    private const string ImageLoadingMessage = "meetingroom-image-loading";
    private const string ImageLoadedMessage = "meetingroom-image-loaded";
    private const string ImageFailedMessage = "meetingroom-image-failed";

    [SerializeField] private string initialImageUrl = "";
    [SerializeField] private Vector2Int webViewSize = new Vector2Int(1280, 720);
    [SerializeField] private float widthInMeters = 1.6f;
    [SerializeField] private bool placeInFrontOfMainCamera = true;
    [SerializeField] private float distanceFromCamera = 2f;
    [SerializeField] private float verticalOffset = 0f;
    [SerializeField] private Vector3 fallbackWorldPosition = new Vector3(5.85f, 1.4f, 2f);
    [SerializeField] private Vector3 fallbackWorldEulerAngles = Vector3.zero;
    [SerializeField] private bool enableRemoteDebugging = false;

    private CanvasWebViewPrefab webViewPrefab;
    private string currentImageUrl = "";

    public CanvasWebViewPrefab WebViewPrefab => webViewPrefab;

    private async void Start()
    {
        try
        {
            MeetingRoomCommonUI.ShowLoading("이미지 뷰어 준비 중...", LoadingKey);
            Web.SetUserAgent(false);
            CreateWebView();

            await webViewPrefab.WaitUntilInitialized();
            webViewPrefab.WebView.LoadFailed += OnWebViewLoadFailed;
            webViewPrefab.WebView.MessageEmitted += OnWebViewMessageEmitted;

            SetImageUrl(initialImageUrl);
        }
        catch (Exception exception)
        {
            MeetingRoomCommonUI.HideLoading(LoadingKey);
            MeetingRoomCommonUI.Alert("이미지 뷰어 오류", "WebView를 초기화하지 못했습니다.");
            Debug.LogError($"[MeetingRoomImageWebView] Failed to initialize WebView: {exception}");
        }
    }

    private void OnDestroy()
    {
        if (webViewPrefab == null || webViewPrefab.WebView == null)
        {
            return;
        }

        webViewPrefab.WebView.LoadFailed -= OnWebViewLoadFailed;
        webViewPrefab.WebView.MessageEmitted -= OnWebViewMessageEmitted;
    }

    public void SetImageUrl(string imageUrl)
    {
        currentImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? "" : imageUrl.Trim();

        if (webViewPrefab == null || webViewPrefab.WebView == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(currentImageUrl))
        {
            MeetingRoomCommonUI.HideLoading(LoadingKey);
        }
        else
        {
            MeetingRoomCommonUI.ShowLoading("이미지 로딩 중...", LoadingKey);
        }

        webViewPrefab.WebView.LoadHtml(BuildImageViewerHtml(currentImageUrl));
    }

    public void Reload()
    {
        SetImageUrl(currentImageUrl);
    }

    private void CreateWebView()
    {
        var canvasObject = new GameObject(
            "MeetingRoom Image WebView Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var canvasRectTransform = (RectTransform)canvasObject.transform;
        canvasRectTransform.sizeDelta = new Vector2(webViewSize.x, webViewSize.y);

        var metersPerPixel = widthInMeters / Mathf.Max(1, webViewSize.x);
        canvasObject.transform.localScale = new Vector3(metersPerPixel, metersPerPixel, metersPerPixel);
        PositionCanvas(canvasObject.transform);

        webViewPrefab = CanvasWebViewPrefab.Instantiate();
        webViewPrefab.transform.SetParent(canvasObject.transform, false);
        webViewPrefab.NativeOnScreenKeyboardEnabled = true;
        webViewPrefab.RemoteDebuggingEnabled = enableRemoteDebugging;
        webViewPrefab.Resolution = 1f;

        var webViewRectTransform = (RectTransform)webViewPrefab.transform;
        webViewRectTransform.anchorMin = Vector2.zero;
        webViewRectTransform.anchorMax = Vector2.one;
        webViewRectTransform.offsetMin = Vector2.zero;
        webViewRectTransform.offsetMax = Vector2.zero;
        webViewRectTransform.localScale = Vector3.one;
    }

    private void PositionCanvas(Transform canvasTransform)
    {
        var mainCamera = Camera.main;
        if (placeInFrontOfMainCamera && mainCamera != null)
        {
            var cameraTransform = mainCamera.transform;
            var targetPosition = cameraTransform.position
                + cameraTransform.forward * distanceFromCamera
                + Vector3.up * verticalOffset;

            canvasTransform.position = targetPosition;
            canvasTransform.rotation = Quaternion.LookRotation(targetPosition - cameraTransform.position, Vector3.up);
            return;
        }

        canvasTransform.position = fallbackWorldPosition;
        canvasTransform.rotation = Quaternion.Euler(fallbackWorldEulerAngles);
    }

    private void OnWebViewLoadFailed(object sender, LoadFailedEventArgs eventArgs)
    {
        MeetingRoomCommonUI.HideLoading(LoadingKey);
        MeetingRoomCommonUI.Alert("이미지 뷰어 로딩 실패", "WebView 페이지를 불러오지 못했습니다.");
        Debug.LogWarning($"[MeetingRoomImageWebView] WebView load failed: {eventArgs.Url}");
    }

    private void OnWebViewMessageEmitted(object sender, Vuplex.WebView.EventArgs<string> eventArgs)
    {
        switch (eventArgs.Value)
        {
            case ImageLoadingMessage:
                MeetingRoomCommonUI.ShowLoading("이미지 로딩 중...", LoadingKey);
                break;
            case ImageLoadedMessage:
                MeetingRoomCommonUI.HideLoading(LoadingKey);
                MeetingRoomCommonUI.Notice("이미지 로딩 완료");
                break;
            case ImageFailedMessage:
                MeetingRoomCommonUI.HideLoading(LoadingKey);
                MeetingRoomCommonUI.Alert("이미지 로딩 실패", "이미지를 불러오지 못했습니다. URL과 네트워크 상태를 확인하세요.");
                break;
        }
    }

    private static string BuildImageViewerHtml(string imageUrl)
    {
        var encodedImageUrl = HtmlAttributeEncode(imageUrl);
        return @"<!doctype html>
<html>
<head>
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<style>
html,
body {
    margin: 0;
    width: 100%;
    height: 100%;
    background: #15171c;
    color: #f4f5f7;
    font-family: Arial, Helvetica, sans-serif;
    overflow: hidden;
}
* {
    box-sizing: border-box;
}
.toolbar {
    display: flex;
    gap: 10px;
    align-items: center;
    height: 64px;
    padding: 12px;
    background: #20242b;
    border-bottom: 1px solid #323743;
}
input {
    flex: 1;
    height: 40px;
    min-width: 0;
    padding: 0 12px;
    border: 1px solid #555d6c;
    border-radius: 4px;
    background: #0f1115;
    color: #fff;
    font-size: 16px;
    outline: none;
}
button {
    height: 40px;
    padding: 0 18px;
    border: 0;
    border-radius: 4px;
    background: #2f7cf6;
    color: #fff;
    font-size: 15px;
    font-weight: 700;
}
.stage {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    height: calc(100% - 64px);
    padding: 18px;
}
img {
    display: none;
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
}
.message {
    position: absolute;
    left: 24px;
    right: 24px;
    top: 50%;
    transform: translateY(-50%);
    color: #aeb6c4;
    font-size: 18px;
    text-align: center;
}
</style>
</head>
<body>
<div class=""toolbar"">
    <input id=""url"" type=""url"" placeholder=""https://example.com/image.png"" value=""" + encodedImageUrl + @""">
    <button id=""load"">Load</button>
</div>
<div class=""stage"">
    <img id=""preview"" alt="""">
    <div id=""message"" class=""message"">Enter an image URL.</div>
</div>
<script>
const input = document.getElementById('url');
const button = document.getElementById('load');
const image = document.getElementById('preview');
const message = document.getElementById('message');

function postUnity(text) {
    if (window.vuplex) {
        window.vuplex.postMessage(text);
        return;
    }

    const postWhenReady = () => {
        window.removeEventListener('vuplexready', postWhenReady);
        if (window.vuplex) {
            window.vuplex.postMessage(text);
        }
    };
    window.addEventListener('vuplexready', postWhenReady);
}

function showMessage(text) {
    image.style.display = 'none';
    message.style.display = 'block';
    message.textContent = text;
}

function loadImage() {
    const url = input.value.trim();
    if (!url) {
        showMessage('Enter an image URL.');
        return;
    }

    message.style.display = 'block';
    message.textContent = 'Loading image...';
    image.style.display = 'none';
    image.dataset.requestUrl = url;
    postUnity('" + ImageLoadingMessage + @"');
    image.onload = () => {
        if (image.dataset.requestUrl !== url) {
            return;
        }

        message.style.display = 'none';
        image.style.display = 'block';
        postUnity('" + ImageLoadedMessage + @"');
    };
    image.onerror = () => {
        if (image.dataset.requestUrl !== url) {
            return;
        }

        showMessage('Image failed to load.');
        postUnity('" + ImageFailedMessage + @"');
    };
    image.src = url;
}

button.addEventListener('click', loadImage);
input.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
        loadImage();
    }
});
loadImage();
</script>
</body>
</html>";
    }

    private static string HtmlAttributeEncode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
