using System;
using System.Buffers;
using System.IO.Ports;
using System.Threading.Tasks;
using SerialApp.Desktop.Models;

namespace SerialApp.Desktop.Services;

public sealed class SerialPortSession : IDisposable
{
    private SerialPort? _serialPort;

    public event EventHandler<SerialDataChunkEventArgs>? DataReceived;

    public event EventHandler<string>? ErrorOccurred;

    public event EventHandler<string>? Disconnected;

    public bool IsOpen => _serialPort?.IsOpen == true;

    public Task OpenAsync(SerialPortSettings settings)
    {
        if (IsOpen)
        {
            throw new InvalidOperationException("串口已经处于打开状态。");
        }

        var serialPort = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            ReadTimeout = 500,
            WriteTimeout = 500,
        };

        serialPort.DataReceived += SerialPort_DataReceived;
        serialPort.ErrorReceived += SerialPort_ErrorReceived;
        serialPort.Open();
        _serialPort = serialPort;

        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        var portToClose = _serialPort;

        if (portToClose is null)
        {
            return Task.CompletedTask;
        }

        _serialPort = null;

        try
        {
            portToClose.DataReceived -= SerialPort_DataReceived;
            portToClose.ErrorReceived -= SerialPort_ErrorReceived;

            if (portToClose.IsOpen)
            {
                portToClose.Close();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"关闭串口异常: {ex}");
        }
        finally
        {
            try
            {
                portToClose.Dispose();
            }
            catch
            {
                // Ignored
            }
        }

        return Task.CompletedTask;
    }

    public Task SendAsync(byte[] data)
    {
        if (!IsOpen || _serialPort is null)
        {
            throw new InvalidOperationException("串口尚未打开。");
        }

        _serialPort.Write(data, 0, data.Length);
        return Task.CompletedTask;
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var serialPort = _serialPort;

        if (serialPort is null)
        {
            return;
        }

        try
        {
            if (!serialPort.IsOpen)
            {
                return;
            }

            var bytesToRead = serialPort.BytesToRead;

            if (bytesToRead <= 0)
            {
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(bytesToRead);
            var bytesRead = 0;

            try
            {
                bytesRead = serialPort.Read(buffer, 0, bytesToRead);

                if (bytesRead <= 0)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }

                var handler = DataReceived;

                if (handler is null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }

                handler.Invoke(this, new SerialDataChunkEventArgs(buffer, bytesRead, DateTime.Now));
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            _ = HandleAbruptDisconnectionAsync(ex.Message);
        }
    }

    private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        ErrorOccurred?.Invoke(this, $"串口错误: {e.EventType}");

        if (e.EventType == SerialError.RXOver || e.EventType == SerialError.Frame)
        {
            // Usually not fatal.
        }
        else
        {
            _ = HandleAbruptDisconnectionAsync($"串口产生严重错误: {e.EventType}");
        }
    }

    private async Task HandleAbruptDisconnectionAsync(string reason)
    {
        await CloseAsync();
        Disconnected?.Invoke(this, reason);
    }

    public void Dispose()
    {
        _ = CloseAsync();
    }
}