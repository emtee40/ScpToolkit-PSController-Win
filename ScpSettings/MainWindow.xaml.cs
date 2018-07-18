﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using ScpControl;
using ScpControl.ScpCore;

namespace ScpSettings
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private fields

        private readonly ScpProxy _proxy = new ScpProxy();
        private GlobalConfiguration _config;

        #endregion

        #region Ctor

        public MainWindow()
        {
            InitializeComponent();
        }

        #endregion

        #region WPF event methods

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            WriteConfig();
        }

        private void WriteConfig()
        {
            if (!_proxy.IsActive || _config == null)
                return;

            _config.IdleTimeout *= GlobalConfiguration.IdleTimeoutMultiplier;
            _config.Latency *= GlobalConfiguration.LatencyMultiplier;

            _proxy.WriteConfig(_config);
            _proxy.Stop();
        }

        private void Window_Initialized(object sender, EventArgs e)
        {
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                if (!_proxy.Start())
                {
                    MessageBox.Show("Couldn't connect to server, please check if the service is running!",
                        "Fatal error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                _config = _proxy.ReadConfig();
            }
            catch
            {
                MessageBox.Show("Couldn't request configuration, make sure the SCP DSx Service is running!",
                    "Couldn't fetch configuration",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            _config.IdleTimeout /= GlobalConfiguration.IdleTimeoutMultiplier;
            _config.Latency /= GlobalConfiguration.LatencyMultiplier;

            DataContext = null;
            DataContext = _config;

            // Invoke Slider EventHandlers To Correctly Display GroupBox Headers
            IdleTimeoutSlider_ValueChanged(null, new RoutedPropertyChangedEventArgs<double> (0, IdleTimeoutSlider.Value));
            RumbleLatencySlider_ValueChanged(null, new RoutedPropertyChangedEventArgs<double>(0, RumbleLatencySlider.Value));
            LEDsFlashingPeriodSlider_ValueChanged(null, new RoutedPropertyChangedEventArgs<double>(0, LEDsFlashingPeriodSlider.Value));
        }

        private void IdleTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var value = e.NewValue;

            if (value == 0)
            {
                IdleTimeoutGroupBox.Header = "Idle Timeout: Disabled";
            }
            else if (value == 1)
            {
                IdleTimeoutGroupBox.Header = "Idle Timeout: 1 minute";
            }
            else
            {
                IdleTimeoutGroupBox.Header = string.Format("Idle Timeout: {0} minutes", value);
            }
        }

        private void RumbleLatencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var value = ((int)e.NewValue) << 4;

            RumbleLatencyGroupBox.Header = string.Format("Rumble Latency: {0} ms", value);
        }

        private void LEDsFlashingPeriodSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var value = (int)e.NewValue;

            LEDsFlashingPeriodGroupBox.Header = string.Format("LEDs flashing period: {0} ms", value);
        }

        private void XInputModToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            var rootDir = _config.Pcsx2RootPath;
            var pluginsDir = Path.Combine(rootDir, "Plugins");
            const string modFileName = "LilyPad-Scp-r5875.dll";

            if (!Directory.Exists(pluginsDir))
            {
                MessageBox.Show("Please set the path to PCSX2!", "Path empty",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var lilypadOrig = Directory.GetFiles(pluginsDir, "*.dll").FirstOrDefault(f => f.Contains("lilypad"));
                var lilypadMod = Path.Combine(GlobalConfiguration.AppDirectory, "LilyPad", modFileName);
                var xinputMod = Path.Combine(GlobalConfiguration.AppDirectory, @"XInput\x86");

                // copy modded XInput DLL and dependencies
                foreach (var file in Directory.GetFiles(xinputMod))
                {
                    File.Copy(file, Path.Combine(rootDir, Path.GetFileName(file)), true);
                }

                // back up original plugin
                if (!string.IsNullOrEmpty(lilypadOrig))
                {
                    File.Move(lilypadOrig, Path.ChangeExtension(lilypadOrig, ".orig"));
                }

                // copy modded lilypad plugin
                File.Copy(lilypadMod, Path.Combine(pluginsDir, modFileName), true);

                XInputModToggleButton.Content = "Disable";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't mod PCSX2!", "Mod install failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                MessageBox.Show(ex.Message, "Error details",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void XInputModToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            var rootDir = _config.Pcsx2RootPath;
            var pluginsDir = Path.Combine(rootDir, "Plugins");

            if (!Directory.Exists(pluginsDir))
                return;

            const string modFileName = "LilyPad-Scp-r5875.dll";

            File.Delete(Path.Combine(rootDir, "XInput1_3.dll"));
            File.Delete(Path.Combine(pluginsDir, modFileName));

            var lilypadOrig = Directory.GetFiles(pluginsDir, "*.orig").FirstOrDefault(f => f.Contains("lilypad"));

            if (!string.IsNullOrEmpty(lilypadOrig))
            {
                File.Move(lilypadOrig, Path.ChangeExtension(lilypadOrig, ".dll"));
            }

            XInputModToggleButton.Content = "Enable";
        }

        private void DisableEvents()
        {
            IdleTimeoutSlider.ValueChanged -= IdleTimeoutSlider_ValueChanged;

            RumbleLatencySlider.ValueChanged -= RumbleLatencySlider_ValueChanged;
            LEDsFlashingPeriodSlider.ValueChanged -= LEDsFlashingPeriodSlider_ValueChanged;

            XInputModToggleButton.Checked -= XInputModToggleButton_OnChecked;
            XInputModToggleButton.Unchecked -= XInputModToggleButton_Unchecked;
        }

        private void EnableEvents()
        {
            IdleTimeoutSlider.ValueChanged += IdleTimeoutSlider_ValueChanged;

            RumbleLatencySlider.ValueChanged += RumbleLatencySlider_ValueChanged;
            LEDsFlashingPeriodSlider.ValueChanged += LEDsFlashingPeriodSlider_ValueChanged;

            XInputModToggleButton.Checked += XInputModToggleButton_OnChecked;
            XInputModToggleButton.Unchecked += XInputModToggleButton_Unchecked;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            DisableEvents();

            WriteConfig();
            LoadConfig();

            EnableEvents();
        }

        #endregion
    }
}
