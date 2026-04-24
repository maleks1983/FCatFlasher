using FCatFlasher.Helpers;
using FCatFlasher.Services;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FCatFlasher.ViewModels
{
    internal class MainViewModel : INotifyPropertyChanged
    {
        // 🔌 Сервіси
        private readonly FlashService _flashService = new FlashService();
        private readonly SerialService _serial = new SerialService();

        // 🔘 Команди
        public ICommand ConnectCommand { get; }
        public RelayCommand FlashCommand { get; }

        // 📄 Firmware шлях (з правильним binding)
        private string _firmwarePath = "firmware.hex";
        public string FirmwarePath
        {
            get => _firmwarePath;
            set
            {
                _firmwarePath = value;
                OnPropertyChanged();
            }
        }

        // 🔄 Стан
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                FlashCommand.RaiseCanExecuteChanged();
            }
        }

        private string _status = "Idle";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private string _log = "";
        public string Log
        {
            get => _log;
            set { _log = value; OnPropertyChanged(); }
        }

        // 🔧 Конструктор
        public MainViewModel()
        {
            ConnectCommand = new RelayCommand(Connect);

            FlashCommand = new RelayCommand(
                async () => await Flash(),
                () => !IsBusy
            );

            _serial.DataReceived += OnDataReceived;
        }

        // 🔌 Підключення до COM
        private void Connect()
        {
            try
            {
                _serial.Open("COM3", 115200);
                Log += "Connected to COM3\n";
            }
            catch (Exception ex)
            {
                Log += "ERROR: " + ex.Message + "\n";
            }
        }

        // 🔌 Відключення (додали!)
        private void Disconnect()
        {
            _serial.Close();
            Log += "Disconnected\n";
        }

        // 📥 Отримання даних з плати
        private void OnDataReceived(string data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Log += data;

                // 🔥 більш точна логіка
                if (data.Contains("TEST OK"))
                    Status = "Success";

                if (data.Contains("TEST FAIL"))
                    Status = "Error";
            });
        }

        // ⚡ Прошивка
        private async Task Flash()
        {
            try
            {
                // перевірка файлу
                if (!System.IO.File.Exists(FirmwarePath))
                {
                    Log += "Firmware not found\n";
                    return;
                }

                IsBusy = true;
                Status = "Flashing...";
                Log += "\n--- FLASH START ---\n";

                var result = await _flashService.Flash(FirmwarePath);

                Log += result;
                Status = "Flashed";
            }
            catch (Exception ex)
            {
                Log += "\nERROR: " + ex.Message;
                Status = "Error";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // 🔔 INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}