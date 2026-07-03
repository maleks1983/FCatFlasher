using System.IO.Ports;
using System.Text;

namespace FCatFlasher.Services
{
/// <summary>
/// Обгортка над SerialPort для команд стенду і line-based UART подій.
/// </summary>
    internal class SerialService
    {
        private const int OpenSettleDelayMs = 800;
        private SerialPort? _port;
        private readonly StringBuilder _buffer = new();

        public bool IsOpen => _port?.IsOpen == true;
        public event Action<string>? DataReceived;

/// <summary>
/// Відкриває COM-порт і підписується на вхідні дані.
/// </summary>
        public void Open(string portName, int baud = 115200)
        {
            Close();

            _port = new SerialPort(portName, baud)
            {
                NewLine = "\n",
                Encoding = Encoding.ASCII,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = false,
                RtsEnable = false
            };

            _port.Open();
            Thread.Sleep(OpenSettleDelayMs);
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            _port.DataReceived += OnDataReceivedRaw;
        }

/// <summary>
/// Накопичує байти з порту та емінить готові рядки.
/// </summary>
        private void OnDataReceivedRaw(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = _port!.ReadExisting();
                _buffer.Append(data);

                while (true)
                {
                    (int index, int length) = IndexOfLineEnding(_buffer);
                    if (index < 0)
                        break;

                    string line = _buffer.ToString(0, index).Trim();
                    _buffer.Remove(0, index + length);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        DataReceived?.Invoke(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Serial error: " + ex.Message);
            }
        }

/// <summary>
/// Знаходить перший CR/LF розділювач у буфері UART.
/// </summary>
        private static (int Index, int Length) IndexOfLineEnding(StringBuilder buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != '\r' && buffer[i] != '\n')
                    continue;

                int length = 1;

                if (buffer[i] == '\r' &&
                    i + 1 < buffer.Length &&
                    buffer[i + 1] == '\n')
                {
                    length = 2;
                }

                return (i, length);
            }

            return (-1, 0);
        }

/// <summary>
/// Надсилає команду стенду з завершенням рядка.
/// </summary>
        public void Send(string command)
        {
            if (_port?.IsOpen == true)
            {
                _port.WriteLine(command);   // автоматично додає NewLine
            }
        }

/// <summary>
/// Закриває COM-порт і очищає внутрішній буфер.
/// </summary>
        public void Close()
        {
            if (_port?.IsOpen == true)
            {
                _port.DataReceived -= OnDataReceivedRaw;
                _port.Close();
                _port.Dispose();
                _port = null;
            }
            _buffer.Clear();
        }
/// <summary>
/// Очищає програмний і апаратні буфери порту.
/// </summary>
        public void ClearBuffers()
        {
            if (_port == null || !_port.IsOpen)
                return;

            try
            {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
            }
            catch
            {
            }
        }
    }
}
