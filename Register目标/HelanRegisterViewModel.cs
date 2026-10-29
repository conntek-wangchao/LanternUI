#nullable enable

using HelanTestPlatform.Common.Models;
using HelanTestPlatform.Services;
using HelanTestPlatform.Communication;
using Microsoft.Win32;
using Newtonsoft.Json;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace HelanTestPlatform.ViewModels
{
    public class HelanRegisterViewModel : BindableBase
    {
        private readonly IHelanCommandService _commandService;
        
        public HelanRegisterViewModel (IHelanCommandService commandService)
        {
            _commandService = commandService;
            RegisterMapInit();

            ReadAllRegCommand = new DelegateCommand(ReadAllReg);
            WriteAllRegCommand = new DelegateCommand(WriteAllReg);
            InputConfigCommand = new DelegateCommand(InputConfig);
            OutputConfigCommand = new DelegateCommand(OutputConfig);
        }

        #region 事件响应函数

        private void ReadAllReg ()
        {
            Task.Run(async () =>
            {
                bool isTrue = true;
                foreach ( var registerData in HelanRegMap.Values )
                {
                    try
                    {
                        var response = await _commandService.ReadRegisterWithDataAsync(
                            SelectedWorkStation, 
                            registerData.Address);
                        
                        if (response != null && response.StationRegisterData.Count > 0)
                        {
                            // 对于多工位，检查第一个工位的状态
                            var firstStationData = response.StationRegisterData.First().Value;
                            registerData.Status = firstStationData.Success;
                            registerData.Value = firstStationData.Data;
                        }
                        else if (response != null)
                        {
                            // 向后兼容：使用旧的字段
                            registerData.Status = true;
                            registerData.Value = response.RegisterData;
                        }
                        else
                        {
                            registerData.Status = false;
                        }
                        
                        isTrue &= registerData.Status;
                    }
                    catch (Exception)
                    {
                        registerData.Status = false;
                        isTrue = false;
                        // 可以添加日志记录
                    }
                }
                OperationCheck = isTrue;
            });

        }

        private void WriteAllReg ()
        {
            Task.Run(async () =>
            {
                bool isTrue = true;
                foreach ( var registerData in HelanRegMap.Values )
                {
                    if ( !registerData.IsReadOnly )
                    {
                        try
                        {
                            var response = await _commandService.WriteRegisterWithDataAsync(
                                SelectedWorkStation, 
                                registerData.Address, 
                                registerData.Value);
                            
                            registerData.Status = response?.Success ?? false;
                            isTrue &= registerData.Status;
                        }
                        catch (Exception)
                        {
                            registerData.Status = false;
                            isTrue = false;
                            // 可以添加日志记录
                        }
                    }
                }
                OperationCheck = isTrue;
            });
        }

        private void InputConfig ()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "JSON文件(*.json)|*.json" ,
                Title = "导入配置文件"
            };

            // 使用Dispatcher在UI线程上显示对话框
            Application.Current.Dispatcher.Invoke(() =>
            {
                bool? result = openFileDialog.ShowDialog();
                if ( result == true )
                {
                    // 处理选定的文件路径
                    CurrentConfigFilePath = openFileDialog.FileName;
                    Task.Run(() =>
                    {
                        ImportRegisterMapJson(_currentConfigFilePath);
                    });
                }
            });

        }

        private void OutputConfig ()
        {
            Task.Run(() =>
            {
                ExportDictionaryToJson(_HelanRegMap);
            });
        }

        #endregion

        #region 命令声明

        public DelegateCommand ReadAllRegCommand { get; init; }

        public DelegateCommand WriteAllRegCommand { get; init; }

        public DelegateCommand InputConfigCommand { get; init; }

        public DelegateCommand OutputConfigCommand { get; init; }

        #endregion

        #region 字段（Binding）
        private bool _operationCheck = false;

        private string _currentConfigFilePath = @".\Config\Helan_Config.json";

        private Dictionary<ushort , RegisterInfo> _HelanRegMap = new Dictionary<ushort , RegisterInfo>();
        
        private WorkStation _selectedWorkStation = WorkStation.Station1;
        
        // 工位选择单选按钮状态
        private bool _isStation1Selected = true;
        private bool _isStation2Selected = false;
        private bool _isStation3Selected = false;
        private bool _isStation4Selected = false;
        #endregion

        #region 属性（Binding）
        public bool OperationCheck
        {
            get => _operationCheck;
            set => SetProperty(ref _operationCheck , value);
        }

        public string CurrentConfigFilePath
        {
            get => _currentConfigFilePath;
            set => SetProperty(ref _currentConfigFilePath , value);
        }

        public Dictionary<ushort , RegisterInfo> HelanRegMap
        {
            get => _HelanRegMap;
            set
            {
                SetProperty(ref _HelanRegMap , value);
            }
        }
        
        /// <summary>
        /// 选中的工位
        /// </summary>
        public WorkStation SelectedWorkStation
        {
            get => _selectedWorkStation;
            set => SetProperty(ref _selectedWorkStation, value);
        }
        
        /// <summary>
        /// 工位1是否选中
        /// </summary>
        public bool IsStation1Selected
        {
            get => _isStation1Selected;
            set
            {
                if (SetProperty(ref _isStation1Selected, value) && value)
                {
                    UpdateStationSelection(WorkStation.Station1);
                }
            }
        }
        
        /// <summary>
        /// 工位2是否选中
        /// </summary>
        public bool IsStation2Selected
        {
            get => _isStation2Selected;
            set
            {
                if (SetProperty(ref _isStation2Selected, value) && value)
                {
                    UpdateStationSelection(WorkStation.Station2);
                }
            }
        }
        
        /// <summary>
        /// 工位3是否选中
        /// </summary>
        public bool IsStation3Selected
        {
            get => _isStation3Selected;
            set
            {
                if (SetProperty(ref _isStation3Selected, value) && value)
                {
                    UpdateStationSelection(WorkStation.Station3);
                }
            }
        }
        
        /// <summary>
        /// 工位4是否选中
        /// </summary>
        public bool IsStation4Selected
        {
            get => _isStation4Selected;
            set
            {
                if (SetProperty(ref _isStation4Selected, value) && value)
                {
                    UpdateStationSelection(WorkStation.Station4);
                }
            }
        }
        #endregion

        #region 方法
        private void RegisterMapInit ()
        {
            _HelanRegMap.Clear();
            ImportRegisterMapJson(_currentConfigFilePath);
        }

        /// <summary>
        /// 导入寄存器表JSON文件
        /// </summary>
        /// <param name="filePath">JSON文件路径</param>
        private void ImportRegisterMapJson (string filePath)
        {
            try
            {
                string fileNamePattern = @"(?<=[\\])[a-zA-Z-_]+([.][j][s][o][n])$";
                string fileName = Regex.Match(filePath , fileNamePattern).Value;
                object temp = JsonConvert.DeserializeObject(File.ReadAllText(filePath) , typeof(Dictionary<ushort , RegisterInfo>));

                if ( temp != null )
                {
                    HelanRegMap = (Dictionary<ushort , RegisterInfo>) temp;

                    foreach ( RegisterInfo registerInfo in HelanRegMap.Values )
                    {
                        // 为RegisterInfo注入通信服务
                        var fieldInfo = typeof(RegisterInfo).GetField("_commandService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        fieldInfo?.SetValue(registerInfo, _commandService);
                        
                        // 为RegisterInfo设置工位提供者
                        registerInfo.SetWorkStationProvider(() => _selectedWorkStation);
                        
                        foreach ( RegisterBitsInfo bitsInfo in registerInfo.RegisterBitsList )
                        {
                            bitsInfo.RegisterInfo = registerInfo;
                            // 为RegisterBitsInfo注入通信服务
                            var bitsFieldInfo = typeof(RegisterBitsInfo).GetField("_commandService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            bitsFieldInfo?.SetValue(bitsInfo, _commandService);
                            
                            // 为RegisterBitsInfo设置工位提供者
                            bitsInfo.SetWorkStationProvider(() => _selectedWorkStation);
                            
                            for ( byte i = 0 ; i < bitsInfo.BitNum ; i++ )
                            {
                                var bitInfo = new RegisterBitInfo((byte) ( bitsInfo.BitNum - 1 - i ) , (byte) ( ( bitsInfo.Value >> ( bitsInfo.BitNum - 1 - i ) ) & 0x01 ), _commandService);
                                bitsInfo.RegisterBitList.Add(bitInfo);
                                bitsInfo.RegisterBitList[i].RegisterBitsInfo = bitsInfo;
                                
                                // 为RegisterBitInfo设置工位提供者
                                bitsInfo.RegisterBitList[i].SetWorkStationProvider(() => _selectedWorkStation);
                            }
                        }
                    }
                }
            }
            catch ( Exception ex )
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ExportDictionaryToJson<T1, T2> (Dictionary<T1 , T2> dic)
        {
            try
            {
                //保存信息到JSON文件
                if ( dic != null && dic.Count != 0 )
                {
                    //JSON序列化
                    string jsonStr = JsonConvert.SerializeObject(dic);

                    //输出文件
                    SaveFileDialog dialog = new SaveFileDialog
                    {
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) ,
                        Filter = "JSON文件(*.json)|*.json" ,
                        RestoreDirectory = true ,
                        Title = "导出配置文件"
                    };

                    if ( dialog.ShowDialog() == true )
                    {
                        using ( StreamWriter outputFile = new StreamWriter(dialog.FileName) )
                        {
                            outputFile.Write(jsonStr);
                        }
                    }
                }
            }
            catch ( Exception ex )
            {
                MessageBox.Show(ex.Message);
            }
        }
        
        /// <summary>
        /// 更新工位选择（确保单选）
        /// </summary>
        /// <param name="selectedStation">选中的工位</param>
        private void UpdateStationSelection(WorkStation selectedStation)
        {
            // 更新所有单选按钮状态
            _isStation1Selected = selectedStation == WorkStation.Station1;
            _isStation2Selected = selectedStation == WorkStation.Station2;
            _isStation3Selected = selectedStation == WorkStation.Station3;
            _isStation4Selected = selectedStation == WorkStation.Station4;
            
            // 更新SelectedWorkStation
            _selectedWorkStation = selectedStation;
            
            // 通知属性变更
            RaisePropertyChanged(nameof(IsStation1Selected));
            RaisePropertyChanged(nameof(IsStation2Selected));
            RaisePropertyChanged(nameof(IsStation3Selected));
            RaisePropertyChanged(nameof(IsStation4Selected));
            RaisePropertyChanged(nameof(SelectedWorkStation));
        }
        #endregion
    }
}
