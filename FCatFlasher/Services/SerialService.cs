using System.IO.Ports;
using System.Text;

namespace FCatFlasher.Services
{
    internal class SerialService
    {
        private const int OpenSettleDelayMs = 800;
        private SerialPort? _port;
        private readonly StringBuilder _buffer = new();

        public bool IsOpen => _port?.IsOpen == true;
        public event Action<string>? DataReceived;

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

        public void Send(string command)
        {
            if (_port?.IsOpen == true)
            {
                _port.WriteLine(command);   // автоматично додає NewLine
            }
        }

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
