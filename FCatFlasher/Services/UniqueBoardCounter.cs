namespace FCatFlasher.Services
{
/// <summary>
/// Рахує унікальні серійники плат у межах поточного запуску режиму.
/// </summary>
    internal class UniqueBoardCounter
    {
        private readonly HashSet<string> _serials = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _serials.Count;

/// <summary>
/// Очищає набір уже врахованих серійників.
/// </summary>
        public void Reset()
        {
            _serials.Clear();
        }

        public bool Register(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return false;

            return _serials.Add(serial.Trim());
        }
    }
}
