namespace FCatFlasher.Services
{
    internal class UniqueBoardCounter
    {
        private readonly HashSet<string> _serials = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _serials.Count;

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
