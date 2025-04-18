using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RtspClientSharp;
using SimpleRtspPlayer.GUI.Models;
using SimpleRtspPlayer.GUI.Views;
using FontStyle = System.Drawing.FontStyle;
using Font = System.Drawing.Font;

namespace SimpleRtspPlayer.GUI.ViewModels
{
    class MainWindowViewModel : ObservableObject
    {
        private const string RtspPrefix = "rtsp://";
        private const string HttpPrefix = "http://";
        private const int VIDEOS_PER_PAGE = 4;
        private const int TOTAL_VIDEOS = 10; // 总共10个视频流

        private string _status = string.Empty;
        private readonly IMainWindowModel _mainWindowModel;
        private readonly List<IMainWindowModel> _videoModels = new List<IMainWindowModel>();
        private bool _startButtonEnabled = true;
        private bool _stopButtonEnabled;
        private int _currentPage = 0;
        private int _totalPages;
        private string _pageInfoText = "第1页/共1页";
        private string _deviceAddress = "rtsp://127.0.0.1/stream214";

        // 使用只读属性，不允许用户修改地址
        public string DeviceAddress => _deviceAddress;

        public string Login { get; set; } = "admin";
        public string Password { get; set; } = "123456";

        // 4个视频源
        public IVideoSource VideoSource1 => _videoModels.Count > 0 ? _videoModels[0].VideoSource : null;
        public IVideoSource VideoSource2 => _videoModels.Count > 1 ? _videoModels[1].VideoSource : null;
        public IVideoSource VideoSource3 => _videoModels.Count > 2 ? _videoModels[2].VideoSource : null;
        public IVideoSource VideoSource4 => _videoModels.Count > 3 ? _videoModels[3].VideoSource : null;

        // 使用IRelayCommand接口
        private IRelayCommand _startClickCommand;
        private IRelayCommand _stopClickCommand;
        private IRelayCommand<CancelEventArgs> _closingCommand;
        private IRelayCommand _prevPageCommand;
        private IRelayCommand _nextPageCommand;

        public IRelayCommand StartClickCommand => _startClickCommand;
        public IRelayCommand StopClickCommand => _stopClickCommand;
        public IRelayCommand<CancelEventArgs> ClosingCommand => _closingCommand;
        public IRelayCommand PrevPageCommand => _prevPageCommand;
        public IRelayCommand NextPageCommand => _nextPageCommand;

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string PageInfoText
        {
            get => _pageInfoText;
            set => SetProperty(ref _pageInfoText, value);
        }

        public MainWindowViewModel(IMainWindowModel mainWindowModel)
        {
            _mainWindowModel = mainWindowModel ?? throw new ArgumentNullException(nameof(mainWindowModel));
            
            // 初始化4个视频模型
            _videoModels.Add(_mainWindowModel);
            for (int i = 1; i < VIDEOS_PER_PAGE; i++)
            {
                _videoModels.Add(new MainWindowModel());
            }
            
            // 计算总页数
            _totalPages = (TOTAL_VIDEOS + VIDEOS_PER_PAGE - 1) / VIDEOS_PER_PAGE; // 向上取整
            
            // 创建命令
            _startClickCommand = new RelayCommand(OnStartButtonClick, () => _startButtonEnabled);
            _stopClickCommand = new RelayCommand(OnStopButtonClick, () => _stopButtonEnabled);
            _closingCommand = new RelayCommand<CancelEventArgs>(OnClosing);
            _prevPageCommand = new RelayCommand(OnPrevPageClick, () => _currentPage > 0);
            _nextPageCommand = new RelayCommand(OnNextPageClick, () => _currentPage < _totalPages - 1);
            
            UpdatePageInfo();
        }

        private void OnStartButtonClick()
        {
            string address = _deviceAddress;

            if (!address.StartsWith(RtspPrefix) && !address.StartsWith(HttpPrefix))
                address = RtspPrefix + address;

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri deviceUri))
            {
                MessageBox.Show("无效的设备地址", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var credential = new NetworkCredential(Login, Password);

            // 停止所有视频
            StopAllVideos();

            // 重新计算总页数
            UpdatePageInfo();

            try
            {
                LoadCurrentPageVideos(address, credential);

                Status = "已连接到RTSP服务器";

                _startButtonEnabled = false;
                (_startClickCommand as RelayCommand)?.NotifyCanExecuteChanged();
                _stopButtonEnabled = true;
                (_stopClickCommand as RelayCommand)?.NotifyCanExecuteChanged();
                UpdateCommandsCanExecute();
            }
            catch (Exception ex)
            {
                Status = $"连接错误: {ex.Message}";
                MessageBox.Show($"连接到RTSP服务器时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnStopButtonClick()
        {
            StopAllVideos();
            
            Status = "已停止";

            _stopButtonEnabled = false;
            (_stopClickCommand as RelayCommand)?.NotifyCanExecuteChanged();
            _startButtonEnabled = true;
            (_startClickCommand as RelayCommand)?.NotifyCanExecuteChanged();
            UpdateCommandsCanExecute();
        }

        private void StopAllVideos()
        {
            foreach (var videoModel in _videoModels)
            {
                videoModel.Stop();
                videoModel.StatusChanged -= VideoModelOnStatusChanged;
            }
        }

        private void VideoModelOnStatusChanged(object sender, string s)
        {
            Application.Current.Dispatcher.Invoke(() => Status = s);
        }

        private void OnClosing(CancelEventArgs args)
        {
            StopAllVideos();
        }
        
        private void OnPrevPageClick()
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                UpdatePageInfo();
                ReloadVideos();
            }
        }
        
        private void OnNextPageClick()
        {
            if (_currentPage < _totalPages - 1)
            {
                _currentPage++;
                UpdatePageInfo();
                ReloadVideos();
            }
        }
        
        private void UpdatePageInfo()
        {
            PageInfoText = $"第{_currentPage + 1}页/共{_totalPages}页";
            UpdateCommandsCanExecute();
        }
        
        private void UpdateCommandsCanExecute()
        {
            (_prevPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (_nextPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
        
        private void ReloadVideos()
        {
            try
            {
                // 先停止所有视频
                StopAllVideos();
                
                // 查找VideoView实例并显示加载信息
                var videoViews = FindVideoViews();
                for (int i = 0; i < videoViews.Count; i++)
                {
                    if (videoViews[i] != null)
                    {
                        videoViews[i].SetLoadingInfo($"正在准备加载第{_currentPage + 1}页视频...");
                    }
                }
                
                // 完全释放并重新创建视频模型
                RecreateVideoModels();
                
                // 通知UI属性变更，卸载当前视频组件
                OnPropertyChanged(nameof(VideoSource1));
                OnPropertyChanged(nameof(VideoSource2));
                OnPropertyChanged(nameof(VideoSource3));
                OnPropertyChanged(nameof(VideoSource4));
                
                // 如果处于播放状态，重新开始播放
                if (_stopButtonEnabled)
                {
                    string address = _deviceAddress;
                    
                    if (!address.StartsWith(RtspPrefix) && !address.StartsWith(HttpPrefix))
                        address = RtspPrefix + address;
                    
                    if (!Uri.TryCreate(address, UriKind.Absolute, out Uri deviceUri))
                        return;
                    
                    var credential = new NetworkCredential(Login, Password);
                    
                    try
                    {
                        LoadCurrentPageVideos(address, credential);
                        
                        Status = $"已加载第{_currentPage + 1}页视频";
                    }
                    catch (Exception ex)
                    {
                        Status = $"重新加载视频时出错: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                Status = $"重新加载视频时出错: {ex.Message}";
            }
        }
        
        // 完全释放并重新创建视频模型
        private void RecreateVideoModels()
        {
            try
            {
                // 释放旧模型
                foreach (var model in _videoModels)
                {
                    model.Stop();
                }
                
                // 清空列表
                _videoModels.Clear();
                
                // 创建新模型
                for (int i = 0; i < VIDEOS_PER_PAGE; i++)
                {
                    _videoModels.Add(new MainWindowModel());
                }
                
                Status = "已重新创建视频模型";
            }
            catch (Exception ex)
            {
                Status = $"重新创建视频模型出错: {ex.Message}";
            }
        }

        private void LoadCurrentPageVideos(string address, NetworkCredential credential)
        {
            try
            {
                // 计算当前页显示的视频数量
                int startIndex = _currentPage * VIDEOS_PER_PAGE;
                int count = Math.Min(VIDEOS_PER_PAGE, TOTAL_VIDEOS - startIndex);
                
                // 获取视频控件实例
                var videoViews = FindVideoViews();
                
                // 为每个视频面板设置自定义加载图像
                for (int i = 0; i < count; i++)
                {
                    // 创建自定义加载图像
                    CreateLoadingImage(i, startIndex);
                }
                
                // 延迟500毫秒后开始连接视频，确保加载图像能够显示
                System.Threading.Thread.Sleep(300);
                
                // 为每个视频面板创建连接参数并启动播放
                for (int i = 0; i < count; i++)
                {
                    var videoModel = _videoModels[i];
                    
                    // 根据当前页和索引计算视频ID
                    int videoId = startIndex + i;
                    
                    // 创建对应索引的视频地址
                    string videoAddress = "rtsp://127.0.0.1/stream214";// videoId == 0 ? address : $"{address.TrimEnd('4')}{videoId+1}";
                    
                    // 显示加载信息（如果能找到对应的VideoView实例）
                    if (i < videoViews.Count && videoViews[i] != null)
                    {
                        videoViews[i].SetLoadingInfo($"地址: {videoAddress}\n正在连接...");
                    }
                    
                    // 为每个视频创建独立的连接参数
                    var connectionUri = new Uri(videoAddress);
                    var connectionParameters = !string.IsNullOrEmpty(connectionUri.UserInfo) 
                        ? new ConnectionParameters(connectionUri) 
                        : new ConnectionParameters(connectionUri, credential);

                    connectionParameters.RtpTransport = RtpTransportProtocol.TCP;
                    connectionParameters.CancelTimeout = TimeSpan.FromSeconds(1);

                    // 启动视频
                    videoModel.Start(connectionParameters);
                    videoModel.StatusChanged += VideoModelOnStatusChanged;
                    
                    // 日志输出以便调试
                    Status = $"正在加载视频: {videoAddress}";
                }
                
                // 如果当前页视频数量少于4个，清空剩余的视频面板
                for (int i = count; i < VIDEOS_PER_PAGE; i++)
                {
                    _videoModels[i].Stop();
                    
                    // 显示无视频信息
                    if (i < videoViews.Count && videoViews[i] != null)
                    {
                        videoViews[i].SetLoadingInfo("无视频");
                    }
                    
                    // 为空面板创建背景图像
                    CreateEmptyImage(i);
                }
                
                // 通知UI更新
                OnPropertyChanged(nameof(VideoSource1));
                OnPropertyChanged(nameof(VideoSource2));
                OnPropertyChanged(nameof(VideoSource3));
                OnPropertyChanged(nameof(VideoSource4));
            }
            catch (Exception ex)
            {
                Status = $"加载视频时出错: {ex.Message}";
            }
        }
        
        /// <summary>
        /// 创建自定义加载图像
        /// </summary>
        private void CreateLoadingImage(int panelIndex, int startIndex)
        {
            try
            {
                // 视频编号
                int videoId = startIndex + panelIndex;
                
                // 计算面板尺寸
                int width = 320;
                int height = 240;
                
                // 创建加载提示图片
                Bitmap loadingImage = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(loadingImage))
                {
                    // 填充黑色背景
                    g.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(34, 34, 34)), 
                                  0, 0, width, height);
                    
                    // 绘制视频标题
                    string titleText = $"RTSP视频 #{videoId + 1}";
                    g.DrawString(
                        titleText,
                        new Font("黑体", 12, FontStyle.Bold),
                        new SolidBrush(System.Drawing.Color.FromArgb(0, 255, 255)), // 使用青色
                        10, 10);
                    
                    // 绘制加载中提示（居中显示）
                    string loadingText = "正在连接到RTSP服务器...";
                    Font loadingFont = new Font("黑体", 16, FontStyle.Bold);
                    SizeF textSize = g.MeasureString(loadingText, loadingFont);
                    g.DrawString(
                        loadingText,
                        loadingFont,
                        new SolidBrush(System.Drawing.Color.White),
                        (width - textSize.Width) / 2,
                        (height - textSize.Height) / 2);
                    
                    // 绘制视频地址
                    string addressText = "rtsp://127.0.0.1/stream214";
                    g.DrawString(
                        addressText,
                        new Font("宋体", 9),
                        new SolidBrush(System.Drawing.Color.LightGray),
                        10, 40);
                    
                    // 绘制页面信息
                    string pageInfo = $"第{_currentPage + 1}页 第{panelIndex + 1}个视频";
                    g.DrawString(
                        pageInfo,
                        new Font("宋体", 9),
                        new SolidBrush(System.Drawing.Color.LightGray),
                        10, height - 20);
                }
                
                // 将背景图片应用到模型
                var model = _videoModels[panelIndex] as MainWindowModel;
                if (model != null)
                {
                    var videoSource = model.VideoSource as RealtimeVideoSource;
                    // 如果可以直接设置背景图像，则设置
                    // 注意：这里需要查看RealtimeVideoSource是否有设置背景图像的方法
                    // 如果没有，可能需要扩展它的功能
                }
                
                // 释放资源
                loadingImage.Dispose();
            }
            catch (Exception ex)
            {
                Status = $"创建加载图像出错: {ex.Message}";
            }
        }
        
        /// <summary>
        /// 为空面板创建背景图像
        /// </summary>
        private void CreateEmptyImage(int panelIndex)
        {
            try
            {
                // 计算面板尺寸
                int width = 320;
                int height = 240;
                
                // 创建空视频提示图片
                Bitmap emptyImage = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(emptyImage))
                {
                    // 填充黑色背景
                    g.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(34, 34, 34)), 
                                  0, 0, width, height);
                    
                    // 绘制无视频提示（居中显示）
                    string emptyText = "暂无视频";
                    Font emptyFont = new Font("黑体", 18, FontStyle.Bold);
                    SizeF textSize = g.MeasureString(emptyText, emptyFont);
                    g.DrawString(
                        emptyText,
                        emptyFont,
                        new SolidBrush(System.Drawing.Color.LightGray),
                        (width - textSize.Width) / 2,
                        (height - textSize.Height) / 2);
                }
                
                // 将背景图片应用到模型
                var model = _videoModels[panelIndex] as MainWindowModel;
                if (model != null)
                {
                    var videoSource = model.VideoSource as RealtimeVideoSource;
                    // 如果可以直接设置背景图像，则设置
                }
                
                // 释放资源
                emptyImage.Dispose();
            }
            catch (Exception ex)
            {
                Status = $"创建空面板图像出错: {ex.Message}";
            }
        }

        /// <summary>
        /// 查找界面上的VideoView控件实例
        /// </summary>
        private List<VideoView> FindVideoViews()
        {
            // 由于类型转换问题，简化此方法的实现
            var result = new List<VideoView>();
            
            try
            {
                // 简化实现，避免类型转换错误
                // 实际使用时需要根据具体环境找到正确的方法获取VideoView实例
            }
            catch (Exception ex)
            {
                Status = $"查找VideoView控件出错: {ex.Message}";
            }
            
            return result;
        }
    }
}