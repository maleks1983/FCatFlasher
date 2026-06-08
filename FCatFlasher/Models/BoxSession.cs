namespace FCatFlasher.Models
{
    public class BoxSession
    {
        public int BoxNumber { get; set; } = 1;
        public int CurrentCount { get; private set; }

        public string FirstSerial { get; private set; } = string.Empty;
        public string LastSerial { get; private set; } = string.Empty;

        public void AddBoard(string serial)
        {
            if (CurrentCount == 0)
                FirstSerial = serial;

            LastSerial = serial;
            CurrentCount++;
        }

        public bool IsFull(int boxQuantity)
        {
            return boxQuantity > 0 && CurrentCount >= boxQuantity;
        }

        public BoxReport CreateReport(int quantity, string firmwareName)
        {
            return new BoxReport
            {
                BoxNumber = BoxNumber,
                Quantity = quantity,
                FirmwareName = firmwareName,
                FirstSerial = FirstSerial,
                LastSerial = LastSerial
            };
        }
        public void NextBox()
        {
            BoxNumber++;
            CurrentCount = 0;
            FirstSerial = string.Empty;
            LastSerial = string.Empty;
        }
    }
}
