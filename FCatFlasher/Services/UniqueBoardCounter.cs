namespace FCatFlasher.Services
{
/// <summary>
/// Рахує унікальні серійники плат у межах поточного запуску режиму.
/// </summary>
    internal class UniqueBoardCounter
    {
        private const string MissingSerialPlaceholder = "D111111";
        private readonly HashSet<string> _serials = new(StringComparer.OrdinalIgnoreCase);
        private int _missingSerialCount;

        public int Count => _serials.Count + _missingSerialCount;

/// <summary>
/// Очищає набір уже врахованих серійників.
/// </summary>
        public void Reset()
        {
            _serials.Clear();
            _missingSerialCount = 0;
        }

        public bool Register(string serial, string mcuId)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return false;

            if (serial.Trim().Equals(MissingSerialPlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                _missingSerialCount++;
                return true;
            }

            string key = $"{serial.Trim()}\u001F{mcuId.Trim()}";
            return _serials.Add(key);
        }
    }
}
