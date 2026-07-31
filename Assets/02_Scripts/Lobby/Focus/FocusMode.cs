using Fusion;

public enum FocusMode : byte
{
    Personal = 0,
    PresenterView = 1
}

public readonly struct PresenterViewState
{
    public readonly FocusMode Mode;
    public readonly PlayerRef Presenter;
    public readonly int Revision;

    public PresenterViewState(FocusMode mode, PlayerRef presenter, int revision)
    {
        Mode = mode;
        Presenter = presenter;
        Revision = revision;
    }

    public bool IsPresenterViewActive => Mode == FocusMode.PresenterView && Presenter != PlayerRef.None;
}
