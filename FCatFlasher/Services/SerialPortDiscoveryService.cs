using System.IO.Ports;

namespace FCatFlasher.Services
{
    /// <summary>
    /// Повертає доступні COM-порти операційної системи.
    /// </summary>
    internal sealed class SerialPortDiscoveryService
    {
        public string[] GetPortNames()
        {
            return SerialPort.GetPortNames();
        }
    }
}
