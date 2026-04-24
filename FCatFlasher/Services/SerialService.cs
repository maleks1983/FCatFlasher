using System;
using System.Collections.Generic;
using System.Text;
using System.IO.Ports;

namespace FCatFlasher.Services
{
    internal class SerialService
    {

        private SerialPort? _port;

        public event Action<string>? DataReceived;

        public void Open(string portName, int baudRate = 115200)
        {
            _port = new SerialPort(portName, baudRate);

            _port.DataReceived += (s, e) =>
            {
                try
                {
                    string data = _port.ReadExisting();
                    DataReceived?.Invoke(data);
                }
                catch
                {
                    // щоб не падало
                }
            };

            _port.Open();
        }

        public void Close()
        {
            if (_port != null && _port.IsOpen)
                _port.Close();
        }
    }
}
