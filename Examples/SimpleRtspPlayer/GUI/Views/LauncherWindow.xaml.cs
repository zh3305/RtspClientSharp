using SimpleRtspPlayer.GUI.ViewModels;
using System.Windows;

namespace SimpleRtspPlayer.GUI.Views
{
    /// <summary>
    /// LauncherWindow.xaml 的交互逻辑
    /// </summary>
    public partial class LauncherWindow : Window
    {
        public LauncherWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 处理启动按钮点击事件，打开一个新的MainWindow窗口
        /// </summary>
        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            // 使用工厂创建并显示MainWindow
            MainWindowFactory.CreateAndShow();
        }
    }
} 