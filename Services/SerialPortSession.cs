using System;
using System.IO.Ports;
using System.Threading.Tasks;
using SerialApp.Desktop.Models;

namespace SerialApp.Desktop.Services;

public sealed class SerialPortSession : IDisposable
{
    private SerialPort? _serialPort;

    public event EventHandler<SerialDataChunkEventArgs>? DataReceived;

    public event EventHandler<string>? ErrorOccurred;

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
        if (_serialPort is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            _serialPort.ErrorReceived -= SerialPort_ErrorReceived;

            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        finally
        {
            _serialPort.Dispose();
            _serialPort = null;
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
        if (_serialPort is null)
        {
            return;
        }

        try
        {
            var bytesToRead = _serialPort.BytesToRead;

            if (bytesToRead <= 0)
            {
                return;
            }

            var buffer = new byte[bytesToRead];
            var bytesRead = _serialPort.Read(buffer, 0, buffer.Length);

            if (bytesRead <= 0)
            {
                return;
            }

            if (bytesRead != buffer.Length)
            {
                Array.Resize(ref buffer, bytesRead);
            }

            DataReceived?.Invoke(this, new SerialDataChunkEventArgs(buffer, DateTime.Now));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        ErrorOccurred?.Invoke(this, $"串口错误: {e.EventType}");
    }

    public void Dispose()
    {
        _ = CloseAsync();
    }
}