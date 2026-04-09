using System;

namespace SerialApp.Desktop.Services;

public sealed class SerialDataChunkEventArgs : EventArgs
{
    public SerialDataChunkEventArgs(byte[] buffer, DateTime occurredAt)
    {
        Buffer = buffer;
        OccurredAt = occurredAt;
    }

    public byte[] Buffer { get; }

    public DateTime OccurredAt { get; }
}