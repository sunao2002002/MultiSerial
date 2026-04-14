using System;

namespace SerialApp.Desktop.Services;

public sealed class SerialDataChunkEventArgs : EventArgs
{
    public SerialDataChunkEventArgs(byte[] buffer, int count, DateTime occurredAt)
    {
        Buffer = buffer;
        Count = count;
        OccurredAt = occurredAt;
    }

    public byte[] Buffer { get; }

    public int Count { get; }

    public DateTime OccurredAt { get; }
}