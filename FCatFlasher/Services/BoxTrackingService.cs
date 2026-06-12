using FCatFlasher.Models;

namespace FCatFlasher.Services
{
    internal class BoxTrackingService
    {
        private readonly BoxSession _boxSession = new();
        private readonly List<BoxReport> _boxReports = new();

        public int FlashedToday { get; private set; }

        public void LoadReports(IEnumerable<BoxReport> reports)
        {
            _boxReports.Clear();

            foreach (BoxReport report in reports)
                _boxReports.Add(report);

            if (_boxReports.Count > 0)
                _boxSession.BoxNumber = _boxReports.Max(report => report.BoxNumber) + 1;
        }

        public void SetFlashedToday(int flashedToday)
        {
            FlashedToday = Math.Max(0, flashedToday);
        }

        public BoxReport? GetCurrentBoxReport(int boxQuantity, string firmwareName)
        {
            if (boxQuantity <= 0)
                return null;

            return _boxSession.CreateCurrentReport(firmwareName);
        }

        public BoxReport? RegisterFlashedBoard(
            int boxQuantity,
            string serial,
            string firmwareName)
        {
            FlashedToday++;

            if (boxQuantity <= 0)
                return null;

            _boxSession.AddBoard(serial);

            if (!_boxSession.IsFull(boxQuantity))
                return null;

            var report = _boxSession.CreateReport(
                boxQuantity,
                firmwareName);

            _boxReports.Add(report);

            return report;
        }

        public void NextBox()
        {
            _boxSession.NextBox();
        }
    }
}
