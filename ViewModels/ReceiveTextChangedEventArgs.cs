using System;

namespace SerialApp.Desktop.ViewModels;

public sealed class ReceiveTextChangedEventArgs : EventArgs
{
    public ReceiveTextChangedEventArgs(string metadataText, string payloadText, bool replaceAll)
    {
        MetadataText = metadataText;
        PayloadText = payloadText;
        ReplaceAll = replaceAll;
    }

    public string MetadataText { get; }

    public string PayloadText { get; }

    public bool ReplaceAll { get; }
}