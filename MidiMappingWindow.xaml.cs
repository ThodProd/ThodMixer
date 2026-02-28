using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AudioMixerApp
{
    public partial class MidiMappingWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<MidiAssignment> Assignments { get; } = new();
        private readonly AudioEngine _engine;
        private readonly MainWindow _mainWindow;
        public string LearnStatus { get; private set; } = "Готов";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public MidiMappingWindow(AudioEngine engine, MainWindow mainWindow)
        {
            InitializeComponent();
            DataContext = this;
            _engine = engine;
            _mainWindow = mainWindow;
            _engine.LearningStateChanged += Engine_LearningStateChanged;
            LoadAllAssignments();
        }

        private void LoadAllAssignments()
        {
            Assignments.Clear();

            foreach (var strip in _engine.Inputs)
            {
                Assignments.Add(new MidiAssignment(strip, strip.Название, "Громкость", MidiMapType.PitchBend)
                { Number = strip.MidiCcVolume });

                Assignments.Add(new MidiAssignment(strip, strip.Название, "Mute", MidiMapType.Note)
                { Number = strip.MidiNoteMute, LedFeedback = strip.SendLedFeedbackMute });

                Assignments.Add(new MidiAssignment(strip, strip.Название, "Solo", MidiMapType.Note)
                { Number = strip.MidiNoteSolo, LedFeedback = strip.SendLedFeedbackSolo });

                foreach (var r in strip.Routings)
                {
                    Assignments.Add(new MidiAssignment(r, strip.Название, $"Send → {r.Bus.Название}", MidiMapType.Note)
                    { Number = r.MidiNoteRouting, LedFeedback = r.SendLedFeedback });
                }
            }

            foreach (var bus in _engine.Buses)
            {
                Assignments.Add(new MidiAssignment(bus, bus.Название + " (шина)", "Громкость", MidiMapType.PitchBend)
                { Number = bus.MidiCcVolume });

                Assignments.Add(new MidiAssignment(bus, bus.Название + " (шина)", "Mute", MidiMapType.Note)
                { Number = bus.MidiNoteMute, LedFeedback = bus.SendLedFeedbackMute });
            }

            // Глобальные медиа-команды
            Assignments.Add(new MidiAssignment(null, "Глобально", "Play/Pause", MidiMapType.Note)
            { Number = _engine.MidiNotePlayPause, LedFeedback = false });

            Assignments.Add(new MidiAssignment(null, "Глобально", "Next Track", MidiMapType.Note)
            { Number = _engine.MidiNoteNextTrack, LedFeedback = false });

            Assignments.Add(new MidiAssignment(null, "Глобально", "Prev Track", MidiMapType.Note)
            { Number = _engine.MidiNotePrevTrack, LedFeedback = false });
        }

        private void Engine_LearningStateChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                LearnStatus = _engine.CurrentLearningAssignment == null ? "Готов" : "Ожидание ввода...";
                Raise(nameof(LearnStatus));
            });
        }

        private void BtnLearn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MidiAssignment ass) return;

            _engine.CurrentLearningAssignment = ass;
            LearnStatus = "Ожидание MIDI...";
            Raise(nameof(LearnStatus));

            void OnLearningFinished(object? s, EventArgs ev)
            {
                _engine.LearningStateChanged -= OnLearningFinished;
                _mainWindow.SaveConfig();
                LearnStatus = "Готов (сохранено)";
                Raise(nameof(LearnStatus));
            }

            _engine.LearningStateChanged += OnLearningFinished;
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MidiAssignment ass) return;

            ass.SetNumber(-1);

            if (ass.Target is InputStrip strip)
            {
                if (ass.Parameter == "Громкость") strip.MidiCcVolume = -1;
                if (ass.Parameter == "Mute") strip.MidiNoteMute = -1;
                if (ass.Parameter == "Solo") strip.MidiNoteSolo = -1;
            }
            else if (ass.Target is Bus bus)
            {
                if (ass.Parameter == "Громкость") bus.MidiCcVolume = -1;
                if (ass.Parameter == "Mute") bus.MidiNoteMute = -1;
            }
            else if (ass.Target is BusRouting r)
            {
                r.MidiNoteRouting = -1;
            }
            else if (ass.Target == null)
            {
                if (ass.Parameter == "Play/Pause") _engine.MidiNotePlayPause = -1;
                if (ass.Parameter == "Next Track") _engine.MidiNoteNextTrack = -1;
                if (ass.Parameter == "Prev Track") _engine.MidiNotePrevTrack = -1;
            }

            _mainWindow.SaveConfig();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ass in Assignments)
            {
                switch (ass.Target)
                {
                    case InputStrip strip:
                        if (ass.Parameter == "Mute")
                            strip.SendLedFeedbackMute = ass.LedFeedback;
                        else if (ass.Parameter == "Solo")
                            strip.SendLedFeedbackSolo = ass.LedFeedback;
                        break;

                    case Bus bus:
                        if (ass.Parameter == "Mute")
                            bus.SendLedFeedbackMute = ass.LedFeedback;
                        break;

                    case BusRouting routing:
                        routing.SendLedFeedback = ass.LedFeedback;
                        break;
                }
            }

            _mainWindow.SaveConfig();
            _engine.ResetAllLeds();
            _engine.UpdateAllLeds();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _engine.CurrentLearningAssignment = null;
            _engine.LearningStateChanged -= Engine_LearningStateChanged;
            base.OnClosed(e);
        }
    }
}