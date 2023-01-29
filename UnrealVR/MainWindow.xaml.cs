﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Diagnostics;
using System.Security.Policy;
using System.Windows.Threading;
using System.Reflection;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows.Markup;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;

using Microsoft.Extensions.Configuration.Ini;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using static UnrealVR.SharedMemory;
using System.Threading.Channels;

namespace UnrealVR {
    class KeyValueComment {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";

        public int KeyAsInt { get { return Int32.Parse(Key); } set { Key = value.ToString(); } }

        public Dictionary<string, string> ComboValues { get; set; } = new Dictionary<string, string>();
    };

    enum RenderingMethod {
        [Description("Native Stereo")]
        NativeStereo = 0,
        [Description("Synced Sequential")]
        SyncedSequential = 1,
        [Description("Alternating/AFR")]
        Alternating = 2
    };

    enum SyncedSequentialMethods {
        SkipTick = 0,
        SkipDraw = 1,
    };

    class ComboMapping {

        public static Dictionary<string, string> RenderingMethodValues = new Dictionary<string, string>(){
            {"0", "Native Stereo" },
            {"1", "Synced Sequential" },
            {"2", "Alternating/AFR" }
        };

        public static Dictionary<string, string> SyncedSequentialMethodValues = new Dictionary<string, string>(){
            {"0", "Skip Tick" },
            {"1", "Skip Draw" },
        };

        public static Dictionary<string, Dictionary<string, string>> KeyEnums = new Dictionary<string, Dictionary<string, string>>() {
            { "VR_RenderingMethod", RenderingMethodValues },
            { "VR_SyncedSequentialMethod", SyncedSequentialMethodValues },
        };
    };

    class MandatoryConfig {
        public static Dictionary<string, string> Entries = new Dictionary<string, string>() {
            { "VR_RenderingMethod", ((int)RenderingMethod.NativeStereo).ToString() },
            { "VR_SyncedSequentialMethod", ((int)SyncedSequentialMethods.SkipTick).ToString() },
        };
    };

    public class ValueTemplateSelector : DataTemplateSelector {
        public DataTemplate ComboBoxTemplate { get; set; }
        public DataTemplate TextBoxTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container) {
            var keyValuePair = (KeyValueComment)item;
            if (ComboMapping.KeyEnums.ContainsKey(keyValuePair.Key)) {
                return ComboBoxTemplate;
            } else {
                return TextBoxTemplate;
            }
        }
    }
    public partial class MainWindow : Window {
        // variables
        // process list
        private List<Process> m_processList = new List<Process>();
        private MainWindowSettings m_mainWindowSettings = new MainWindowSettings();

        private string m_lastSelectedProcessName = new string("");
        private int m_lastSelectedProcessId = 0;

        private SharedMemory.Data? m_lastSharedData = null;
        private bool m_connected = false;

        private DispatcherTimer m_updateTimer = new DispatcherTimer {
            Interval = new TimeSpan(0, 0, 1)
        };

        private IConfiguration? m_currentConfig = null;
        private string? m_currentConfigPath = null;

        public MainWindow() {
            InitializeComponent();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            FillProcessList();
            m_openvrRadio.IsChecked = m_mainWindowSettings.OpenVRRadio;
            m_openxrRadio.IsChecked = m_mainWindowSettings.OpenXRRadio;

            m_updateTimer.Tick += (sender, e) => Dispatcher.Invoke(MainWindow_Update);
            m_updateTimer.Start();
        }

        private void Update_InjectStatus() {
            if (m_connected) {
                m_injectButton.Content = "Terminate Connected Process";
                return;
            }

            if (m_lastSelectedProcessId == 0) {
                m_injectButton.Content = "Inject";
                return;
            }

            try {
                var verifyProcess = Process.GetProcessById(m_lastSelectedProcessId);

                if (verifyProcess == null || verifyProcess.HasExited || verifyProcess.ProcessName != m_lastSelectedProcessName) {
                    var processes = Process.GetProcessesByName(m_lastSelectedProcessName);

                    if (processes == null || processes.Length == 0 || !AnyInjectableProcesses(processes)) {
                        m_injectButton.Content = "Waiting for Process";
                        return;
                    }
                }

                m_injectButton.Content = "Inject";
            } catch (ArgumentException) {
                var processes = Process.GetProcessesByName(m_lastSelectedProcessName);

                if (processes == null || processes.Length == 0 || !AnyInjectableProcesses(processes)) {
                    m_injectButton.Content = "Waiting for Process";
                    return;
                }

                m_injectButton.Content = "Inject";
            }
        }

        private void Hide_ConnectionOptions() {
            m_openGameDirectoryBtn.Visibility = Visibility.Collapsed;
        }

        private void Show_ConnectionOptions() {
            m_openGameDirectoryBtn.Visibility = Visibility.Visible;
        }

        private void Update_InjectorConnectionStatus() {
            var data = SharedMemory.GetData();

            if (data != null) {
                m_connectionStatus.Text = UnrealVRConnectionStatus.Connected;
                m_connectionStatus.Text += ": " + data?.path;
                m_connectionStatus.Text += "\nThread ID: " + data?.mainThreadId.ToString();
                m_lastSharedData = data;
                m_connected = true;
                Show_ConnectionOptions();
            } else {
                m_connectionStatus.Text = UnrealVRConnectionStatus.NoInstanceDetected;
                m_connected = false;
                Hide_ConnectionOptions();
            }
        }

        private string GetGlobalDir() {
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            directory += "\\UnrealVRMod";

            if (!System.IO.Directory.Exists(directory)) {
                System.IO.Directory.CreateDirectory(directory);
            }

            return directory;
        }

        private string GetGlobalGameDir(string gameName) {
            string directory = GetGlobalDir() + "\\" + gameName;

            if (!System.IO.Directory.Exists(directory)) {
                System.IO.Directory.CreateDirectory(directory);
            }

            return directory;
        }

        private void OpenGlobalDir_Clicked(object sender, RoutedEventArgs e) {
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            directory += "\\UnrealVRMod";

            if (!System.IO.Directory.Exists(directory)) {
                System.IO.Directory.CreateDirectory(directory);
            }

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorerPath = System.IO.Path.Combine(windowsDirectory, "explorer.exe");
            Process.Start(explorerPath, directory);
        }

        private void OpenGameDir_Clicked(object sender, RoutedEventArgs e) {
            if (m_lastSharedData == null) {
                return;
            }

            var directory = System.IO.Path.GetDirectoryName(m_lastSharedData?.path);
            if (directory == null) {
                return;
            }

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorerPath = System.IO.Path.Combine(windowsDirectory, "explorer.exe");
            Process.Start(explorerPath, directory);
        }

        private void MainWindow_Update() {
            Update_InjectorConnectionStatus();
            Update_InjectStatus();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            m_mainWindowSettings.OpenXRRadio = m_openxrRadio.IsChecked == true;
            m_mainWindowSettings.OpenVRRadio = m_openvrRadio.IsChecked == true;

            m_mainWindowSettings.Save();
        }

        private string m_lastDisplayedWarningProcess = "";
        private string[] m_discouragedPlugins = {
            "OpenVR",
            "OpenXR",
            "Oculus"
        };

        private bool AreVRPluginsPresent_InEngineDir(string enginePath) {
            string pluginsPath = enginePath + "\\Binaries\\ThirdParty";

            if (!Directory.Exists(pluginsPath)) {
                return false;
            }

            foreach (string discouragedPlugin in m_discouragedPlugins) {
                string pluginPath = pluginsPath + "\\" + discouragedPlugin;

                if (Directory.Exists(pluginPath)) {
                    return true;
                }
            }

            return false;
        }

        private bool AreVRPluginsPresent(string gameDirectory) {
            try {
                var parentPath = gameDirectory;

                for (int i = 0; i < 10; ++i) {
                    parentPath = System.IO.Path.GetDirectoryName(parentPath);

                    if (parentPath == null) {
                        return false;
                    }

                    if (Directory.Exists(parentPath + "\\Engine")) {
                        return AreVRPluginsPresent_InEngineDir(parentPath + "\\Engine");
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Exception caught: {ex}");
            }

            return false;
        }

        private string IniToString(IConfiguration config) {
            string result = "";

            foreach (var kv in config.AsEnumerable()) {
                result += kv.Key + "=" + kv.Value + "\n";
            }

            return result;
        }

        private void SaveCurrentConfig() {
            if (m_currentConfig == null || m_currentConfigPath == null) {
                return;
            }

            var iniStr = IniToString(m_currentConfig);
            Debug.Print(iniStr);

            File.WriteAllText(m_currentConfigPath, iniStr);

            if (m_connected) {
                SharedMemory.SendCommand(SharedMemory.Command.ReloadConfig);
            }

            InitializeConfig_FromPath(m_currentConfigPath);
        }

        private void TextChanged_Value(object sender, RoutedEventArgs e) {
            try {
                if (m_currentConfig == null || m_currentConfigPath == null) {
                    return;
                }

                var textBox = (TextBox)sender;
                var keyValuePair = (KeyValueComment)textBox.DataContext;

                bool changed = m_currentConfig[keyValuePair.Key] != textBox.Text;
                m_currentConfig[keyValuePair.Key] = textBox.Text;

                if (changed) {
                    SaveCurrentConfig();
                }
            } catch(Exception ex) { 
                Console.WriteLine(ex.ToString()); 
            }
        }

        private void ComboChanged_Value(object sender, RoutedEventArgs e) {
            try {
                if (m_currentConfig == null || m_currentConfigPath == null) {
                    return;
                }

                var comboBox = (ComboBox)sender;
                var keyValuePair = (KeyValueComment)comboBox.DataContext;

                bool changed = m_currentConfig[keyValuePair.Key] != keyValuePair.Value;
                m_currentConfig[keyValuePair.Key] = keyValuePair.Value;

                if (changed) {
                    SaveCurrentConfig();
                }
            } catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }

        private void InitializeConfig_FromPath(string configPath) {
            var builder = new ConfigurationBuilder().AddIniFile(configPath, optional: true, reloadOnChange: true);

            m_currentConfig = builder.Build();
            m_currentConfigPath = configPath;

            foreach (var entry in MandatoryConfig.Entries) {
                if (m_currentConfig.AsEnumerable().ToList().FindAll(v => v.Key == entry.Key).Count() == 0) {
                    m_currentConfig[entry.Key] = entry.Value;
                    SaveCurrentConfig();
                }
            }

            var vanillaList = m_currentConfig.AsEnumerable().ToList();
            vanillaList.Sort((a, b) => a.Key.CompareTo(b.Key));

            List<KeyValueComment> newList = new List<KeyValueComment>();

            foreach (var kv in vanillaList) {
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)) {
                    Dictionary<string, string> comboValues = new Dictionary<string, string>();

                    if (ComboMapping.KeyEnums.ContainsKey(kv.Key)) {
                        var valueList = ComboMapping.KeyEnums[kv.Key];

                        if (valueList != null && valueList.ContainsKey(kv.Value)) {
                            comboValues = valueList;
                        }
                    }

                    newList.Add(new KeyValueComment { Key = kv.Key, Value = kv.Value, ComboValues = comboValues });
                }
            }

            m_iniListView.ItemsSource = newList;
            m_iniListView.Visibility = Visibility.Visible;
        }

        private void InitializeConfig(string gameName) {
            var configDir = GetGlobalGameDir(gameName);
            var configPath = configDir + "\\config.txt";

            InitializeConfig_FromPath(configPath);
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            //ComboBoxItem comboBoxItem = ((sender as ComboBox).SelectedItem as ComboBoxItem);

            try {
                var box = (sender as ComboBox);
                if (box == null || box.SelectedIndex < 0 || box.SelectedIndex > m_processList.Count) {
                    return;
                }

                var p = m_processList[box.SelectedIndex];
                if (p == null || p.HasExited) {
                    return;
                }

                m_lastSelectedProcessName = p.ProcessName;
                m_lastSelectedProcessId = p.Id;

                // Search for the VR plugins inside the game directory
                // and warn the user if they exist.
                if (m_lastDisplayedWarningProcess != m_lastSelectedProcessName && p.MainModule != null) {
                    m_lastDisplayedWarningProcess = m_lastSelectedProcessName;

                    var gamePath = p.MainModule.FileName;
                    
                    if (gamePath != null) {
                        var gameDirectory = System.IO.Path.GetDirectoryName(gamePath);

                        if (gameDirectory != null) {
                            if (AreVRPluginsPresent(gameDirectory)) {
                                MessageBox.Show("VR plugins have been detected in the game install directory.\n" +
                                                "You may want to delete or rename these as they will cause issues with the mod.\n" +
                                                "You may also want to pass -nohmd as a command-line option to the game.");
                            }

                            InitializeConfig(p.ProcessName);
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Exception caught: {ex}");
            }
        }

        private void ComboBox_DropDownOpened(object sender, System.EventArgs e) {
            m_lastSelectedProcessName = "";
            m_lastSelectedProcessId = 0;

            FillProcessList();
            Update_InjectStatus();
        }

        private void Donate_Clicked(object sender, RoutedEventArgs e) {
            Process.Start(new ProcessStartInfo("https://patreon.com/praydog") { UseShellExecute = true });
        }

        private void Inject_Clicked(object sender, RoutedEventArgs e) {
            // "Terminate Connected Process"
            if (m_connected) {
                try {
                    var pid = m_lastSharedData?.pid;

                    if (pid != null) {
                        var target = Process.GetProcessById((int)pid);
                        target.CloseMainWindow();
                    }
                } catch(Exception) {

                }

                return;
            }

            var selectedProcessName = m_processListBox.SelectedItem;

            if (selectedProcessName == null) {
                return;
            }

            var index = m_processListBox.SelectedIndex;
            var process = m_processList[index];

            if (process == null) {
                return;
            }

            // Double check that the process we want to inject into exists
            // this can happen if the user presses inject again while
            // the previous combo entry is still selected but the old process
            // has died.
            try {
                var verifyProcess = Process.GetProcessById(m_lastSelectedProcessId);

                if (verifyProcess == null || verifyProcess.HasExited || verifyProcess.ProcessName != m_lastSelectedProcessName) {
                    var processes = Process.GetProcessesByName(m_lastSelectedProcessName);

                    if (processes == null || processes.Length == 0) {
                        return;
                    }

                    process = processes[0];
                    m_processList[index] = process;
                    m_processListBox.Items[index] = GenerateProcessName(process);
                    m_processListBox.SelectedIndex = index;
                }
            } catch(Exception ex) {
                MessageBox.Show(ex.Message);
                return;
            }

            string runtimeName;

            if (m_openvrRadio.IsChecked == true) {
                runtimeName = "openvr_api.dll";
            } else if (m_openxrRadio.IsChecked == true) {
                runtimeName = "openxr_loader.dll";
            } else {
                runtimeName = "openvr_api.dll";
            }

            if (Injector.InjectDll(runtimeName, process.Id)) {
                Injector.InjectDll("UnrealVRBackend.dll", process.Id);
            }
        }

        private string GenerateProcessName(Process p) {
            return p.ProcessName + " (pid: " + p.Id + ")" + " (" + p.MainWindowTitle + ")";
        }

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process([In] IntPtr hProcess, [Out] out bool wow64Process);

        private bool IsInjectableProcess(Process process) {
            if (Environment.Is64BitOperatingSystem) {
                try {
                    bool isWow64 = false;
                    if (IsWow64Process(process.Handle, out isWow64) && isWow64) {
                        return false;
                    }
                } catch {
                    // If we threw an exception here, then the process probably can't be accessed anyways.
                    return false;
                }
            }

            if (process.MainWindowTitle.Length == 0) {
                return false;
            }

            foreach (ProcessModule module in process.Modules) {
                if (module.ModuleName == null) {
                    continue;
                }

                string moduleLow = module.ModuleName.ToLower();
                if (moduleLow == "d3d11.dll" || moduleLow == "d3d12.dll") {
                    return true;
                }
            }

            return false;
        }

        private bool AnyInjectableProcesses(Process[] processList) {
            foreach (Process process in processList) {
                if (IsInjectableProcess(process)) {
                    return true;
                }
            }

            return false;
        }
        private SemaphoreSlim m_processSemaphore = new SemaphoreSlim(1, 1); // create a semaphore with initial count of 1 and max count of 1

        private async void FillProcessList() {
            // Allow the previous running FillProcessList task to finish first
            if (m_processSemaphore.CurrentCount == 0) {
                return;
            }

            await m_processSemaphore.WaitAsync();

            try {
                m_processList.Clear();
                m_processListBox.Items.Clear();

                await Task.Run(() => {
                    // get the list of processes
                    Process[] processList = Process.GetProcesses();

                    // loop through the list of processes
                    foreach (Process process in processList) {
                        if (IsInjectableProcess(process)) {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                m_processList.Add(process);
                                m_processList.Sort((a, b) => a.ProcessName.CompareTo(b.ProcessName));
                                m_processListBox.Items.Clear();

                                foreach (Process p in m_processList) {
                                    string processName = GenerateProcessName(p);
                                    m_processListBox.Items.Add(processName);
                                }
                            });
                        }
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        m_processListBox.Items.Clear();

                        foreach (Process process in m_processList) {
                            string processName = GenerateProcessName(process);
                            m_processListBox.Items.Add(processName);
                        }
                    });
                });
            } finally {
                m_processSemaphore.Release();
            }
        }
    }
}