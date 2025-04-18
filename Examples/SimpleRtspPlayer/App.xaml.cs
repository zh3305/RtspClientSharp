using System.Windows;
using SimpleRtspPlayer.GUI.ViewModels;

namespace SimpleRtspPlayer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 初始化MainWindowFactory，确保类被加载
            var factoryType = typeof(MainWindowFactory);
        }
    }
}