using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

// i fucking hate maui
// all code associated with MacOS goes untested, since i do not own a Mac / Mac KVM
namespace MultiInject
{
    public partial class MainPage : ContentPage
    {
        private Process _selectedProcess;

        public MainPage()
        {
            InitializeComponent();
            LoadProcesses();
        }

        private void LoadProcesses()
        {
            var processes = Process.GetProcesses()
                .OrderBy(p => p.ProcessName)
                .ToList();
            ProcessListView.ItemsSource = processes;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = e.NewTextValue.ToLower();
            var filteredProcesses = Process.GetProcesses()
                .Where(p => p.ProcessName.ToLower().Contains(searchText))
                .OrderBy(p => p.ProcessName)
                .ToList();
            ProcessListView.ItemsSource = filteredProcesses;
        }

        private void OnProcessSelected(object sender, SelectedItemChangedEventArgs e)
        {
            _selectedProcess = e.SelectedItem as Process;
            AttachButton.IsEnabled = _selectedProcess != null;
        }

        private async void OnAttachButtonClicked(object sender, EventArgs e)
        {
            if (_selectedProcess == null) return;

            var filePickerOptions = new PickOptions
            {
                PickerTitle = "Select a library to attach",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".dll" } },
            { DevicePlatform.macOS, new[] { ".dylib" } },
            { DevicePlatform.Create("Linux"), new[] { ".so" } }
        })
            };

            var result = await FilePicker.Default.PickAsync(filePickerOptions);
            if (result == null) return;

            var libPath = result.FullPath;

            if (OperatingSystem.IsWindows())
            {
                AttachDllWindows(libPath);
            }
            else if (OperatingSystem.IsMacOS())
            {
                AttachLibraryUnix(libPath);
            }
            else if (OperatingSystem.IsLinux())
            {
                await DisplayAlert("Linux Detected", "Linux detected; attempting library load.", "OK");
                AttachLibraryUnix(libPath);
            }
            else
            {
                await DisplayAlert("Unsupported OS", "Unsupported operating system for library attachment.", "OK");
            }
        }

        private void AttachDllWindows(string dllPath)
        {
            try
            {
                var processHandle = OpenProcess(0x001F0FFF, false, _selectedProcess.Id);
                if (processHandle == IntPtr.Zero)
                {
                    DisplayAlert("Error", "Failed to open process.", "OK");
                    return;
                }

                IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                IntPtr allocMemAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)((dllPath.Length + 1) * Marshal.SizeOf(typeof(char))),
                    0x1000 | 0x2000, 0x04);

                if (allocMemAddress == IntPtr.Zero)
                {
                    DisplayAlert("Error", "Failed to allocate memory in target process.", "OK");
                    return;
                }

                WriteProcessMemory(processHandle, allocMemAddress, System.Text.Encoding.Default.GetBytes(dllPath), (uint)((dllPath.Length + 1) * Marshal.SizeOf(typeof(char))), out _);
                CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);

                DisplayAlert("Success", "DLL attached successfully.", "OK");
            }
            catch (Exception ex)
            {
                DisplayAlert("Error", $"Failed to attach DLL: {ex.Message}", "OK");
            }
        }

        private async void AttachLibraryUnix(string libraryPath)
        {
            try
            {
                IntPtr libHandle = dlopen(libraryPath, RTLD_NOW);
                if (libHandle == IntPtr.Zero)
                {
                    var error = Marshal.PtrToStringAnsi(dlerror());
                    await DisplayAlert("Error", $"Failed to load library: {error}", "OK");
                    return;
                }

                await DisplayAlert("Success", "Library loaded successfully on Unix.", "OK");

                // idk if this would work, but i guess it can?
                // i do not own a Mac so i cannot really test this.
                IntPtr exampleFunctionPtr = dlsym(libHandle, "exampleFunction");
                if (exampleFunctionPtr == IntPtr.Zero)
                {
                    await DisplayAlert("Error", "Failed to retrieve function 'exampleFunction'.", "OK");
                }
                else
                {
                    await DisplayAlert("Success", "Function pointer retrieved successfully.", "OK");
                }

                dlclose(libHandle);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to attach library: {ex.Message}", "OK");
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        private const int RTLD_NOW = 2;

#if WINDOWS
        [DllImport("libdl.so")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.so")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl.so")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("libdl.so")]
        private static extern IntPtr dlerror();
#else
        [DllImport("libSystem.B.dylib", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libSystem.B.dylib", EntryPoint = "dlsym")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libSystem.B.dylib", EntryPoint = "dlclose")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("libSystem.B.dylib", EntryPoint = "dlerror")]
        private static extern IntPtr dlerror();
#endif
    }
}