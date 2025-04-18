using SimpleRtspPlayer.GUI.Models;
using SimpleRtspPlayer.GUI.Views;
using System;
using System.Windows;

namespace SimpleRtspPlayer.GUI.ViewModels
{
    /// <summary>
    /// 创建MainWindow以及关联的ViewModel和Model的工厂类
    /// </summary>
    public static class MainWindowFactory
    {
        /// <summary>
        /// 创建并显示一个具有独立ViewModel和Model的MainWindow
        /// </summary>
        /// <returns>返回创建的MainWindow实例</returns>
        public static MainWindow CreateAndShow()
        {
            try
            {
                // 创建独立的Model和ViewModel
                var model = new MainWindowModel();
                var viewModel = new MainWindowViewModel(model);

                // 创建MainWindow并设置ViewModel
                var window = new MainWindow();
                window.DataContext = viewModel;
                
                // 设置窗口编号
                int windowCount = Application.Current.Windows.Count;
                window.Title = $"RTSP多视频播放器 - 实例 {windowCount}";
                
                // 显示窗口
                window.Show();
                
                return window;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }
    }
} 