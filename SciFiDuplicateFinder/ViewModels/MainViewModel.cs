using SciFiDuplicateFinder.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SciFiDuplicateFinder.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly FileScanner _scanner;
        private CancellationTokenSource _cts;

        private int _progressValue;
        private string _currentFile;
        private bool _isScanning;
        private string _selectedPath;

        public ObservableCollection<DuplicateFileInfo> Duplicates { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel()
        {
            _scanner = new FileScanner();
            Duplicates = new ObservableCollection<DuplicateFileInfo>();

            // Watch collection changes so we can update DuplicatesCount
            Duplicates.CollectionChanged += OnDuplicatesChanged;

            ScanCommand = new RelayCommand(async _ => await ScanAsync(),
                                           _ => !IsScanning && !string.IsNullOrWhiteSpace(SelectedPath));

            StopCommand = new RelayCommand(_ => StopScan(),
                                           _ => IsScanning);

            ExportCommand = new RelayCommand(_ => ExportToCSV(),
                                             _ => !IsScanning && Duplicates.Count > 0);

            DeleteCommand = new RelayCommand<DuplicateFileInfo>(DeleteFile);
        }

        #region Properties

        public string SelectedPath
        {
            get => _selectedPath;
            set
            {
                _selectedPath = value;
                OnPropertyChanged();
                (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged();
            }
        }

        public string CurrentFile
        {
            get => _currentFile;
            set
            {
                _currentFile = value;
                OnPropertyChanged();
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // New: show number of duplicates in UI
        private int _duplicatesCount;
        public int DuplicatesCount
        {
            get => _duplicatesCount;
            private set
            {
                _duplicatesCount = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Commands

        public ICommand ScanCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand DeleteCommand { get; }

        private async Task ScanAsync()
        {
            IsScanning = true;
            Duplicates.Clear();
            CurrentFile = string.Empty;
            ProgressValue = 0;
            _cts = new CancellationTokenSource();

            try
            {
                var token = _cts.Token;

                var progressPartialDuplicates = new Progress<IReadOnlyList<DuplicateFileInfo>>(newDuplicates =>
                {
                    // This callback runs on the UI thread automatically
                    foreach (var dup in newDuplicates)
                    {
                        Duplicates.Add(dup);
                    }
                });

                var progressFile = new Progress<string>(f => CurrentFile = $"Processing: {f}");
                var progressVal = new Progress<int>(val => ProgressValue = val);

                // Run in background
                await Task.Run(() =>
                {
                    _scanner.FindDuplicates(
                        SelectedPath,
                        progressPartialDuplicates, // <--- new
                        progressFile,
                        progressVal,
                        token
                    );
                });

                if (!token.IsCancellationRequested)
                {
                    MessageBox.Show("Scan Completed!", "Duplicate Finder");
                }
            }
            catch (OperationCanceledException)
            {
                // swallow if canceled
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Duplicate Finder");
            }
            finally
            {
                CurrentFile = "";
                IsScanning = false;
                _cts.Dispose();
            }
        }

        private void StopScan()
        {
            _cts?.Cancel();
            CurrentFile = "Stopping... Please wait.";
        }

        private void ExportToCSV()
        {
            if (Duplicates.Count == 0)
            {
                MessageBox.Show("No results to export.");
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                using var sw = new StreamWriter(sfd.FileName);
                sw.WriteLine("File Path,File Name,File Size");
                foreach (var d in Duplicates)
                {
                    sw.WriteLine($"\"{d.FolderPath}\",\"{d.FileName}\",\"{d.FileSize}\"");
                }
                MessageBox.Show("Export Complete!", "Export");
            }
        }

        private void DeleteFile(DuplicateFileInfo fileInfo)
        {
            if (fileInfo == null) return;

            string path = fileInfo.FullPath;
            var confirm = MessageBox.Show(
                $"Are you sure you want to delete:\n{path}?",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(path);
                    Duplicates.Remove(fileInfo);
                    MessageBox.Show($"File deleted successfully:\n{path}", "Deletion");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error deleting file: " + ex.Message, "Error");
                }
            }
        }

        #endregion

        #region INotifyPropertyChanged

        private void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        #endregion

        // When Duplicates changes, recalc DuplicatesCount
        private void OnDuplicatesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            DuplicatesCount = Duplicates.Count;
        }
    }
}
