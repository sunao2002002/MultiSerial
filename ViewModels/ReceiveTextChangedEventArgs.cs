using System;

namespace SerialApp.Desktop.ViewModels;

public sealed class ReceiveTextChangedEventArgs : EventArgs
{
    public ReceiveTextChangedEventArgs(string text, bool replaceAll)
    {
        Text = text;
        ReplaceAll = replaceAll;
    }

    public string Text { get; }

    public bool ReplaceAll { get; }
}