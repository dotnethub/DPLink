﻿using DipesLink.Models;
using DipesLink.ViewModels;
using DipesLink.Views.Extension;
using DipesLink.Views.SubWindows;
using SharedProgram.Models;
using SharedProgram.Shared;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using static SharedProgram.DataTypes.CommonDataType;
using System.Data;

namespace DipesLink.Views.UserControls.MainUc
{
    /// <summary>
    /// Interaction logic for HomeUc.xaml
    /// </summary>
    public partial class JobDetails : UserControl
    {

        PrintingDataTableHelper _printingDataTableHelper = new();
       
        JobOverview? _currentJob;
        ConcurrentQueue<string[]> _queueCheckedCode = new();
        private CheckedObserHelper _checkedObserHelper = new();
        int count = 0;
        public JobDetails()
        {
            InitializeComponent();
            Loaded += StationDetailUc_Loaded;
            InitValues();
            Task.Run(() => { TaskAddDataAsync(); });
            Task.Run(() => { TaskChangePrintStatusAsync(); });
            Debug.WriteLine("JobDetails !" + ++count);
        }


        public void InitValues()
        {
            TextBlockTotalChecked.Text = "0";
            TextBlockTotalPassed.Text = "0";
            TextBlockTotalFailed.Text = "0";
        }
        public async void StationDetailUc_Loaded(object sender, RoutedEventArgs e)
        {
            EventRegister();
            ViewModelSharedEvents.OnJobDetailChangeHandler(_currentJob.Index);
            if (!_currentJob.IsDBExist)
            {
                Debug.WriteLine("Event load database was called: " + _currentJob.Index);
                await PerformLoadDbAfterDelay();
            }

        }
        private async Task PerformLoadDbAfterDelay()
        {
            await Task.Delay(1000); // waiting for 3s connection completed
            _currentJob?.RaiseLoadDb(_currentJob.Index);
        }
        public void EventRegister()
        {
            try
            {
                if (_currentJob == null)
                {
                    _currentJob = CurrentViewModel<JobOverview>();
                    if (_currentJob == null) return;
                    //_currentJob.OnLoadCompleteDatabase -= Shared_OnLoadCompleteDatabase;
                    //_currentJob.OnChangePrintedCode -= Shared_OnChangePrintedCode;
                    //_currentJob.OnLoadCompleteCheckedDatabase -= Shared_OnLoadCompleteCheckedDatabase;
                    //_currentJob.OnChangeCheckedCode -= Shared_OnChangeCheckedCode;

                    _currentJob.OnLoadCompleteDatabase += Shared_OnLoadCompleteDatabase;
                    _currentJob.OnChangePrintedCode += Shared_OnChangePrintedCode;
                    _currentJob.OnLoadCompleteCheckedDatabase += Shared_OnLoadCompleteCheckedDatabase;
                    _currentJob.OnChangeCheckedCode += Shared_OnChangeCheckedCode;
                }
                _printingDataTableHelper.OnDetectMissPrintedCode += _printingDataTableHelper_OnDetectMissPrintedCode;
            }
            catch (Exception) { }
        }

        private void _printingDataTableHelper_OnDetectMissPrintedCode(object? sender, EventArgs e)
        {
          //  _currentJob?.OnStopButtonCommandClick(); // Stop Printer while miss Printed code (this is option)
        }

        #region VIEWMODEL HANDLER

        public void CallbackCommand(Action<MainViewModel> execute)
        {
            try
            {
                if (DataContext is MainViewModel model)
                {
                    execute?.Invoke(model);
                }
                else
                {
                    return;
                }
            }
            catch (Exception)
            {
                return;
            }
        }
        private T? CurrentViewModel<T>() where T : class
        {
            if (DataContext is T viewModel)
            {
                return viewModel;
            }
            else
            {
                return null;
            }
        }

        #endregion VIEWMODEL HANDLER

        #region DATAGRID FOR DATABASE

        /// <summary>
        /// Detect Firstload Database
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Shared_OnLoadCompleteDatabase(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                if (sender is List<(List<string[]>, int)> dbList)  // Item 1: db, item 2: current page
                {
                    await _printingDataTableHelper.InitDatabaseAsync(dbList.FirstOrDefault().Item1, DataGridDB, dbList.FirstOrDefault().Item2, CurrentViewModel<JobOverview>());
                    if (_currentJob != null) _currentJob.PrintedDataNumber = _printingDataTableHelper.PrintedNumber.ToString(); // Update UI First time
                }
            });
        }

        ConcurrentQueue<string[]> _queuePrintedCode = new();

        /// <summary>
        /// Detect new Printed Code
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Shared_OnChangePrintedCode(object? sender, EventArgs e)
        {
            if (sender is string[] printedCode)
            {
                _queuePrintedCode.Enqueue(printedCode);
            }
        }
        private void TaskChangePrintStatusAsync()
        {
            Application.Current.Dispatcher.Invoke(new Action(async () =>
            {
                while (true)
                {
                    if (_queuePrintedCode.TryDequeue(out var code))
                    {
                        _printingDataTableHelper.ChangeStatusOnDataGrid(code, CurrentViewModel<JobOverview>(), DataGridDB);
                    }
                    await Task.Delay(5);
                }
            }));

        }

        #endregion DATAGRID FOR AFTER PRODUCTION

        #region DATAGRID FOR CHECKED CODE

        /// <summary>
        /// First load checked DB
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Shared_OnLoadCompleteCheckedDatabase(object? sender, EventArgs e)
        {
            var listChecked = sender as List<string[]>;
            if (listChecked != null)
            {
                Application.Current?.Dispatcher.Invoke(new Action(() =>
                {
                    DataGridResult.AutoGenerateColumns = false;
                    _checkedObserHelper.ConvertListToObservableCol(listChecked);
                    _checkedObserHelper.CreateDataTemplate(DataGridResult);
                    _checkedObserHelper.TakeFirtloadCollection();
                    DataGridResult.ItemsSource = _checkedObserHelper.DisplayList;
                    UpdateCheckedNumber();
                    FirtLoadForChartPercent();
                }));
            }
        }

        private void FirtLoadForChartPercent()
        {
            if (_currentJob != null)
            {
                _currentJob.TotalChecked = _checkedObserHelper.TotalChecked.ToString();
                _currentJob.TotalPassed = _checkedObserHelper.TotalPassed.ToString();
                _currentJob.TotalFailed = _checkedObserHelper.TotalFailed.ToString();
                _currentJob.RaisePercentageChange(_currentJob.Index);
            }
        }

        /// <summary>
        /// Detect new checked code 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Shared_OnChangeCheckedCode(object? sender, EventArgs e)
        {
            if (sender is string[] checkedCode && checkedCode != null)
            {
                _queueCheckedCode.Enqueue(checkedCode);
            }
        }

        private void TaskAddDataAsync()
        {
            Application.Current?.Dispatcher.Invoke(async () =>
            {
                while (true)
                {
                    try
                    {
                        if (_queueCheckedCode.TryDequeue(out string[]? result))
                        {
                            if (result != null)
                            {
                                _checkedObserHelper.AddNewData(result);
                                UpdateCheckedNumber();
                            }
                        }
                    }
                    catch (Exception) { }
                    await Task.Delay(5);
                }
            });

        }

        private void UpdateCheckedNumber()
        {
            TextBlockTotalChecked.Text = _checkedObserHelper.TotalChecked.ToString();
            TextBlockTotalPassed.Text = _checkedObserHelper.TotalPassed.ToString();
            TextBlockTotalFailed.Text = _checkedObserHelper.TotalFailed.ToString();
        }


        #endregion DATAGRID FOR CHECKED CODE

        private void ViewChekedResult_PreMouDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (_currentJob == null) return;
             
                JobLogsWindow jobLogsWindow = new(_checkedObserHelper)
                {
                    DataContext = DataContext as JobOverview,
                   // CheckedDataTable = _checkedObserHelper?.GetDataTableDB().Copy(),
                    Num_TotalChecked = _currentJob.TotalRecDb
                };

                if (int.TryParse(_currentJob.PrintedDataNumber, out int printed))
                {
                    jobLogsWindow.Num_Printed = printed;
                }

        
                if (int.TryParse(TextBlockTotalChecked.Text, out int totalChecked))
                {
                    jobLogsWindow.Num_Verified = totalChecked;
                    if (int.TryParse(TextBlockTotalFailed.Text, out int failed))
                    {
                        jobLogsWindow.Num_Failed = failed;
                        jobLogsWindow.Num_Valid = totalChecked - failed;
                    }
                }
                jobLogsWindow.ShowDialog();
            }
            catch (Exception)
            {
            }
        }

        private void PrintedData_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (_currentJob == null) return;
              
                    PrintedLogsWindow printedLogsWindow = new(_currentJob);
                    printedLogsWindow.ShowDialog();
              
              
            }
            catch (Exception)
            {
            }
        }

     
       

        //private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        //{
        //    _currentJob?.OnStopButtonCommandClick();
        //}
    }
}
