using FCatFlasher.Models;
using System.Collections.ObjectModel;

namespace FCatFlasher.Services
{
    internal class BoxTrackingService
    {
        private readonly BoxSession _boxSession = new();

        public ObservableCollection<BoxReport> BoxReports { get; } = new();

        public int FlashedToday { get; private set; }

        public BoxReport? RegisterFlashedBoard(
            int boxQuantity,
            string serial,
            string firmwareName)
        {
            if (boxQuantity <= 0)
                return null;

            FlashedToday++;

            _boxSession.AddBoard(serial);

            if (!_boxSession.IsFull(boxQuantity))
                return null;

            var report = _boxSession.CreateReport(
                boxQuantity,
                firmwareName);

            BoxReports.Add(report);

            return report;
        }

        public void NextBox()
        {
            _boxSession.NextBox();
        }
    }
}
