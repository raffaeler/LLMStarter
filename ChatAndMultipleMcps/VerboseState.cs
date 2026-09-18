namespace ChatAndMultipleMcps;

internal sealed class VerboseState
{
    private volatile bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
