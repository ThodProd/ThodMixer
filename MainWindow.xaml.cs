using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Navigation;

using MidiInputDevice = Melanchall.DryWetMidi.Multimedia.InputDevice;

namespace AudioMixerApp
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<InputStrip> InputStrips { get; } = new();
        public ObservableCollection<Bus> Шины { get; } = new();
        public ObservableCollection<Bus> Колонки => Шины;
        private readonly AudioEngine _engine = new();
        private string? SelectedMidiDevice;
        private bool _isLocked = false;
        private bool _reallyExit = false;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 1. Сначала загружаем конфиг (включая сохранённое имя MIDI-устройства)
            LoadConfig();

            // 2. Сбрасываем LED (на всякий случай, пока MIDI ещё не подключен)
            _engine.ResetAllLeds();
            _engine.UpdateAllLeds();

            // 3. Обновляем список MIDI-устройств и восстанавливаем подключение
            RefreshMidiDevices();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) => _engine.UpdateMeters(InputStrips, Шины);
            timer.Start();

            UpdateAutoStartButton();
        }

        private void RefreshMidiDevices()
        {
            string? currentSelected = SelectedMidiDevice;
            MidiCombo.Items.Clear();
            foreach (var dev in MidiInputDevice.GetAll())
            {
                MidiCombo.Items.Add(dev.Name);
            }
            if (!string.IsNullOrEmpty(currentSelected) && MidiCombo.Items.Contains(currentSelected))
            {
                MidiCombo.SelectedItem = currentSelected;
            }
            else
            {
                MidiCombo.SelectedIndex = -1;
                SelectedMidiDevice = null;
                _engine.SetMidiDevice(null);
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized)
            {
                Hide(); // сворачиваем в трей
            }
        }

        // Обработчик закрытия окна (крестик) — теперь реальное закрытие
        protected override void OnClosing(CancelEventArgs e)
        {
            // Ничего не отменяем — крестик закрывает приложение
            StopAll();
            base.OnClosing(e);
        }

        // Метод для выхода через трей
        public void ExitApplication()
        {
            StopAll();
            Close(); // вызовет OnClosing и закроет окно
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            bool wasLocked = _isLocked;
            try
            {
                // Показываем оверлей "Перезапуск..." как при импорте
                LockOverlay.Visibility = Visibility.Visible;
                LockOverlayText.Text = "Перезапуск...";
                Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

                SaveConfig();
                _engine.ResetAllLeds();
                LoadConfig(); // Полностью перезагружаем/реинициализируем конфигурацию
                RefreshMidiDevices(); // Обновляем список MIDI-устройств и переподключаем контроллер
                _engine.UpdateAllLeds(); // Синхронизируем лампочки

                // Небольшая задержка, чтобы пользователь успел увидеть оверлей
                System.Threading.Thread.Sleep(200);
            }
            finally
            {
                // Восстанавливаем состояние блокировки
                if (wasLocked)
                {
                    LockOverlay.Visibility = Visibility.Visible;
                    LockOverlayText.Text = "Заблокировано";
                }
                else
                {
                    LockOverlay.Visibility = Visibility.Collapsed;
                    LockOverlayText.Text = "Заблокировано";
                }
            }
        }

        private void Slider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                var binding = slider.GetBindingExpression(Slider.ValueProperty);
                binding?.UpdateSource();
            }
        }
        private void ResetEngine()
        {
            foreach (var strip in InputStrips)
            {
                strip.Capture?.StopRecording();
                strip.Capture?.Dispose();
                strip.Capture = null;
                strip.RoutedBuffers.Clear();
            }
            foreach (var bus in Шины)
            {
                bus.Output?.Stop();
                bus.Output?.Dispose();
                bus.Output = null;
                bus.Mixer = null;
                bus.VolumeProvider = null;
                bus.MeterProvider = null;
            }
            foreach (var strip in InputStrips)
            {
                if (!string.IsNullOrEmpty(strip.ВыбранныйВход))
                    _engine.StartInput(strip);
            }
            foreach (var bus in Шины)
            {
                if (!string.IsNullOrEmpty(bus.ВыбранныйВыход))
                    _engine.StartOutput(bus);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }

        private void StopAll()
        {
            // 1. Останавливаем все входы (микрофоны)
            foreach (var strip in InputStrips)
            {
                if (strip.Capture != null)
                {
                    try { strip.Capture.StopRecording(); } catch { }
                    strip.Capture.Dispose();
                    strip.Capture = null;
                }
                strip.RoutedBuffers.Clear();
            }

            // 2. Останавливаем все выходы (динамики/шины)
            foreach (var bus in Шины)
            {
                if (bus.Output != null)
                {
                    try { bus.Output.Stop(); } catch { }
                    bus.Output.Dispose();
                    bus.Output = null;
                }
                bus.Mixer = null;
                bus.VolumeProvider = null;
                bus.MeterProvider = null;
            }

            // 3. ГАСИМ ЛАМПОЧКИ НА MIDI
            // Сначала отправляем команду сброса всех LED
            _engine.ResetAllLeds();

            // Даем маленькую задержку, чтобы MIDI успел обработать команды перед отключением
            System.Threading.Thread.Sleep(50);

            // 4. Отключаем MIDI-устройство полностью
            _engine.SetMidiDevice(null);
        }

        private void LoadConfig()
        {
            LoadConfigFrom(DefaultConfigPath);
        }
        private static string DefaultConfigPath
        {
            get
            {
                var dir=Environment.GetEnvironmentVariable("THODHUB_DATA_DIR");
                if(string.IsNullOrWhiteSpace(dir)) return "config.json";
                Directory.CreateDirectory(dir);
                return Path.Combine(dir,"config.json");
            }
        }

        private void SelectAndConnectMidiDevice(string? deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                MidiCombo.SelectedIndex = -1;
                _engine.SetMidiDevice(null);
                SelectedMidiDevice = null;
                return;
            }

            if (MidiCombo.Items.Contains(deviceName))
            {
                MidiCombo.SelectedItem = deviceName;
            }
            else
            {
                // Устройство пропало из системы
                MidiCombo.SelectedIndex = -1;
                SelectedMidiDevice = null;
                _engine.SetMidiDevice(null);
                return;
            }

            var dev = MidiInputDevice.GetAll().FirstOrDefault(d => d.Name == deviceName);
            if (dev != null)
            {
                try
                {
                    _engine.SetMidiDevice(dev);
                    _engine.UpdateAllLeds();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка подключения MIDI:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    SelectedMidiDevice = null;
                    _engine.SetMidiDevice(null);
                    MidiCombo.SelectedIndex = -1;
                }
            }
        }

        private void LoadConfigFrom(string filePath)
        {
            // Если файл отсутствует или пустой → дефолтная конфигурация
            if (filePath == DefaultConfigPath &&
                (!File.Exists(filePath) || string.IsNullOrWhiteSpace(File.ReadAllText(filePath))))
            {
                Шины.Clear(); _engine._buses.Clear();
                var master = new Bus("Мастер", _engine) { CanDelete = false };
                Шины.Add(master); _engine.AddBus(master);

                InputStrips.Clear(); _engine._inputs.Clear();
                for (int i = 1; i <= 6; i++)
                {
                    var strip = new InputStrip($"Дорожка {i}", _engine) { CanDelete = (i > 1) };
                    InputStrips.Add(strip);
                    _engine.AddInput(strip);
                }

                SelectedMidiDevice = null;

                // ─── Явно сбрасываем глобальные ноты при первом запуске ─────────────
                _engine.MidiNotePlayPause = -1;
                _engine.MidiNoteNextTrack = -1;
                _engine.MidiNotePrevTrack = -1;

                _engine.LedFeedbackPlayPause = false;
                _engine.LedFeedbackNextTrack = false;
                _engine.LedFeedbackPrevTrack = false;

                _isLocked = false;
                SaveConfig();
                return;
            }

            // Нормальная загрузка
            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<Config>(json) ?? new Config();

            StopAll();

            Шины.Clear(); _engine._buses.Clear();
            InputStrips.Clear(); _engine._inputs.Clear();

            // шины ...
            int busIndex = 0;
            foreach (var bc in config.Buses)
            {
                var bus = new Bus(bc.Name, _engine)
                {
                    Громкость = bc.Volume,
                    Mute = bc.Mute,
                    ВыбранныйВыход = bc.SelectedOutput,
                    MidiCcVolume = bc.MidiCcVolume,
                    MidiNoteMute = bc.MidiNoteMute,
                    SendLedFeedbackMute = bc.LedMute,
                    CanDelete = (busIndex > 0)
                };
                Шины.Add(bus);
                _engine.AddBus(bus);
                busIndex++;
            }

            // дорожки ...
            int stripIndex = 0;
            foreach (var sc in config.InputStrips)
            {
                var strip = new InputStrip(sc.Name, _engine)
                {
                    Громкость = sc.Volume,
                    Mute = sc.Mute,
                    Solo = sc.Solo,
                    MidiCcVolume = sc.MidiCcVolume,
                    MidiNoteMute = sc.MidiNoteMute,
                    MidiNoteSolo = sc.MidiNoteSolo,
                    SendLedFeedbackMute = sc.LedMute,
                    SendLedFeedbackSolo = sc.LedSolo,
                    ВыбранныйВход = sc.SelectedInput,
                    GateEnabled = sc.GateEnabled,
                    GateThresholdDb = sc.GateThresholdDb,
                    GateAttackMs = sc.GateAttackMs,
                    GateHoldMs = sc.GateHoldMs,
                    GateReleaseMs = sc.GateReleaseMs,
                    GateHysteresisDb = sc.GateHysteresisDb,
                    CompressorEnabled = sc.CompressorEnabled,
                    CompressorThresholdDb = sc.CompressorThresholdDb,
                    CompressorRatio = sc.CompressorRatio,
                    NormalizeTargetLevel = sc.NormalizeTargetLevel,
                    CanDelete = (stripIndex > 0)
                };
                InputStrips.Add(strip);
                _engine.AddInput(strip);
                stripIndex++;

                strip.Routings.Clear();
                foreach (var rc in sc.Routings)
                {
                    var bus = Шины.FirstOrDefault(b => b.Название == rc.BusName);
                    if (bus != null)
                    {
                        strip.Routings.Add(new BusRouting(strip, bus, _engine)
                        {
                            Enabled = rc.Enabled,
                            MidiNoteRouting = rc.MidiNoteRouting,
                            SendLedFeedback = rc.Led
                        });
                    }
                }
            }

            // ─── Самое важное — восстанавливаем глобальные ноты и LED-флаги ───────
            _engine.MidiNotePlayPause = config.MidiNotePlayPause;
            _engine.MidiNoteNextTrack = config.MidiNoteNextTrack;
            _engine.MidiNotePrevTrack = config.MidiNotePrevTrack;

            _engine.LedFeedbackPlayPause = config.LedFeedbackPlayPause;
            _engine.LedFeedbackNextTrack = config.LedFeedbackNextTrack;
            _engine.LedFeedbackPrevTrack = config.LedFeedbackPrevTrack;

            SelectedMidiDevice = config.SelectedMidi;

            _isLocked = config.IsLocked;
            btnLock.Content = _isLocked ? "🔓 Разблокировать" : "🔒 Заблокировать";
            LockOverlay.Visibility = _isLocked ? Visibility.Visible : Visibility.Collapsed;

            // Запускаем устройства
            foreach (var strip in InputStrips)
                if (!string.IsNullOrEmpty(strip.ВыбранныйВход))
                    _engine.StartInput(strip);

            foreach (var bus in Шины)
                if (!string.IsNullOrEmpty(bus.ВыбранныйВыход))
                    _engine.StartOutput(bus);

            // Синхронизируем LED после загрузки (очень важно!)
            _engine.ResetAllLeds();
            _engine.UpdateAllLeds();

            UpdateAutoStartButton();
        }



        private void RemoveStrip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is InputStrip strip)
            {
                if (!strip.CanDelete) return;
                InputStrips.Remove(strip);
                _engine._inputs.Remove(strip);
            }
        }

        private void RemoveBus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Bus bus)
            {
                if (!bus.CanDelete) return;
                Шины.Remove(bus);
                _engine._buses.Remove(bus);
                foreach (var strip in InputStrips)
                {
                    var toRemove = strip.Routings.FirstOrDefault(r => r.Bus == bus);
                    if (toRemove != null) strip.Routings.Remove(toRemove);
                }
            }
        }
        private void SaveConfigTo(string filePath)
        {
            var config = new Config
            {
                InputStrips = InputStrips.Select(s => new StripConfig
                {
                    Name = s.Название,
                    Volume = s.Громкость,
                    Mute = s.Mute,
                    Solo = s.Solo,
                    MidiCcVolume = s.MidiCcVolume,
                    MidiNoteMute = s.MidiNoteMute,
                    MidiNoteSolo = s.MidiNoteSolo,
                    LedMute = s.SendLedFeedbackMute,
                    LedSolo = s.SendLedFeedbackSolo,
                    SelectedInput = s.ВыбранныйВход,
                    GateEnabled = s.GateEnabled,
                    GateThresholdDb = s.GateThresholdDb,
                    GateAttackMs = s.GateAttackMs,
                    GateHoldMs = s.GateHoldMs,
                    GateReleaseMs = s.GateReleaseMs,
                    GateHysteresisDb = s.GateHysteresisDb,
                    CompressorEnabled = s.CompressorEnabled,
                    CompressorThresholdDb = s.CompressorThresholdDb,
                    CompressorRatio = s.CompressorRatio,
                    NormalizeTargetLevel = s.NormalizeTargetLevel,
                    Routings = s.Routings.Select(r => new RoutingConfig
                    {
                        BusName = r.Bus.Название,
                        Enabled = r.Enabled,
                        MidiNoteRouting = r.MidiNoteRouting,
                        Led = r.SendLedFeedback
                    }).ToList()
                }).ToList(),

                Buses = Шины.Select(b => new BusConfig
                {
                    Name = b.Название,
                    Volume = b.Громкость,
                    Mute = b.Mute,
                    MidiCcVolume = b.MidiCcVolume,
                    MidiNoteMute = b.MidiNoteMute,
                    LedMute = b.SendLedFeedbackMute,
                    SelectedOutput = b.ВыбранныйВыход
                }).ToList(),

                SelectedMidi = SelectedMidiDevice,

                // ─── Вот эти четыре строки должны быть здесь ────────────────────────
                MidiNotePlayPause = _engine.MidiNotePlayPause,
                MidiNoteNextTrack = _engine.MidiNoteNextTrack,
                MidiNotePrevTrack = _engine.MidiNotePrevTrack,

                LedFeedbackPlayPause = _engine.LedFeedbackPlayPause,
                LedFeedbackNextTrack = _engine.LedFeedbackNextTrack,
                LedFeedbackPrevTrack = _engine.LedFeedbackPrevTrack,

                IsLocked = _isLocked
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public void SaveConfig()
        {
            SaveConfigTo(DefaultConfigPath);
        }
        private void MidiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;

            if (MidiCombo.SelectedItem is string name && !string.IsNullOrEmpty(name))
            {
                SelectedMidiDevice = name;
                var dev = MidiInputDevice.GetAll().FirstOrDefault(d => d.Name == name);
                if (dev != null)
                {
                    try
                    {
                        _engine.SetMidiDevice(dev);
                        SaveConfig();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка при подключении {name}:\n{ex.Message}", "MIDI ошибка");
                        SelectedMidiDevice = null;
                    }
                }
            }
        }

        private void RefreshMidi_Click(object sender, RoutedEventArgs e)
        {
            RefreshMidiDevices();
        }

        private void MapMidi_Click(object sender, RoutedEventArgs e)
        {
            var wnd = new MidiMappingWindow(_engine, this) { Owner = this };
            wnd.ShowDialog();
        }

        private void AddStrip_Click(object sender, RoutedEventArgs e)
        {
            var strip = new InputStrip($"Дорожка {InputStrips.Count + 1}", _engine);
            InputStrips.Add(strip);
            _engine.AddInput(strip);
        }

        private void AddBus_Click(object sender, RoutedEventArgs e)
        {
            var bus = new Bus($"Шина {Шины.Count + 1}", _engine);
            Шины.Add(bus);
            _engine.AddBus(bus);
        }

        private void InputDeviceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.DataContext is InputStrip strip)
                _engine.StartInput(strip);
        }

        private void BusOutputChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.DataContext is Bus bus)
                _engine.StartOutput(bus);
        }

        // ────────────────────────────────────────────────
        // Справка по эффектам — кнопки «i»
        // ────────────────────────────────────────────────

        private void ShowGateInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Подрезатель шума (Noise Gate) — очень полезная штука для чистого звука\n\n" +
                "Что делает в двух словах:\n" +
                "Когда ты молчишь — звук полностью выключается (тишина). Как только начинаешь говорить или играть — звук мгновенно включается.\n" +
                "Результат: исчезает постоянное шипение микрофона, шум комнаты, вентилятор, гул от кондиционера.\n\n" +

                "Главные параметры и что они делают:\n\n" +

                "Порог (GateThresholdDb) — от -80 до -10 dB\n" +
                "   Это уровень громкости, ниже которого звук считается «шумом» и вырезается.\n" +
                "   Примеры:\n" +
                "   • -60 dB — пропускает почти всё, шум почти не режется (для очень тихих микрофонов)\n" +
                "   • -45…-35 dB — золотая середина для голоса и речи\n" +
                "   • -25 dB — очень строго, может резать тихие окончания слов\n\n" +
                "   Совет: начинай с -40, говори в микрофон — если режет слова, делай ниже (-45, -50). Если шум всё равно слышен — делай выше (-35, -30).\n\n" +

                "Attack (GateAttackMs) — от 0.5 до 15 мс\n" +
                "   Как быстро гейт открывается, когда звук превысил порог.\n" +
                "   • 0.5–3 мс — мгновенно, очень естественно для голоса и ударных\n" +
                "   • 5–10 мс — чуть медленнее, мягче, для акустической гитары\n" +
                "   • >10 мс — может «съедать» начало звука (щелчки, «п» и «т»)\n\n" +
                "   Рекомендация: 2–4 мс — почти всегда идеально.\n\n" +

                "Hold (GateHoldMs) — от 5 до 150 мс\n" +
                "   Сколько времени гейт остаётся открытым после того, как звук упал ниже порога.\n" +
                "   • 20–50 мс — стандарт, не даёт «дрожанию» (когда гейт быстро открывается-закрывается)\n" +
                "   • 80–120 мс — для длинных нот или речи с паузами\n" +
                "   • Меньше 10 мс — может «заикаться» на тихих звуках\n\n" +
                "   Совет: 30–60 мс — золотая середина.\n\n" +

                "Release (GateReleaseMs) — от 30 до 600 мс\n" +
                "   Как плавно гейт закрывается после окончания Hold.\n" +
                "   • 100–250 мс — естественно для голоса, не слышно щелчка закрытия\n" +
                "   • 300–500 мс — очень плавно, для атмосферных звуков, гитары\n" +
                "   • <80 мс — может быть слышно «хлопок» закрытия\n\n" +
                "   Рекомендация: 150–300 мс — почти всегда хорошо.\n\n" +

                "Hysteresis (GateHysteresisDb) — от 2 до 15 dB\n" +
                "   Разница между порогом открытия и закрытия (защита от «дребезга»).\n" +
                "   • 6–8 dB — стандарт, хороший баланс\n" +
                "   • 3–5 dB — если всё равно дрожит\n" +
                "   • 10–15 dB — если очень шумное окружение\n\n" +
                "   Совет: 6–8 dB — почти всегда оптимально.\n\n" +

                "Коротко — как настроить быстро:\n" +
                "1. Порог → -40 dB\n" +
                "2. Attack → 3 мс\n" +
                "3. Hold → 40 мс\n" +
                "4. Release → 200 мс\n" +
                "5. Hysteresis → 6 dB\n" +
                "Говори → если режет — порог ниже, если шум — порог выше.",

                "Подрезатель шума — подробная справка",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private void ShowCompressorInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Компрессор — один из самых мощных инструментов для ровного звука\n\n" +
                "Что делает в двух словах:\n" +
                "Громкие звуки делает тише, тихие — чуть громче.\n" +
                "Результат: голос/инструмент звучит стабильно, без резких скачков громкости.\n\n" +

                "Два главных параметра:\n\n" +

                "Порог (CompressorThresholdDb) — от -60 до -6 dB\n" +
                "   Всё, что громче этого уровня — сжимается.\n" +
                "   • -40 … -30 dB — сжимает почти всё (сильный компрессор)\n" +
                "   • -25 … -18 dB — средний, хороший для голоса\n" +
                "   • -12 … -6 dB — лёгкий, только пики убирает\n\n" +
                "   Совет: начинай с -24…-20, говори громко → если скачет — снижай порог.\n\n" +

                "Соотношение (CompressorRatio) — от 1:1 до 20:1\n" +
                "   Показывает, насколько сильно сжимается звук выше порога.\n" +
                "   • 1:1 — ничего не меняет (компрессор выключен)\n" +
                "   • 2:1 — очень мягко (громкий звук становится в 2 раза тише)\n" +
                "   • 4:1–6:1 — хороший баланс для голоса и речи\n" +
                "   • 8:1–12:1 — сильно сжимает, звук становится ровным\n" +
                "   • 20:1+ — почти лимитер (громче порога почти не становится)\n\n" +
                "   Рекомендация: 4:1–8:1 — почти всегда идеально.\n\n" +

                "Как настроить быстро и правильно:\n" +
                "1. Порог → -24 dB\n" +
                "2. Соотношение → 4:1\n" +
                "3. Говори громко и тихо\n" +
                "4. Если всё ещё скачет — снижай порог (-28, -32)\n" +
                "5. Если голос «задушенный», плоский — повышай порог или снижай соотношение\n\n" +

                "Типичные настройки:\n" +
                "   Разговорный подкаст: Порог -22…-18, Ratio 4:1–6:1\n" +
                "   Вокал в стриме: Порог -26…-20, Ratio 5:1–8:1\n" +
                "   Гитара/бас: Порог -30…-20, Ratio 4:1–10:1\n" +
                "   Очень динамичный звук: Порог -35, Ratio 8:1–12:1\n\n" +

                "Главный совет:\n" +
                "Компрессор — это не «сделать громче», а «сделать ровнее».\n" +
                "Лучше слабый компрессор, чем слишком сильный — иначе звук будет «плоским» и «мёртвым».",

                "Компрессор — подробная справка",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            _engine.SimulatePlayPause();
        }

        private void NextTrack_Click(object sender, RoutedEventArgs e)
        {
            _engine.SimulateNextTrack();
        }

        private void PrevTrack_Click(object sender, RoutedEventArgs e)
        {
            _engine.SimulatePrevTrack();
        }


        // ====================== БЛОКИРОВКА ЭКРАНА ======================
        private void LockScreen_Click(object sender, RoutedEventArgs e)
        {
            _isLocked = !_isLocked;
            btnLock.Content = _isLocked ? "🔓 Разблокировать" : "🔒 Заблокировать";
            LockOverlay.Visibility = _isLocked ? Visibility.Visible : Visibility.Collapsed;
        }



        // ====================== АВТОЗАПУСК ======================
        private void UpdateAutoStartButton()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("THODHUB_DATA_DIR")))
            {
                btnAutoStart.IsEnabled = false;
                btnAutoStart.Content = "Автозапуск в сборке ThodHUB пока недоступен";
                return;
            }
            btnAutoStart.Content = IsInAutoStart()
                ? "❌ Удалить из автозапуска"
                : "▶️ Добавить в автозапуск";
        }
          
        private bool IsInAutoStart()
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string lnkPath = Path.Combine(startupFolder, "ThodMixer.lnk");
                return File.Exists(lnkPath);
            }
            catch
            {
                return false;
            }
        }

        private string? TryGetCurrentExePath()
        {
            string? path = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;

            try
            {
                path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
            }
            catch { }

            try
            {
                path = AppDomain.CurrentDomain.BaseDirectory;
                string? friendly = AppDomain.CurrentDomain.FriendlyName;
                if (!string.IsNullOrWhiteSpace(friendly) && friendly.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.Combine(path, friendly);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            return null;
        }

        private void ToggleAutoStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string targetLnk = Path.Combine(startupFolder, "ThodMixer.lnk");

                if (IsInAutoStart())
                {
                    File.Delete(targetLnk);
                }
                else
                {
                    string? exePath = TryGetCurrentExePath();

                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        MessageBox.Show(
                            "Не удалось определить путь к .exe-файлу.\n\n" +
                            "Запустите программу двойным кликом (не из Visual Studio),\n" +
                            "лучше — из опубликованной папки (Publish → Folder).",
                            "Путь к exe не найден",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string exeDir = Path.GetDirectoryName(exePath) ?? "";

                    Type? wshType = Type.GetTypeFromProgID("WScript.Shell");
                    if (wshType == null)
                    {
                        MessageBox.Show("Не удалось создать WScript.Shell (компонент отключён?).",
                            "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    dynamic wsh = Activator.CreateInstance(wshType);
                    dynamic shortcut = wsh.CreateShortcut(targetLnk);

                    // ← Вот главное исправление: оборачиваем путь в кавычки
                    shortcut.TargetPath = "\"" + exePath + "\"";
                    shortcut.WorkingDirectory = exeDir;
                    shortcut.Description = "ThodMixer";
                    // shortcut.IconLocation = exePath + ",0";   // если нужна иконка exe

                    shortcut.Save();

                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wsh);
                }

                UpdateAutoStartButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка автозапуска:\n{ex.Message}\n\n{ex.StackTrace?.Split(new[] { '\r', '\n' }, 2)[0] ?? ""}",
                    "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // ====================== ЭКСПОРТ / ИМПОРТ ======================
        private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            btnSettings.ContextMenu.PlacementTarget = btnSettings;
            btnSettings.ContextMenu.IsOpen = true;
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName = "ГрокМикшер_настройки.json",
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                SaveConfigTo(dlg.FileName);
                MessageBox.Show("Настройки успешно экспортированы!", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
                Title = "Выберите файл настроек для импорта"
            };

            if (dlg.ShowDialog() != true)
                return;

            var result = MessageBox.Show(
                $"Вы действительно хотите импортировать настройки из файла:\n\n{Path.GetFileName(dlg.FileName)}\n\nТекущие настройки будут полностью заменены!",
                "Подтверждение импорта",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            // запоминаем состояние блокировки
            bool wasLocked = _isLocked;

            try
            {
                // показываем загрузку
                LockOverlay.Visibility = Visibility.Visible;
                LockOverlayText.Text = "Загрузка...";
                Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

                // импорт
                LoadConfigFrom(dlg.FileName);

                // сохраняем
                SaveConfig();

                UpdateAutoStartButton();

                // восстанавливаем состояние блокировки
                if (wasLocked)
                {
                    LockOverlay.Visibility = Visibility.Visible;
                    LockOverlayText.Text = "Заблокировано";
                }
                else
                {
                    LockOverlay.Visibility = Visibility.Collapsed;
                    LockOverlayText.Text = "Заблокировано";
                }

                MessageBox.Show(
                    "Настройки успешно импортированы и применены!",
                    "Успех",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // восстанавливаем состояние блокировки
                if (wasLocked)
                {
                    LockOverlay.Visibility = Visibility.Visible;
                    LockOverlayText.Text = "Заблокировано";
                }
                else
                {
                    LockOverlay.Visibility = Visibility.Collapsed;
                    LockOverlayText.Text = "Заблокировано";
                }

                MessageBox.Show(
                    $"При импорте произошла ошибка:\n\n{ex.Message}",
                    "Ошибка импорта",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }



    }
    
    public class Config
    {
        public List<StripConfig> InputStrips { get; set; } = new();
        public List<BusConfig> Buses { get; set; } = new();
        public string? SelectedMidi { get; set; }
        public int MidiNotePlayPause { get; set; } = -1;
        public int MidiNoteNextTrack { get; set; } = -1;
        public int MidiNotePrevTrack { get; set; } = -1;
        public bool IsLocked { get; set; } = false;

        public bool LedFeedbackPlayPause { get; set; } = false;
        public bool LedFeedbackNextTrack { get; set; } = false;
        public bool LedFeedbackPrevTrack { get; set; } = false;
    }
    public class StripConfig
    {
        public string Name { get; set; } = "";
        public double Volume { get; set; }
        public bool Mute { get; set; }
        public bool Solo { get; set; }
        public int MidiCcVolume { get; set; } = -1;
        public int MidiNoteMute { get; set; } = -1;
        public int MidiNoteSolo { get; set; } = -1;
        public bool LedMute { get; set; } = true;
        public bool LedSolo { get; set; } = true;
        public string? SelectedInput { get; set; }
        public List<RoutingConfig> Routings { get; set; } = new();
        public bool GateEnabled { get; set; }
        public double GateThresholdDb { get; set; } = -45;
        public double GateAttackMs { get; set; } = 2;
        public double GateHoldMs { get; set; } = 20;
        public double GateReleaseMs { get; set; } = 150;
        public double GateHysteresisDb { get; set; } = 6;

        public bool CompressorEnabled { get; set; }
        public double CompressorThresholdDb { get; set; } = -30;
        public double CompressorRatio { get; set; } = 4;
        public double NormalizeTargetLevel { get; set; } = 0.5;

        public bool LedFeedbackPlayPause { get; set; }
        public bool LedFeedbackNextTrack { get; set; }
        public bool LedFeedbackPrevTrack { get; set; }

    }
    public class BusConfig
    {
        public string Name { get; set; } = "";
        public double Volume { get; set; }
        public bool Mute { get; set; }
        public int MidiCcVolume { get; set; } = -1;
        public int MidiNoteMute { get; set; } = -1;
        public bool LedMute { get; set; } = true;
        public string? SelectedOutput { get; set; }
    }
    public class RoutingConfig
    {
        public string BusName { get; set; } = "";
        public bool Enabled { get; set; }
        public int MidiNoteRouting { get; set; } = -1;
        public bool Led { get; set; } = true;
    }
    public class InputStrip : INotifyPropertyChanged
    {
        private readonly AudioEngine _engine;
        public bool SendLedFeedbackMute { get; set; } = true;
        public bool SendLedFeedbackSolo { get; set; } = true;
        public string Название { get; set; }
        private bool _canDelete = true;
        public bool CanDelete
        {
            get => _canDelete;
            set
            {
                if (_canDelete == value) return;
                _canDelete = value;
                Raise(nameof(CanDelete));
            }
        }
        private double _громкость = -12;
        public double Громкость
        {
            get => _громкость;
            set
            {
                if (Math.Abs(_громкость - value) < 0.001) return;
                _громкость = value;
                Raise(nameof(Громкость));
            }
        }
        private bool _mute;
        public bool Mute
        {
            get => _mute;
            set
            {
                if (_mute == value) return;
                _mute = value;
                Raise(nameof(Mute));
                if (SendLedFeedbackMute)
                    _engine.SendNoteLed(MidiNoteMute, _mute);
            }
        }
        private bool _solo;
        public bool Solo
        {
            get => _solo;
            set
            {
                if (_solo == value) return;
                _solo = value;
                Raise(nameof(Solo));
                if (SendLedFeedbackSolo)
                    _engine.SendNoteLed(MidiNoteSolo, _solo);
            }
        }
        public double ВходУровень { get; set; }
        public int MidiCcVolume { get; set; } = -1;
        public int MidiNoteMute { get; set; } = -1;
        public int MidiNoteSolo { get; set; } = -1;
        public List<string> ДоступныеВходы => AudioEngine.GetInputDevices();
        public string? ВыбранныйВход { get; set; }
        public WasapiCapture? Capture { get; set; }
        public Dictionary<Bus, BufferedWaveProvider> RoutedBuffers { get; } = new();
        public ObservableCollection<BusRouting> Routings { get; } = new();


        // Компрессор
        private bool _compressorEnabled = false;
        public bool CompressorEnabled
        {
            get => _compressorEnabled;
            set
            {
                if (_compressorEnabled == value) return;
                _compressorEnabled = value;
                Raise(nameof(CompressorEnabled));
                _engine.ApplyEnhancements(this);
            }
        }

        private double _compressorThresholdDb = -30;
        public double CompressorThresholdDb
        {
            get => _compressorThresholdDb;
            set
            {
                if (Math.Abs(_compressorThresholdDb - value) < 0.1) return;
                _compressorThresholdDb = value;
                Raise(nameof(CompressorThresholdDb));
                _engine.ApplyEnhancements(this);
            }
        }

        private double _compressorRatio = 4;
        public double CompressorRatio
        {
            get => _compressorRatio;
            set
            {
                if (Math.Abs(_compressorRatio - value) < 0.1) return;
                _compressorRatio = value;
                Raise(nameof(CompressorRatio));
                _engine.ApplyEnhancements(this);
            }
        }

        // Нормализатор — целевой уровень громкости
        private double _normalizeTargetLevel = 0.5; // от 0.1 до 1.0
        public double NormalizeTargetLevel
        {
            get => _normalizeTargetLevel;
            set
            {
                if (Math.Abs(_normalizeTargetLevel - value) < 0.01) return;
                _normalizeTargetLevel = value;
                Raise(nameof(NormalizeTargetLevel));
                _engine.ApplyEnhancements(this);
            }
        }

        // Noise Gate
        private bool _gateEnabled = false;
        public bool GateEnabled
        {
            get => _gateEnabled;
            set
            {
                if (_gateEnabled == value) return;
                _gateEnabled = value;
                Raise(nameof(GateEnabled));
                _engine.ApplyEnhancements(this);
            }
        }
        private double _gateThresholdDb = -45;
        public double GateThresholdDb
        {
            get => _gateThresholdDb;
            set
            {
                if (Math.Abs(_gateThresholdDb - value) < 0.1) return;
                _gateThresholdDb = value;
                Raise(nameof(GateThresholdDb));
                _engine.ApplyEnhancements(this);
            }
        }
        private double _gateAttackMs = 2;
        public double GateAttackMs
        {
            get => _gateAttackMs;
            set
            {
                if (Math.Abs(_gateAttackMs - value) < 0.5) return;
                _gateAttackMs = value;
                Raise(nameof(GateAttackMs));
                _engine.ApplyEnhancements(this);
            }
        }
        private double _gateHoldMs = 20;
        public double GateHoldMs
        {
            get => _gateHoldMs;
            set
            {
                if (Math.Abs(_gateHoldMs - value) < 1) return;
                _gateHoldMs = value;
                Raise(nameof(GateHoldMs));
                _engine.ApplyEnhancements(this);
            }
        }
        private double _gateReleaseMs = 150;
        public double GateReleaseMs
        {
            get => _gateReleaseMs;
            set
            {
                if (Math.Abs(_gateReleaseMs - value) < 1) return;
                _gateReleaseMs = value;
                Raise(nameof(GateReleaseMs));
                _engine.ApplyEnhancements(this);
            }
        }
        private double _gateHysteresisDb = 6;
        public double GateHysteresisDb
        {
            get => _gateHysteresisDb;
            set
            {
                if (Math.Abs(_gateHysteresisDb - value) < 0.1) return;
                _gateHysteresisDb = value;
                Raise(nameof(GateHysteresisDb));
                _engine.ApplyEnhancements(this);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public InputStrip(string name, AudioEngine engine)
        {
            Название = name;
            _engine = engine;
        }
    }
    public class Bus : INotifyPropertyChanged
    {
        private readonly AudioEngine _engine;
        public bool SendLedFeedbackMute { get; set; } = true;
        public string Название { get; set; }
        private bool _canDelete = true;
        public bool CanDelete
        {
            get => _canDelete;
            set
            {
                if (_canDelete == value) return;
                _canDelete = value;
                Raise(nameof(CanDelete));
            }
        }
        private double _громкость = 0;
        public double Громкость
        {
            get => _громкость;
            set
            {
                if (Math.Abs(_громкость - value) < 0.001) return;
                _громкость = value;
                if (VolumeProvider != null) VolumeProvider.Volume = Mute ? 0f : (float)Math.Pow(10, value / 20.0);
                Raise(nameof(Громкость));
            }
        }
        private bool _mute;
        public bool Mute
        {
            get => _mute;
            set
            {
                if (_mute == value) return;
                _mute = value;
                if (VolumeProvider != null) VolumeProvider.Volume = value ? 0f : (float)Math.Pow(10, Громкость / 20.0);
                Raise(nameof(Mute));
                if (SendLedFeedbackMute)
                    _engine.SendNoteLed(MidiNoteMute, _mute);
            }
        }
        public double ВыходУровень { get; set; }
        public int MidiCcVolume { get; set; } = -1;
        public int MidiNoteMute { get; set; } = -1;
        public List<string> ДоступныеВыходы => AudioEngine.GetOutputDevices();
        public string? ВыбранныйВыход { get; set; }
        public WasapiOut? Output { get; set; }
        public MixingSampleProvider? Mixer { get; set; }
        public VolumeSampleProvider? VolumeProvider { get; set; }
        public MeteringSampleProvider? MeterProvider { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public Bus(string name, AudioEngine engine)
        {
            Название = name;
            _engine = engine;
        }
    }
    public class BusRouting : INotifyPropertyChanged
    {
        private readonly AudioEngine _engine;
        public InputStrip Strip { get; }
        public Bus Bus { get; }
        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                Raise(nameof(Enabled));
                if (value)
                {
                    if (!Strip.RoutedBuffers.ContainsKey(Bus))
                    {
                        var buffer = new BufferedWaveProvider(Strip.Capture?.WaveFormat ?? new WaveFormat(44100, 2))
                        {
                            BufferDuration = TimeSpan.FromMilliseconds(100),     // 60–120 мс — комфортный диапазон
                            DiscardOnBufferOverflow = true,                       
                            //ReadFully = false
                        };
                        Strip.RoutedBuffers[Bus] = buffer;
                    }
                }
                else
                {
                    Strip.RoutedBuffers.Remove(Bus);
                }
                if (!string.IsNullOrEmpty(Bus.ВыбранныйВыход))
                    _engine.StartOutput(Bus);
                if (SendLedFeedback)
                    _engine.SendNoteLed(MidiNoteRouting, _enabled);
            }
        }
        public int MidiNoteRouting { get; set; } = -1;
        public bool SendLedFeedback { get; set; } = true;

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public BusRouting(InputStrip s, Bus b, AudioEngine e)
        {
            Strip = s;
            Bus = b;
            _engine = e;
            _enabled = true;
        }
    }
    public class NoiseGateSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _threshold;
        private readonly float _hysteresis;
        private readonly int _attackSamples;
        private readonly int _holdSamples;
        private readonly int _releaseSamples;
        private float _gain = 0f;
        private int _holdCounter;
        private bool _isOpen;
        private float _env = 0f;

        public NoiseGateSampleProvider(ISampleProvider source,
            float thresholdDb = -45f, float hysteresisDb = 6f,
            float attackMs = 2f, float holdMs = 20f, float releaseMs = 150f)
        {
            _source = source;
            _threshold = (float)Math.Pow(10, thresholdDb / 20f);
            _hysteresis = (float)Math.Pow(10, hysteresisDb / 20f);
            int sampleRate = source.WaveFormat.SampleRate;
            _attackSamples = (int)(sampleRate * attackMs / 1000f);
            _holdSamples = (int)(sampleRate * holdMs / 1000f);
            _releaseSamples = (int)(sampleRate * releaseMs / 1000f);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            for (int i = 0; i < samplesRead; i++)
            {
                float sample = Math.Abs(buffer[offset + i]);
                _env = Math.Max(sample, _env * 0.999f);

                float effectiveThreshold = _isOpen ? _threshold / _hysteresis : _threshold;

                if (_env > effectiveThreshold)
                {
                    _isOpen = true;
                    _holdCounter = _holdSamples;
                    _gain = Math.Min(1f, _gain + 1f / _attackSamples);
                }
                else
                {
                    if (_holdCounter > 0)
                    {
                        _holdCounter--;
                    }
                    else
                    {
                        _isOpen = false;
                        _gain = Math.Max(0f, _gain - 1f / _releaseSamples);
                    }
                }

                buffer[offset + i] *= _gain;
            }

            return samplesRead;
        }
    }
    public class SimpleHighPass : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float _z1L, _z2L; // левый канал
        private float _z1R, _z2R; // правый канал
        private readonly double _a0, _a1, _a2, _b1, _b2;

        public SimpleHighPass(ISampleProvider source, float cutoffHz = 90f)
        {
            _source = source;

            double sampleRate = source.WaveFormat.SampleRate;
            double omega = 2 * Math.PI * cutoffHz / sampleRate;
            double sn = Math.Sin(omega);
            double cs = Math.Cos(omega);
            double alpha = sn / (2 * 0.7071); // Butterworth Q = 1/√2

            // Правильные коэффициенты для HIGH-PASS (2-го порядка)
            double b0 = (1 + cs) / 2;
            double b1 = -(1 + cs);
            double b2 = (1 + cs) / 2;
            double a0 = 1 + alpha;
            double a1 = -2 * cs;
            double a2 = 1 - alpha;

            _a0 = b0 / a0;
            _a1 = b1 / a0;
            _a2 = b2 / a0;
            _b1 = a1 / a0;
            _b2 = a2 / a0;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            bool stereo = WaveFormat.Channels == 2;

            for (int i = 0; i < read; i += WaveFormat.Channels)
            {
                // Левый канал
                float xL = buffer[offset + i];
                float yL = (float)(_a0 * xL + _a1 * xL + _a2 * xL - _b1 * _z1L - _b2 * _z2L);
                _z2L = _z1L;
                _z1L = yL;
                buffer[offset + i] = yL;

                // Правый канал
                if (stereo && i + 1 < read)
                {
                    float xR = buffer[offset + i + 1];
                    float yR = (float)(_a0 * xR + _a1 * xR + _a2 * xR - _b1 * _z1R - _b2 * _z2R);
                    _z2R = _z1R;
                    _z1R = yR;
                    buffer[offset + i + 1] = yR;
                }
            }

            return read;
        }
    }
    public class CompressorSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float _gain = 1f;
        private float _env = 0f;           // текущая огибающая
        private readonly float _threshold; // порог в линейном виде
        private readonly float _ratio;     // соотношение сжатия

        public CompressorSampleProvider(ISampleProvider source, float thresholdDb = -30f, float ratio = 4f)
        {
            _source = source;
            _threshold = (float)Math.Pow(10, thresholdDb / 20f);
            _ratio = ratio;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            for (int i = 0; i < read; i++)
            {
                float sample = buffer[offset + i];
                float abs = Math.Abs(sample);

                // Плавная огибающая
                _env = Math.Max(abs, _env * 0.995f);

                float desiredGain = 1f;
                if (_env > _threshold)
                {
                    // Сжимаем: уменьшаем громкость выше порога
                    float over = _env - _threshold;
                    desiredGain = 1f - (over * (1f - 1f / _ratio)) / _env;
                }

                // Плавный переход gain
                _gain = _gain * 0.98f + desiredGain * 0.02f;
                _gain = Math.Clamp(_gain, 0.3f, 3f); // не даём слишком сильно усиливать/ослаблять

                buffer[offset + i] *= _gain;
            }

            return read;
        }
    }

    // === УЛУЧШЕННЫЙ NORMALIZER (RMS-based AGC) ===
    public class AutoGainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float _gain = 1f;
        private float _env = 0f; // плавная огибающая
        private readonly float _targetLevel; // целевой уровень

        public AutoGainSampleProvider(ISampleProvider source, float targetLevel = 0.5f)
        {
            _source = source;
            _targetLevel = targetLevel;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            // Считаем RMS для блока
            float sumSq = 0f;
            for (int i = 0; i < read; i++)
                sumSq += buffer[offset + i] * buffer[offset + i];

            float rms = read > 0 ? (float)Math.Sqrt(sumSq / read) : 0f;

            // Плавное обновление огибающей
            _env = Math.Max(rms, _env * 0.995f); // медленно падает

            if (_env > 0.005f) // порог шума
            {
                float desiredGain = _targetLevel / Math.Max(_env, 0.0001f);
                // Плавный переход gain
                _gain = _gain * 0.99f + desiredGain * 0.01f;
                _gain = Math.Clamp(_gain, 0.4f, 8f);
            }
            else
            {
                // В тишине медленно возвращаем gain к 1
                _gain = _gain * 0.99f + 1f * 0.01f;
            }

            for (int i = 0; i < read; i++)
                buffer[offset + i] *= _gain;

            return read;
        }
    }

    public class AudioEngine
    {
        private MidiInputDevice? _midiIn;

        private OutputDevice? _midiOut;
        private bool _isUpdatingFromMidi = false;

        internal readonly List<InputStrip> _inputs = new();
        internal readonly List<Bus> _buses = new();

        public bool LedFeedbackPlayPause { get; set; } = false;
        public bool LedFeedbackNextTrack { get; set; } = false;
        public bool LedFeedbackPrevTrack { get; set; } = false;

        public MidiAssignment? CurrentLearningAssignment { get; set; }
        public event EventHandler? LearningStateChanged;
        public int MidiNotePlayPause { get; set; } = -1;
        public int MidiNoteNextTrack { get; set; } = -1;
        public int MidiNotePrevTrack { get; set; } = -1;
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private void SimulateMediaKey(byte vk)
        {
            keybd_event(vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public void SimulatePlayPause() => SimulateMediaKey(VK_MEDIA_PLAY_PAUSE);
        public void SimulateNextTrack() => SimulateMediaKey(VK_MEDIA_NEXT_TRACK);
        public void SimulatePrevTrack() => SimulateMediaKey(VK_MEDIA_PREV_TRACK);

        private void OnMidiEvent(object? sender, MidiEventReceivedEventArgs e)
        {
            // РЕЖИМ ОБУЧЕНИЯ
            if (CurrentLearningAssignment != null)
            {
                HandleLearningMode(e.Event);
                return;
            }

            if (_isUpdatingFromMidi) return;
            _isUpdatingFromMidi = true;

            try
            {
                switch (e.Event)
                {
                    case NoteEvent note:
                        HandleNoteEvent(note);
                        break;

                    case ControlChangeEvent cc:
                        HandleControlChange(cc);
                        break;

                    case PitchBendEvent pb:
                        HandlePitchBend(pb);
                        break;
                }
            }
            finally
            {
                _isUpdatingFromMidi = false;
            }
        }

        public void StopInput(InputStrip strip)
        {
            // Остановить захват
            strip.Capture?.StopRecording();
            strip.Capture?.Dispose();
            strip.Capture = null;

            // Очистить буферы маршрутов
            strip.RoutedBuffers.Clear();
        }

        public void RestartInput(InputStrip strip)
        {
            if (string.IsNullOrEmpty(strip.ВыбранныйВход))
                return;

            StopInput(strip);   // ❗ ОБЯЗАТЕЛЬНО
            StartInput(strip);  // ❗ теперь новая цепочка реально используется
        }


        public void ResetAllLeds()
        {
            if (_midiOut == null) return;

            // Самый простой и надёжный способ — выключить все ноты от 0 до 127
            // Это работает почти на всех контроллерах (Launchpad, APC, Launch Control, X-Touch Mini и т.д.)
            for (int note = 0; note < 128; note++)
            {
                var ev = new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)0);
                _midiOut.SendEvent(ev);
            }

            // Альтернативно (если контроллер поддерживает) можно отправить All Notes Off,
            // но он работает не везде корректно для LED:
            // _midiOut.SendEvent(new ControlChangeEvent(SevenBitNumber.valueOf(123), SevenBitNumber.valueOf(0)));
        }
        private void HandleLearningMode(MidiEvent midiEvent)
        {
            if (CurrentLearningAssignment == null)
                return;

            var ass = CurrentLearningAssignment;
            int? number = null;

            switch (ass.Type)
            {
                case MidiMapType.ControlChange:
                    if (midiEvent is ControlChangeEvent cc)
                        number = cc.ControlNumber;
                    break;
                case MidiMapType.Note:
                    if (midiEvent is NoteOnEvent on && on.Velocity > 0)
                        number = on.NoteNumber;
                    break;
                case MidiMapType.PitchBend:
                    if (midiEvent is PitchBendEvent pb)
                        number = pb.Channel;
                    break;
            }

            if (!number.HasValue)
                return;

            ass.SetNumber(number.Value);

            switch (ass.Target)
            {
                case InputStrip strip:
                    if (ass.Parameter == "Громкость") strip.MidiCcVolume = number.Value;
                    else if (ass.Parameter == "Mute")
                    {
                        strip.MidiNoteMute = number.Value;
                        strip.SendLedFeedbackMute = ass.LedFeedback;
                    }
                    else if (ass.Parameter == "Solo")
                    {
                        strip.MidiNoteSolo = number.Value;
                        strip.SendLedFeedbackSolo = ass.LedFeedback;
                    }
                    break;

                case Bus bus:
                    if (ass.Parameter == "Громкость") bus.MidiCcVolume = number.Value;
                    else if (ass.Parameter == "Mute")
                    {
                        bus.MidiNoteMute = number.Value;
                        bus.SendLedFeedbackMute = ass.LedFeedback;
                    }
                    break;

                case BusRouting routing:
                    routing.MidiNoteRouting = number.Value;
                    routing.SendLedFeedback = ass.LedFeedback;
                    break;

                case null:  // Глобальные медиа-команды
                    if (ass.Parameter == "Play/Pause")
                        MidiNotePlayPause = number.Value;
                    else if (ass.Parameter == "Next Track")
                        MidiNoteNextTrack = number.Value;
                    else if (ass.Parameter == "Prev Track")
                        MidiNotePrevTrack = number.Value;
                    break;
            }

            CurrentLearningAssignment = null;
            LearningStateChanged?.Invoke(this, EventArgs.Empty);
        }



        private void HandleNoteEvent(NoteEvent note)
        {
            // Определяем тип события
            bool isNoteOn = note is NoteOnEvent onEvent && onEvent.Velocity > 0;
            bool isNoteOff = note is NoteOffEvent || (note is NoteOnEvent on && on.Velocity == 0);

            int nn = note.NoteNumber;

            // ────────────────────────────────────────────────
            // 1. Глобальные медиа-команды — выполняем только при нажатии
            // ────────────────────────────────────────────────
            if (isNoteOn)
            {
                if (nn == MidiNotePlayPause)
                {
                    SimulatePlayPause();
                    if (LedFeedbackPlayPause)
                    {
                        SendNoteLed(MidiNotePlayPause, true);
                    }
                }

                if (nn == MidiNoteNextTrack)
                {
                    SimulateNextTrack();
                    if (LedFeedbackNextTrack)
                    {
                        SendNoteLed(MidiNoteNextTrack, true);
                    }
                }

                if (nn == MidiNotePrevTrack)
                {
                    SimulatePrevTrack();
                    if (LedFeedbackPrevTrack)
                    {
                        SendNoteLed(MidiNotePrevTrack, true);
                    }
                }
            }

            // ────────────────────────────────────────────────
            // 2. Гасим LED глобальных кнопок при отпускании (если событие пришло)
            // ────────────────────────────────────────────────
            if (isNoteOff)
            {
                if (nn == MidiNotePlayPause && LedFeedbackPlayPause)
                    SendNoteLed(MidiNotePlayPause, false);

                if (nn == MidiNoteNextTrack && LedFeedbackNextTrack)
                    SendNoteLed(MidiNoteNextTrack, false);

                if (nn == MidiNotePrevTrack && LedFeedbackPrevTrack)
                    SendNoteLed(MidiNotePrevTrack, false);
            }

            // ────────────────────────────────────────────────
            // 3. Остальные действия (mute, solo, routing) — только при нажатии
            // ────────────────────────────────────────────────
            if (!isNoteOn) return;

            // InputStrips — Mute и Solo
            foreach (var strip in _inputs)
            {
                if (strip.MidiNoteMute == nn)
                {
                    strip.Mute = !strip.Mute;
                }

                if (strip.MidiNoteSolo == nn)
                {
                    strip.Solo = !strip.Solo;
                }
            }

            // Buses — Mute
            foreach (var bus in _buses)
            {
                if (bus.MidiNoteMute == nn)
                {
                    bus.Mute = !bus.Mute;
                }
            }

            // Routings (Send to bus)
            foreach (var strip in _inputs)
            {
                foreach (var r in strip.Routings)
                {
                    if (r.MidiNoteRouting == nn)
                    {
                        r.Enabled = !r.Enabled;
                    }
                }
            }
        }

        private void HandleControlChange(ControlChangeEvent cc)
        {
            double normalized = cc.ControlValue / 127.0;
            double db = normalized * 72 - 60;

            foreach (var strip in _inputs)
                if (strip.MidiCcVolume == cc.ControlNumber)
                    strip.Громкость = db;

            foreach (var bus in _buses)
                if (bus.MidiCcVolume == cc.ControlNumber)
                    bus.Громкость = db;
        }

        private void HandlePitchBend(PitchBendEvent pb)
        {
            double normalized = pb.PitchValue / 16383.0;
            double db;

            if (pb.PitchValue < 300)  // первые 100 значений - тишина
                db = -96.0;
            else
                db = Math.Pow(normalized, 1.5) * 72 - 60;

            foreach (var strip in _inputs)
                if (strip.MidiCcVolume == pb.Channel)
                    strip.Громкость = db;

            foreach (var bus in _buses)
                if (bus.MidiCcVolume == pb.Channel)
                    bus.Громкость = db;
        }


        public void SendNoteLed(int note, bool state)
        {
            SendNoteLedIfNeeded(note, state);
        }

        public void SendNoteLedIfNeeded(int note, bool state)
        {
            if (note < 0 || _midiOut == null) return;

            var ev = new NoteOnEvent((SevenBitNumber)note, state ? (SevenBitNumber)127 : (SevenBitNumber)0);
            _midiOut.SendEvent(ev);
        }

        public void SendVolumeToMidi(object target)
        {
            if (_midiOut == null || _isUpdatingFromMidi) return;

            int ccNumber = -1;
            double volumeDb = 0;

            if (target is InputStrip strip)
            {
                ccNumber = strip.MidiCcVolume;
                volumeDb = strip.Громкость;
            }
            else if (target is Bus bus)
            {
                ccNumber = bus.MidiCcVolume;
                volumeDb = bus.Громкость;
            }

            if (ccNumber <= 0) return; // 0 = Pitch Bend, -1 = ничего

            int value = (int)Math.Clamp((volumeDb + 60) / 72 * 127, 0, 127);
            var ev = new ControlChangeEvent((SevenBitNumber)ccNumber, (SevenBitNumber)value);
            _midiOut.SendEvent(ev);
        }

        public void UpdateAllLeds()
        {
            if (_midiOut == null) return;

            foreach (var strip in _inputs)
            {
                if (strip.SendLedFeedbackMute) SendNoteLed(strip.MidiNoteMute, strip.Mute);
                if (strip.SendLedFeedbackSolo) SendNoteLed(strip.MidiNoteSolo, strip.Solo);

                foreach (var r in strip.Routings)
                {
                    if (r.SendLedFeedback) SendNoteLed(r.MidiNoteRouting, r.Enabled);
                }
            }

            foreach (var bus in _buses)
            {
                if (bus.SendLedFeedbackMute) SendNoteLed(bus.MidiNoteMute, bus.Mute);
            }
        }

        public void AddInput(InputStrip s)
        {
            _inputs.Add(s);
            foreach (var bus in _buses)
            {
                var old = s.Routings.FirstOrDefault(r => r.Bus == bus);
                if (old != null) s.Routings.Remove(old);
                s.Routings.Add(new BusRouting(s, bus, this) { Enabled = true });
            }
        }

        public void AddBus(Bus b)
        {
            _buses.Add(b);
            foreach (var strip in _inputs)
                strip.Routings.Add(new BusRouting(strip, b, this) { Enabled = true });
        }

        public void SetMidiDevice(MidiInputDevice? dev)

        {
            // Если передали null — просто отключаем MIDI
            if (dev == null)
            {
                if (_midiIn != null)
                {
                    try
                    {
                        _midiIn.EventReceived -= OnMidiEvent;
                        if (_midiIn.IsListeningForEvents)
                            _midiIn.StopEventsListening();
                        _midiIn.Dispose();
                    }
                    catch { } // глотаем ошибки при закрытии
                    _midiIn = null;
                }

                if (_midiOut != null)
                {
                    try { _midiOut.Dispose(); }
                    catch { }
                    _midiOut = null;
                }

                return; // больше ничего не делаем
            }

            // Если dev НЕ null — продолжаем обычную логику подключения
            if (_midiIn != null && _midiIn.Name == dev.Name && _midiIn.IsListeningForEvents)
            {
                return; // уже подключено это же устройство
            }

            // Закрываем старое устройство
            if (_midiIn != null)
            {
                try
                {
                    _midiIn.EventReceived -= OnMidiEvent;
                    if (_midiIn.IsListeningForEvents)
                        _midiIn.StopEventsListening();
                    _midiIn.Dispose();
                }
                catch { }
                _midiIn = null;
            }

            if (_midiOut != null)
            {
                try { _midiOut.Dispose(); }
                catch { }
                _midiOut = null;
            }

            // Даём Windows немного времени на освобождение ресурсов
            System.Threading.Thread.Sleep(80);

            // Открываем новое устройство
            try
            {
                _midiIn = dev;
                _midiIn.EventReceived += OnMidiEvent;
                _midiIn.StartEventsListening();

                // Ищем Output с тем же именем
                _midiOut = OutputDevice.GetAll()
                    .FirstOrDefault(d => d.Name == dev.Name);

                // ←←← добавляем очистку и синхронизацию состояния
                ResetAllLeds();
                UpdateAllLeds();
            }
            catch (MidiDeviceException ex)
            {
                MessageBox.Show($"Не удалось открыть MIDI-устройство:\n{ex.Message}\n\nПопробуйте переподключить устройство или выбрать другое.",
                                "Ошибка MIDI", MessageBoxButton.OK, MessageBoxImage.Error);
                _midiIn = null;
                _midiOut = null;
            }
        }


        public void StartInput(InputStrip strip)
        {
            strip.Capture?.StopRecording();
            strip.Capture?.Dispose();
            strip.Capture = null;
            strip.RoutedBuffers.Clear();

            var dev = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName == strip.ВыбранныйВход);

            if (dev == null) return;

            var cap = new WasapiCapture(dev);
            strip.Capture = cap;

            cap.DataAvailable += (s, e) =>
            {
                if (e.BytesRecorded == 0) return;

                // VU-метр входа (без unsafe)
                float max = 0f;
                for (int i = 0; i < e.BytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(e.Buffer, i);
                    float abs = Math.Abs(sample);
                    if (abs > max) max = abs;
                }
                strip.ВходУровень = max;
                strip.Raise(nameof(strip.ВходУровень));

                // Пропускаем, если muted или не в соло-режиме
                bool anySolo = _inputs.Any(st => st.Solo);
                if (strip.Mute || (anySolo && !strip.Solo))
                    return;

                // Применяем gain дорожки
                float gain = (float)Math.Pow(10, strip.Громкость / 20.0);



                byte[] scaledBuffer = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, scaledBuffer, 0, e.BytesRecorded);

                for (int i = 0; i < e.BytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(scaledBuffer, i) * gain;
                    byte[] bytes = BitConverter.GetBytes(sample);
                    Buffer.BlockCopy(bytes, 0, scaledBuffer, i, 4);
                }

                // Рассылаем во все подключённые шины
                foreach (var kvp in strip.RoutedBuffers)
                {
                    var buffer = kvp.Value;

                    // Очистка, если буфер почти полон (оставляем запас 20%)
                    //if (buffer.BufferedBytes > buffer.BufferLength * 0.92)
                    //{
                    //    buffer.ClearBuffer();         
                    //}

                    buffer.AddSamples(scaledBuffer, 0, e.BytesRecorded);
                }
            };

            cap.StartRecording();

            // ⚡ ВАЖНО: после старта входа активируем все маршруты
            foreach (var r in strip.Routings)
            {
                if (r.Enabled)
                {
                    // принудительно вызовем сеттер, чтобы он создавал буфер
                    bool tmp = r.Enabled;
                    r.Enabled = false;
                    r.Enabled = tmp;
                }
            }
        }

        public void StartOutput(Bus bus)
        {
            // 1. Полностью останавливаем и очищаем старый вывод (важно делать в правильном порядке)
            if (bus.Output != null)
            {
                bus.Output.Stop();
                bus.Output.Dispose();
                bus.Output = null;
            }

            bus.Mixer = null;
            bus.VolumeProvider = null;
            bus.MeterProvider = null;

            // 2. Находим устройство вывода
            var dev = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName == bus.ВыбранныйВыход);

            if (dev == null)
            {
                // Можно добавить лог или сообщение, если устройство не найдено
                return;
            }

            // 3. Создаём формат: 44.1 кГц, стерео, float — это стандарт для NAudio
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

            // 4. Создаём новый микшер (RemoveAllMixerInputs не нужен, т.к. мы создали новый объект)
            bus.Mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };

            // 5. Обрабатываем все дорожки, которые routed на эту шину
            foreach (var strip in _inputs)
            {
                if (strip.RoutedBuffers.TryGetValue(bus, out var buffer))
                {
                    // Базовый провайдер
                    ISampleProvider sp = buffer.ToSampleProvider();

                    // Порядок эффектов — самый логичный и безопасный:
                    // Сначала убираем шум → режем низ → сжимаем динамику → нормализуем

                    if (strip.GateEnabled)
                    {
                        sp = new NoiseGateSampleProvider(
                            sp,
                            thresholdDb: (float)strip.GateThresholdDb,
                            hysteresisDb: (float)strip.GateHysteresisDb,
                            attackMs: (float)strip.GateAttackMs,
                            holdMs: (float)strip.GateHoldMs,
                            releaseMs: (float)strip.GateReleaseMs
                        );
                    }


                    // Приводим к стерео и 44.1 кГц (если нужно)
                    if (sp.WaveFormat.Channels == 1)
                    {
                        sp = new MonoToStereoSampleProvider(sp);
                    }

                    if (sp.WaveFormat.SampleRate != 44100)
                    {
                        sp = new WdlResamplingSampleProvider(sp, 44100);
                    }

                    // Добавляем в микшер
                    bus.Mixer.AddMixerInput(sp);
                }
            }

            // 6. Громкость шины (применяется после всех эффектов)
            bus.VolumeProvider = new VolumeSampleProvider(bus.Mixer);
            bus.VolumeProvider.Volume = bus.Mute ? 0f : (float)Math.Pow(10, bus.Громкость / 20.0);

            // 7. Метр уровня (обновляет ВыходУровень)
            bus.MeterProvider = new MeteringSampleProvider(bus.VolumeProvider);
            bus.MeterProvider.StreamVolume += (sender, args) =>
            {
                float max = 0f;
                if (args.MaxSampleValues.Length > 0)
                {
                    max = args.MaxSampleValues.Max(v => Math.Abs(v));
                }
                bus.ВыходУровень = max;
                bus.Raise(nameof(bus.ВыходУровень));
            };

            // 8. Запуск WasapiOut
            bus.Output = new WasapiOut(dev, AudioClientShareMode.Shared, true, 100);
            bus.Output.Init(bus.MeterProvider.ToWaveProvider());
            bus.Output.Play();
        }

        public void ApplyEnhancements(InputStrip strip)
        {
            // Перезапускаем только те шины, куда routed этот стрип
            foreach (var routing in strip.Routings)
            {
                if (routing.Enabled && !string.IsNullOrEmpty(routing.Bus.ВыбранныйВыход))
                {
                    StartOutput(routing.Bus);
                }
            }
        }



        public static List<string> GetInputDevices()
        {
            return new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => d.FriendlyName ?? "Неизвестное устройство")
                .ToList();
        }

        public static List<string> GetOutputDevices()
        {
            return new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => d.FriendlyName ?? "Неизвестное устройство")
                .ToList();
        }

        public void UpdateMeters(IEnumerable<InputStrip> strips, IEnumerable<Bus> buses)
        {
            // Метры уже обновляются в реальном времени через MeteringSampleProvider
            // Можно оставить пустым или добавить принудительное обновление UI, если нужно
        }

        public IReadOnlyList<InputStrip> Inputs => _inputs;
        public IReadOnlyList<Bus> Buses => _buses;

    }

    public enum MidiMapType { ControlChange, Note, PitchBend }

    public class MidiMapping : INotifyPropertyChanged
    {
        public object Target { get; }

        public string Control { get; }
        public MidiMapType Type { get; }
        public int Number { get; set; } = -1;
        public string CcNumber => Type == MidiMapType.ControlChange && Number >= 0 ? Number.ToString() : "—";

        public event PropertyChangedEventHandler? PropertyChanged;
        public void RaisePropertyChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public MidiMapping(object target, string c, MidiMapType t, int n = -1)
        {
            Target = target;
            Control = c;
            Type = t;
            Number = n;
        }

    }

    public class MidiAssignment : INotifyPropertyChanged
    {
        public object Target { get; }               // InputStrip, Bus или BusRouting
        public string TargetName { get; }
        public string Parameter { get; }            // "Volume", "Mute", "Solo", "Send to AUX1" ...
        public MidiMapType Type { get; }            // Note или ControlChange
        public int Number { get; set; } = -1;       // номер ноты или CC


        private bool _ledFeedback = true;
        public bool LedFeedback
        {
            get => _ledFeedback;
            set
            {
                if (_ledFeedback == value) return;
                _ledFeedback = value;
                Raise(nameof(LedFeedback));
            }
        }



        // для UI
        public string DisplayNumber => Number >= 0 ? Number.ToString() : "—";
        public string StatusText { get; private set; } = "Не назначено";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        public MidiAssignment(object target, string targetName, string param, MidiMapType type, bool toggle = true)
        {
            Target = target;
            TargetName = targetName;
            Parameter = param;
            Type = type;

        }

        public void SetNumber(int number)
        {
            Number = number;
            Raise(nameof(Number));
            Raise(nameof(DisplayNumber));
            UpdateStatus();
        }



        public void UpdateStatus(string extra = "")
        {
            StatusText = Number < 0 ? "Не назначено" : $"Назначено: {Number}{(extra.Length > 0 ? " " + extra : "")}";
            Raise(nameof(StatusText));
        }
    }
}
