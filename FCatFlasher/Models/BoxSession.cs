namespace FCatFlasher.Models
{
/// <summary>
/// Накопичує серійники поточної виробничої коробки.
/// </summary>
    public class BoxSession
    {
        private readonly List<string> _serialNumbers = new();

        public int BoxNumber { get; set; } = 1;
        public int CurrentCount { get; private set; }
        public bool HasBoards => CurrentCount > 0;

/// <summary>
/// Додає серійник прошитої плати до поточної коробки.
/// </summary>
        public void AddBoard(string serial)
        {
            _serialNumbers.Add(serial);
            CurrentCount++;
        }

/// <summary>
/// Перевіряє, чи досягнута задана місткість коробки.
/// </summary>
        public bool IsFull(int boxQuantity)
        {
            return boxQuantity > 0 && CurrentCount >= boxQuantity;
        }

/// <summary>
/// Створює проміжний звіт для поточної коробки без переходу до наступної.
/// </summary>
        public BoxReport CreateCurrentReport(string firmwareName)
        {
            SerialRangeData rangeData = BuildSerialRangeData();

            return new BoxReport
            {
                BoxNumber = BoxNumber,
                Quantity = CurrentCount,
                FirmwareName = firmwareName,
                SerialNumbersText = rangeData.RangeText,
                FirstSerial = rangeData.FirstSerial,
                LastSerial = rangeData.LastSerial,
                CreatedAt = DateTime.Now
            };
        }

/// <summary>
/// Створює фінальний звіт коробки.
/// </summary>
        public BoxReport CreateReport(int quantity, string firmwareName)
        {
            SerialRangeData rangeData = BuildSerialRangeData();

            return new BoxReport
            {
                BoxNumber = BoxNumber,
                Quantity = quantity,
                FirmwareName = firmwareName,
                SerialNumbersText = rangeData.RangeText,
                FirstSerial = rangeData.FirstSerial,
                LastSerial = rangeData.LastSerial,
                CreatedAt = DateTime.Now
            };
        }

/// <summary>
/// Очищає поточні серійники і збільшує номер коробки.
/// </summary>
        public void NextBox()
        {
            BoxNumber++;
            CurrentCount = 0;
            _serialNumbers.Clear();
        }

/// <summary>
/// Будує компактні текстові діапазони серійників для етикетки або звіту.
/// </summary>
        private SerialRangeData BuildSerialRangeData()
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

            return new SerialRangeData(
                string.Join(", ", ranges),
                serials.Count == 0 ? string.Empty : serials[0].Original,
                serials.Count == 0 ? string.Empty : serials[^1].Original);
        }

        private sealed record SerialRangeData(
            string RangeText,
            string FirstSerial,
            string LastSerial);

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
