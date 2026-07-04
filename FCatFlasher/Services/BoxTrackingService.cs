using FCatFlasher.Models;

namespace FCatFlasher.Services
{
/// <summary>
/// Веде стан поточної коробки та вже збережені звіти коробок.
/// </summary>
    internal class BoxTrackingService
    {
        private readonly BoxSession _boxSession = new();
        private readonly List<BoxReport> _boxReports = new();

        public int FlashedToday { get; private set; }
        public bool HasCurrentBox => _boxSession.HasBoards;
        public int CurrentBoxCount => _boxSession.CurrentCount;
        public int CurrentBoxNumber => _boxSession.BoxNumber;

/// <summary>
/// Завантажує існуючі звіти, щоб продовжити нумерацію коробок.
/// </summary>
        public void LoadReports(IEnumerable<BoxReport> reports)
        {
            _boxReports.Clear();

            foreach (BoxReport report in reports)
                _boxReports.Add(report);

            if (_boxReports.Count > 0)
                _boxSession.BoxNumber = _boxReports.Max(report => report.BoxNumber) + 1;
        }

/// <summary>
/// Встановлює денний лічильник прошитих плат для розрахунку номера коробки.
/// </summary>
        public void SetFlashedToday(int flashedToday)
        {
            FlashedToday = Math.Max(0, flashedToday);
        }

/// <summary>
/// Повертає проміжний звіт поточної неповної коробки.
/// </summary>
        public BoxReport? GetCurrentBoxReport(int boxQuantity, string firmwareName)
        {
            if (boxQuantity <= 0)
                return null;

            return _boxSession.CreateCurrentReport(firmwareName);
        }

/// <summary>
/// Завершує поточну неповну коробку і повертає звіт для збереження.
/// </summary>
        public BoxReport? CompleteCurrentBox(string firmwareName)
        {
            if (!_boxSession.HasBoards)
                return null;

            var report = _boxSession.CreateCurrentReport(firmwareName);
            _boxReports.Add(report);
            return report;
        }

/// <summary>
/// Додає плату до коробки і повертає звіт, якщо коробка заповнилась.
/// </summary>
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

/// <summary>
/// Переходить до наступної коробки після збереження поточної.
/// </summary>
        public void NextBox()
        {
            _boxSession.NextBox();
        }
    }
}
