namespace FCatFlasher.Models
{
    public class BoxSession
    {
        private readonly List<string> _serialNumbers = new();

        public int BoxNumber { get; set; } = 1;
        public int CurrentCount { get; private set; }

        public void AddBoard(string serial)
        {
            _serialNumbers.Add(serial);
            CurrentCount++;
        }

        public bool IsFull(int boxQuantity)
        {
            return boxQuantity > 0 && CurrentCount >= boxQuantity;
        }

        public BoxReport CreateCurrentReport(string firmwareName)
        {
            string serialRanges = FormatSerialRanges();

            return new BoxReport
            {
                BoxNumber = BoxNumber,
                Quantity = CurrentCount,
                FirmwareName = firmwareName,
                SerialNumbersText = serialRanges,
                FirstSerial = serialRanges,
                LastSerial = serialRanges,
                CreatedAt = DateTime.Now
            };
        }

        public BoxReport CreateReport(int quantity, string firmwareName)
        {
            string serialRanges = FormatSerialRanges();

            return new BoxReport
            {
                BoxNumber = BoxNumber,
                Quantity = quantity,
                FirmwareName = firmwareName,
                SerialNumbersText = serialRanges,
                FirstSerial = serialRanges,
                LastSerial = serialRanges,
                CreatedAt = DateTime.Now
            };
        }

        public void NextBox()
        {
            BoxNumber++;
            CurrentCount = 0;
            _serialNumbers.Clear();
        }

        private string FormatSerialRanges()
        {
            var serials = _serialNumbers
                .Where(serial => !string.IsNullOrWhiteSpace(serial))
                .Select(serial => ParsedSerial.Parse(serial.Trim()))
                .OrderBy(serial => serial.Prefix)
                .ThenBy(serial => serial.HasNumber ? 0 : 1)
                .ThenBy(serial => serial.Number)
                .ThenBy(serial => serial.Original)
                .ToList();

            var ranges = new List<string>();
            int index = 0;

            while (index < serials.Count)
            {
                ParsedSerial start = serials[index];

                if (!start.HasNumber)
                {
                    ranges.Add(start.Original);
                    index++;
                    continue;
                }

                ParsedSerial end = start;
                index++;

                while (index < serials.Count &&
                       serials[index].HasNumber &&
                       serials[index].Prefix == start.Prefix &&
                       serials[index].Number == end.Number + 1)
                {
                    end = serials[index];
                    index++;
                }

                ranges.Add(start.Original == end.Original
                    ? start.Original
                    : $"{start.Original}-{end.Original}");
            }

            return string.Join(", ", ranges);
        }

        private sealed record ParsedSerial(
            string Original,
            string Prefix,
            long Number,
            bool HasNumber)
        {
            public static ParsedSerial Parse(string serial)
            {
                int index = serial.Length - 1;

                while (index >= 0 && char.IsDigit(serial[index]))
                    index--;

                if (index == serial.Length - 1)
                    return new ParsedSerial(serial, serial, 0, false);

                string prefix = serial[..(index + 1)];
                string numberText = serial[(index + 1)..];

                return long.TryParse(numberText, out long number)
                    ? new ParsedSerial(serial, prefix, number, true)
                    : new ParsedSerial(serial, serial, 0, false);
            }
        }
    }
}
